using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using PortableDictionary.Core;

namespace PortableDictionary.Services;

public sealed class SelectedTextCapture
{
    private readonly CaptureCoordinator _coordinator = new();

    public async Task<CaptureResult> CaptureAsync(IntPtr foregroundWindow, Keys shortcutKey = Keys.D)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "No originating window was available.");
        }

        try
        {
            // Keep the first UIA request alive through Copy and initialization
            // retries. UIA uses MTA; the OLE clipboard uses its own STA worker.
            return await _coordinator.CaptureAsync(
                () => StartOnThread(() => CaptureWithAutomation(foregroundWindow), ApartmentState.MTA),
                () => CaptureWithFallbacksAsync(foregroundWindow, shortcutKey),
                () => NativeMethods.GetForegroundWindow() == foregroundWindow);
        }
        catch (Exception ex)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: ex.Message);
        }
    }

    private static CaptureResult CaptureWithAutomation(IntPtr foregroundWindow)
    {
        try
        {
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                return new CaptureResult(CaptureStatus.Unavailable, Detail: "The originating window changed before capture.");
            }

            using var automation = new UIA3Automation();
            var source = automation.FromHandle(foregroundWindow);
            var focused = automation.FocusedElement();
            var ancestors = new List<AutomationElement>();
            var belongsToSource = false;
            for (var element = focused; element is not null && ancestors.Count < 32; element = element.Parent)
            {
                ancestors.Add(element);
                if (element.Equals(source))
                {
                    belongsToSource = true;
                    break;
                }
            }
            if (!belongsToSource)
            {
                return new CaptureResult(CaptureStatus.Unavailable, Detail: "The focused control no longer belongs to the originating window.");
            }

            // Browser/editor focus can be on a child node while its document or
            // edit-control ancestor owns TextPattern. Stop at the source window.
            foreach (var element in ancestors)
            {
                if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return new CaptureResult(CaptureStatus.Unavailable, Detail: "The originating window changed during capture.");
                }
                try
                {
                    if (!element.Patterns.Text.IsSupported)
                    {
                        continue;
                    }
                    var result = TextCaptureReader.ReadRanges(
                        element.Patterns.Text.Pattern.GetSelection(),
                        range => range.GetText(TextNormalization.MaximumQueryLength + 1),
                        ReadWordAtCaret);
                    if (result.Status is CaptureStatus.Success or CaptureStatus.TooLong)
                    {
                        return NativeMethods.GetForegroundWindow() == foregroundWindow
                            ? result
                            : new CaptureResult(CaptureStatus.Unavailable, Detail: "The originating window changed during capture.");
                    }
                }
                catch (Exception)
                {
                    // A broken child provider must not prevent trying its parent
                    // or the guarded clipboard fallback.
                }
            }
            return new CaptureResult(CaptureStatus.NoSelection);
        }
        catch (Exception)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "The focused control did not expose its selection through UI Automation.");
        }
    }

    private static string ReadWordAtCaret(ITextRange caret)
    {
        // Expanding a cloned range reads the word without selecting it, moving
        // the insertion point, or changing the originating document.
        var word = caret.Clone();
        word.ExpandToEnclosingUnit(TextUnit.Word);
        return word.GetText(TextNormalization.MaximumQueryLength + 1);
    }

    private static async Task<CaptureResult> CaptureWithFallbacksAsync(IntPtr source, Keys shortcutKey)
    {
        // Native providers can be slow or broken. Give their read-only worker a
        // head start, but never let it block the independent clipboard fallback.
        var nativeTask = StartOnThread(() => NativeTextCapture.Capture(source), ApartmentState.STA);
        try
        {
            var native = await nativeTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            if (native.Status is CaptureStatus.Success or CaptureStatus.TooLong) return native;
        }
        catch (Exception) { /* A timeout or unavailable provider still permits Copy. */ }

        var copied = await StartOnThread(() => CaptureWithCopy(source, shortcutKey), ApartmentState.STA).ConfigureAwait(false);
        if (copied.Status is CaptureStatus.Success or CaptureStatus.TooLong) return copied;
        if (nativeTask.IsCompletedSuccessfully && nativeTask.Result.Status is CaptureStatus.Success or CaptureStatus.TooLong)
            return nativeTask.Result;
        return copied;
    }

    private static CaptureResult CaptureWithCopy(IntPtr foregroundWindow, Keys shortcutKey)
    {
        if (NativeMethods.GetForegroundWindow() != foregroundWindow)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "The fallback copy command was cancelled because focus changed.");
        }

        IDataObject? previousData = null;
        try { previousData = Clipboard.GetDataObject(); } catch (ExternalException) { }
        var before = CurrentClipboardStamp();
        ClipboardStamp? captured = null;
        try
        {
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                return new CaptureResult(CaptureStatus.Unavailable);
            }
            if (SendNativeCopyCommand(foregroundWindow))
            {
                captured = WaitForClipboardChange(before, 600);
            }
            if (captured is null)
            {
                if (!WaitForShortcutRelease(shortcutKey) || NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return new CaptureResult(CaptureStatus.Unavailable, Detail: "The keyboard copy fallback was cancelled.");
                }
                before = CurrentClipboardStamp();
                if (!SendCopyCommand())
                {
                    return new CaptureResult(CaptureStatus.Unavailable, Detail: "Windows did not accept the fallback copy command.");
                }
                captured = WaitForClipboardChange(before, 1500);
            }
            if (captured is null)
            {
                return new CaptureResult(CaptureStatus.NoSelection);
            }
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var sourceProcess);
            NativeMethods.GetWindowThreadProcessId(captured.Value.Owner, out var clipboardProcess);
            if (sourceProcess == 0 || sourceProcess != clipboardProcess)
            {
                captured = null;
                return new CaptureResult(CaptureStatus.Unavailable, Detail: "The clipboard copy was not owned by the originating application.");
            }

            for (var attempt = 0; attempt < 25; attempt++)
            {
                var read = ClipboardTextReader.Read(new NativeClipboardTextSource(), captured.Value);
                if (read.Stamp is { } rendered)
                {
                    captured = rendered;
                    return NativeMethods.GetForegroundWindow() == foregroundWindow && CurrentClipboardStamp() == rendered
                        ? read.Result : new CaptureResult(CaptureStatus.Unavailable);
                }
                if (CurrentClipboardStamp() != captured.Value)
                {
                    // Do not read or restore a newer Copy performed elsewhere.
                    captured = null;
                    return read.Result;
                }
                Thread.Sleep(20);
            }
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "The clipboard could not be read.");
        }
        finally
        {
            RestoreClipboardIfUnchanged(previousData, captured);
        }
    }

    private static ClipboardStamp CurrentClipboardStamp() =>
        new(NativeMethods.GetClipboardSequenceNumber(), NativeMethods.GetClipboardOwner());

    private static ClipboardStamp? WaitForClipboardChange(ClipboardStamp before, int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        ClipboardStamp? last = null;
        long stableSince = 0;
        while (Environment.TickCount64 < deadline)
        {
            var stamp = CurrentClipboardStamp();
            if (stamp != before)
            {
                if (last == stamp && Environment.TickCount64 - stableSince >= 30) return stamp;
                if (last != stamp)
                {
                    last = stamp;
                    stableSince = Environment.TickCount64;
                }
            }
            Thread.Sleep(15);
        }
        return null;
    }

    private static void RestoreClipboardIfUnchanged(IDataObject? previousData, ClipboardStamp? captured)
    {
        if (previousData is null || captured is null || CurrentClipboardStamp() != captured.Value)
        {
            return;
        }
        try { Clipboard.SetDataObject(previousData, true); } catch (Exception) { /* Restoration is best-effort only. */ }
    }

    private sealed class NativeClipboardTextSource : IClipboardTextSource
    {
        public bool TryOpen() => NativeMethods.OpenClipboard(IntPtr.Zero);
        public void Close() => NativeMethods.CloseClipboard();
        public ClipboardStamp Stamp => CurrentClipboardStamp();
        public bool HasUnicodeText => NativeMethods.IsClipboardFormatAvailable(13);
        public string? ReadUnicodeText(int maximumCharacters)
        {
            var handle = NativeMethods.GetClipboardData(13);
            if (handle == IntPtr.Zero) return null;
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                var count = (int)Math.Min((ulong)maximumCharacters, NativeMethods.GlobalSize(handle).ToUInt64() / 2);
                if (count <= 0) return null;
                var text = Marshal.PtrToStringUni(pointer, count);
                var end = text?.IndexOf('\0') ?? -1;
                return end >= 0 ? text![..end] : text;
            }
            finally { NativeMethods.GlobalUnlock(handle); }
        }
    }

    private static bool WaitForShortcutRelease(Keys shortcutKey)
    {
        var deadline = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < deadline)
        {
            if (!IsDown(0x11) && !IsDown(0x10) && !IsDown(0x12) && !IsDown(0x5B) && !IsDown(0x5C) &&
                !IsDown((int)(shortcutKey & Keys.KeyCode)))
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return false;
    }

    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool SendNativeCopyCommand(IntPtr foregroundWindow)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        if (threadId == 0)
        {
            return false;
        }

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };
        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        var target = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : foregroundWindow;
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(target, className, className.Capacity);
        if (!className.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase) &&
            !className.ToString().StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        const uint wmCopy = 0x0301;
        const uint abortIfHungAndBlock = 0x0003;
        return NativeMethods.SendMessageTimeout(
            target, wmCopy, IntPtr.Zero, IntPtr.Zero,
            abortIfHungAndBlock, 300, out _) != IntPtr.Zero;
    }

    private static bool SendCopyCommand()
    {
        var inputs = new[]
        {
            KeyInput(0x11, false), KeyInput(0x43, false),
            KeyInput(0x43, true), KeyInput(0x11, true)
        };
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == (uint)inputs.Length;
    }

    private static NativeMethods.INPUT KeyInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = 1,
        Union = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KEYBDINPUT
            {
                VirtualKey = virtualKey,
                ScanCode = 0,
                Flags = keyUp ? 2u : 0u,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    private static Task<T> StartOnThread<T>(Func<T> action, ApartmentState apartment)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(apartment);
        thread.Start();

        return completion.Task;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetClipboardData(uint format);
        [DllImport("kernel32.dll")]
        internal static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")]
        internal static extern UIntPtr GlobalSize(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder name, int count);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO threadInfo);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeoutMilliseconds, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT caretBounds;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint Type;
            public InputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT Mouse;
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
            [FieldOffset(0)] public HARDWAREINPUT Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            public uint Message;
            public ushort ParamL;
            public ushort ParamH;
        }
    }
}
