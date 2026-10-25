using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using PortableDictionary.Core;

namespace PortableDictionary.Services;

public enum CaptureStatus
{
    Success,
    NoSelection,
    TooLong,
    Unavailable
}

/// <param name="TextExposed">
/// True when the focused control exposed its text through UI Automation, so a highlighted selection
/// would have been visible there. The keyboard copy fallback is then unnecessary and is skipped.
/// </param>
public sealed record CaptureResult(CaptureStatus Status, string? Text = null, string? Detail = null, bool TextExposed = false);

/// <summary>
/// Finds the text the lookup shortcut should act on. In order of preference this is the highlighted
/// selection in the focused control, the word under the mouse pointer when the pointer rests on text
/// of the originating window, and the word at the text cursor (caret) of the focused control. All three
/// are read through UI Automation. Applications that do not expose their text through UI Automation
/// still get the guarded Copy fallback, which can only see a highlighted selection.
/// </summary>
public sealed class SelectedTextCapture
{
    private const int AutomationBudgetMilliseconds = 1800;
    private const int AutomationTimeoutMilliseconds = 2200;
    private const int CopyTimeoutMilliseconds = 3000;
    private const int MaximumAutomationAttempts = 3;
    private const int RetryDelayMilliseconds = 150;
    private const int MaximumAncestorDepth = 12;
    private const int PointerToleranceInPixels = 2;

    /// <summary>
    /// True when the window belongs to this process, for example when the shortcut is pressed while
    /// Dictionary itself is the active window.
    /// </summary>
    public static bool BelongsToCurrentProcess(IntPtr window)
    {
        return window != IntPtr.Zero &&
               NativeMethods.GetWindowThreadProcessId(window, out var processId) != 0 &&
               processId == (uint)Environment.ProcessId;
    }

    public async Task<CaptureResult> CaptureAsync(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "No originating window was available.");
        }

        try
        {
            var automationAttempt = await RunSta(() => CaptureWithAutomation(foregroundWindow), AutomationTimeoutMilliseconds);
            var allowKeyboardCopy = true;
            if (!automationAttempt.TimedOut)
            {
                var automationResult = automationAttempt.Value!;
                if (automationResult.Status is CaptureStatus.Success or CaptureStatus.TooLong)
                {
                    return automationResult;
                }
                // When the focused control exposed its text and had no selection, a simulated Ctrl+C cannot
                // find one either. It could instead act as a command, for example interrupting a console program.
                allowKeyboardCopy = !automationResult.TextExposed;
            }

            // A slow or unavailable UI Automation provider must not prevent the guarded Copy fallback.
            var copyAttempt = await RunSta(() => CaptureWithCopy(foregroundWindow, allowKeyboardCopy), CopyTimeoutMilliseconds);
            return copyAttempt.TimedOut
                ? new CaptureResult(CaptureStatus.Unavailable, Detail: "Clipboard fallback capture timed out.")
                : copyAttempt.Value!;
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

            var deadline = Environment.TickCount64 + AutomationBudgetMilliseconds;
            var pointer = PointerOverWindow(foregroundWindow);
            using var automation = new UIA3Automation();

            for (var attempt = 1; ; attempt++)
            {
                var sawText = false;
                var pointerUnsettled = false;

                // 1. A highlighted selection in the focused control always wins.
                var focused = Try(() => automation.FocusedElement());
                var focusedText = focused is null ? null : FindTextPattern(focused);
                ITextRange[]? selection = null;
                if (focusedText is not null)
                {
                    sawText = true;
                    var text = focusedText;
                    selection = Try(() => text.GetSelection());
                    var selected = JoinSelection(selection);
                    if (selected is not null)
                    {
                        return ClassifyCapturedText(selected);
                    }
                }

                // 2. The word under the mouse pointer, when the pointer rests on text of the originating window.
                if (pointer is { } point)
                {
                    var pointerWord = WordUnderPointer(automation, point, attempt, out var pointerText);
                    sawText |= pointerText;
                    if (pointerWord is not null)
                    {
                        return ClassifyCapturedText(pointerWord);
                    }
                    pointerUnsettled = pointerText && attempt == 1;
                }

                // 3. The word at the text cursor of the focused control.
                if (focused is not null && focusedText is not null)
                {
                    var caretWord = WordAtCaret(focused, selection);
                    if (caretWord is not null)
                    {
                        return ClassifyCapturedText(caretWord);
                    }
                }

                // Browsers and some other applications build their accessibility tree only after a UI Automation
                // client first asks for it, so the first query after launch may see no text at all, or text whose
                // character positions are not final yet. Retry briefly in those cases instead of giving up.
                var retry = attempt < MaximumAutomationAttempts && (!sawText || pointerUnsettled) &&
                            Environment.TickCount64 + RetryDelayMilliseconds < deadline;
                if (!retry)
                {
                    return new CaptureResult(CaptureStatus.NoSelection, TextExposed: focusedText is not null);
                }
                Thread.Sleep(RetryDelayMilliseconds);
                if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return new CaptureResult(CaptureStatus.Unavailable, Detail: "The originating window changed before capture.");
                }
            }
        }
        catch (Exception)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "The focused control did not expose its text through UI Automation.");
        }
    }

    /// <summary>
    /// The mouse pointer position when the pointer is over the originating window (or another window of the
    /// same application), otherwise null. Physical coordinates are used because UI Automation reports physical
    /// pixels regardless of this process's DPI awareness.
    /// </summary>
    private static Point? PointerOverWindow(IntPtr sourceWindow)
    {
        if (!NativeMethods.GetPhysicalCursorPos(out var position))
        {
            return null;
        }
        var windowUnderPointer = NativeMethods.WindowFromPhysicalPoint(position);
        if (windowUnderPointer == IntPtr.Zero)
        {
            return null;
        }
        var sameWindow = NativeMethods.GetAncestor(windowUnderPointer, NativeMethods.GaRoot) == sourceWindow;
        if (!sameWindow && !SameProcess(windowUnderPointer, sourceWindow))
        {
            return null;
        }
        return new Point(position.X, position.Y);
    }

    private static bool SameProcess(IntPtr first, IntPtr second)
    {
        return NativeMethods.GetWindowThreadProcessId(first, out var firstProcess) != 0 &&
               NativeMethods.GetWindowThreadProcessId(second, out var secondProcess) != 0 &&
               firstProcess == secondProcess;
    }

    /// <summary>
    /// The Text pattern of the element or of its nearest ancestor that supports one. Browsers, for example,
    /// place the pointer on a text leaf or a link while the pattern may live on the enclosing document.
    /// </summary>
    private static ITextPattern? FindTextPattern(AutomationElement element)
    {
        var current = element;
        for (var depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
        {
            var candidate = current;
            var pattern = Try(() => candidate.Patterns.Text.TryGetPattern(out var text) ? text : null);
            if (pattern is not null)
            {
                return pattern;
            }
            current = Try(() => candidate.Parent);
        }
        return null;
    }

    private static string? JoinSelection(ITextRange[]? ranges)
    {
        if (ranges is null || ranges.Length == 0)
        {
            return null;
        }
        var parts = new List<string>();
        foreach (var range in ranges)
        {
            var text = Try(() => range.GetText(TextNormalization.MaximumQueryLength + 1));
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }
        var joined = string.Join("\n", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? WordUnderPointer(UIA3Automation automation, Point point, int attempt, out bool textFound)
    {
        textFound = false;
        var element = Try(() => automation.FromPoint(point));
        if (element is null)
        {
            return null;
        }
        var text = FindTextPattern(element);
        if (text is null)
        {
            return null;
        }
        textFound = true;

        var range = Try(() => text.RangeFromPoint(point));
        if (range is null || !TryRun(() => range.ExpandToEnclosingUnit(TextUnit.Word)))
        {
            return null;
        }
        var word = WordFromRange(range);
        if (word is null)
        {
            return null;
        }

        // RangeFromPoint returns the text nearest to the pointer even when the pointer rests on empty space,
        // so only accept a word whose bounding box contains the pointer. A provider that reports no boxes is
        // trusted on a repeated attempt only, by which time its text positions should be final.
        var boxes = Try(() => range.GetBoundingRectangles());
        if (boxes is { Length: > 0 })
        {
            return boxes.Any(box => Rectangle.Inflate(box, PointerToleranceInPixels, PointerToleranceInPixels).Contains(point))
                ? word
                : null;
        }
        return attempt > 1 ? word : null;
    }

    private static string? WordAtCaret(AutomationElement focused, ITextRange[]? selection)
    {
        ITextRange? caret = null;
        var text2 = Try(() => focused.Patterns.Text2.TryGetPattern(out var pattern) ? pattern : null);
        if (text2 is not null)
        {
            caret = Try(() =>
            {
                var range = text2.GetCaretRange(out var isActive);
                return isActive ? range : null;
            });
        }
        if (caret is null && selection is { Length: 1 })
        {
            // Controls without TextPattern2 report the caret as a degenerate (empty) selection range.
            caret = selection[0];
        }
        if (caret is null)
        {
            return null;
        }

        // A caret often sits just after the last letter of a word, where the enclosing word unit may be the
        // following whitespace or line break. Probe the caret position first, then one character back.
        foreach (var offset in new[] { 0, -1 })
        {
            var probe = Try(() => caret.Clone());
            if (probe is null)
            {
                return null;
            }
            if (offset != 0 && TryMove(probe, TextUnit.Character, offset) == 0)
            {
                return null;
            }
            if (!TryRun(() => probe.ExpandToEnclosingUnit(TextUnit.Word)))
            {
                return null;
            }
            var word = WordFromRange(probe);
            if (word is not null)
            {
                return word;
            }
        }
        return null;
    }

    /// <summary>
    /// The dictionary word represented by a word-unit text range, or null when the range holds no word: empty
    /// space, punctuation only, several lines, or an over-long token such as a URL. Surrounding punctuation is
    /// dropped because the user did not choose it.
    /// </summary>
    private static string? WordFromRange(ITextRange range)
    {
        var text = Try(() => range.GetText(TextNormalization.MaximumQueryLength + 1));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var trimmed = text.Trim();
        if (trimmed.Length > TextNormalization.MaximumQueryLength ||
            trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
            !trimmed.Any(char.IsLetterOrDigit))
        {
            return null;
        }
        var conservative = TextNormalization.Prepare(trimmed).ConservativeText;
        return conservative.Length > 0 ? conservative : trimmed;
    }

    private static T? Try<T>(Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception)
        {
            // UI Automation providers fail in many ways (COM errors, elements that vanish, unsupported
            // operations). Every step is optional, so a failure simply means "nothing found here".
            return default;
        }
    }

    private static bool TryRun(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The number of units the range moved (negative for backward), or 0 when it could not move.</summary>
    private static int TryMove(ITextRange range, TextUnit unit, int count)
    {
        try
        {
            return range.Move(unit, count);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static CaptureResult CaptureWithCopy(IntPtr foregroundWindow, bool allowKeyboardCopy)
    {
        if (NativeMethods.GetForegroundWindow() != foregroundWindow)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: "The fallback copy command was cancelled because focus changed.");
        }

        var snapshot = ClipboardSnapshot.Take();
        var sequenceBefore = NativeMethods.GetClipboardSequenceNumber();
        uint capturedSequence = 0;

        try
        {
            // Standard edit and rich-edit controls honor WM_COPY directly. This does not
            // depend on Ctrl, Alt, or the D key having been released after the hotkey.
            if (SendNativeCopyCommand(foregroundWindow))
            {
                capturedSequence = WaitForClipboardChange(sequenceBefore, 350);
            }

            if (capturedSequence == 0)
            {
                if (!allowKeyboardCopy)
                {
                    return new CaptureResult(CaptureStatus.NoSelection, Detail: "The focused control exposed its text without a selection; the keyboard copy fallback was skipped.");
                }
                if (!WaitForShortcutModifiers())
                {
                    return new CaptureResult(CaptureStatus.NoSelection, Detail: "The keyboard copy fallback was skipped while shortcut modifiers remained pressed.");
                }
                if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return new CaptureResult(CaptureStatus.Unavailable, Detail: "The keyboard copy fallback was cancelled because focus changed.");
                }

                // Do not mistake a clipboard change while waiting for our own Copy result.
                sequenceBefore = NativeMethods.GetClipboardSequenceNumber();
                if (!SendCopyCommand())
                {
                    return new CaptureResult(CaptureStatus.Unavailable, Detail: "Windows did not accept the fallback copy command.");
                }
                capturedSequence = WaitForClipboardChange(sequenceBefore, 350);
            }

            if (capturedSequence == 0)
            {
                return new CaptureResult(CaptureStatus.NoSelection);
            }

            string? text = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        text = Clipboard.GetText();
                    }
                    break;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(20);
                }
            }

            if (NativeMethods.GetClipboardSequenceNumber() != capturedSequence)
            {
                return new CaptureResult(CaptureStatus.Unavailable, Detail: "A newer clipboard change was not treated as the captured selection.");
            }
            return ClassifyCapturedText(text);
        }
        finally
        {
            snapshot.RestoreIfUnchanged(capturedSequence);
        }
    }

    private static uint WaitForClipboardChange(uint sequenceBefore, int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            var sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence != sequenceBefore)
            {
                return sequence;
            }
            Thread.Sleep(15);
        }
        return 0;
    }

    private static CaptureResult ClassifyCapturedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CaptureResult(CaptureStatus.NoSelection);
        }
        if (text.Contains('\r') || text.Contains('\n'))
        {
            return new CaptureResult(CaptureStatus.NoSelection, Detail: "Multiline text is not a dictionary entry.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length > TextNormalization.MaximumQueryLength)
        {
            return new CaptureResult(CaptureStatus.TooLong, Detail: $"The selection exceeds {TextNormalization.MaximumQueryLength} characters.");
        }
        return trimmed.Length == 0
            ? new CaptureResult(CaptureStatus.NoSelection)
            : new CaptureResult(CaptureStatus.Success, trimmed);
    }

    private static bool WaitForShortcutModifiers()
    {
        var deadline = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < deadline)
        {
            if (!IsDown(0x11) && !IsDown(0x10) && !IsDown(0x12) && !IsDown(0x5B) && !IsDown(0x5C))
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

    private static async Task<Attempt<T>> RunSta<T>(Func<T> action, int timeoutMilliseconds)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
        return finished == completion.Task
            ? new Attempt<T>(false, await completion.Task.ConfigureAwait(false))
            : new Attempt<T>(true, default);
    }

    private sealed record Attempt<T>(bool TimedOut, T? Value);

    /// <summary>
    /// A by-value copy of the common clipboard formats, taken before the Copy fallback runs. The object that
    /// Clipboard.GetDataObject() returns only wraps the live clipboard, so once the Copy command has replaced
    /// the clipboard it describes the captured selection rather than the content that is to be restored.
    /// </summary>
    private sealed class ClipboardSnapshot
    {
        private static readonly string[] Formats =
        {
            DataFormats.UnicodeText, DataFormats.Rtf, DataFormats.Html, DataFormats.FileDrop
        };

        private readonly List<KeyValuePair<string, object>> _items = new();

        public static ClipboardSnapshot Take()
        {
            var snapshot = new ClipboardSnapshot();
            try
            {
                var data = Clipboard.GetDataObject();
                if (data is not null)
                {
                    foreach (var format in Formats)
                    {
                        try
                        {
                            if (data.GetDataPresent(format, false) && data.GetData(format, false) is { } value)
                            {
                                snapshot._items.Add(new KeyValuePair<string, object>(format, value));
                            }
                        }
                        catch (Exception) { /* A format that cannot be read is not restored. */ }
                    }
                }
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                {
                    snapshot._items.Add(new KeyValuePair<string, object>(DataFormats.Bitmap, image));
                }
            }
            catch (Exception) { /* Snapshotting is best-effort only. */ }
            return snapshot;
        }

        public void RestoreIfUnchanged(uint capturedSequence)
        {
            if (_items.Count == 0 || capturedSequence == 0 ||
                NativeMethods.GetClipboardSequenceNumber() != capturedSequence)
            {
                return;
            }
            try
            {
                var data = new DataObject();
                foreach (var item in _items)
                {
                    if (item.Value is Image image)
                    {
                        data.SetImage(image);
                    }
                    else
                    {
                        data.SetData(item.Key, true, item.Value);
                    }
                }
                Clipboard.SetDataObject(data, true);
            }
            catch (Exception) { /* Restoration is best-effort only. */ }
        }
    }

    private static class NativeMethods
    {
        internal const uint GaRoot = 2;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();

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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicalCursorPos(out POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPhysicalPoint(POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

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
