using System.Runtime.InteropServices;
using System.Text;
using Accessibility;
using PortableDictionary.Core;

namespace PortableDictionary.Services;

// Called on the clipboard STA worker. These APIs read document text without
// selecting it, sending navigation keys, creating an Office instance, or editing.
internal static class NativeTextCapture
{
    internal static CaptureResult Capture(IntPtr source)
    {
        var focused = FocusedWindow(source);
        if (focused == IntPtr.Zero) return new(CaptureStatus.NoSelection);
        var word = ReadWord(source, focused);
        if (word.Status is CaptureStatus.Success or CaptureStatus.TooLong) return word;

        foreach (var window in new[] { focused, source }.Distinct())
        {
            var result = ReadAccessibleText(source, window);
            if (result.Status is CaptureStatus.Success or CaptureStatus.TooLong) return result;
        }
        return new(CaptureStatus.NoSelection);
    }

    private static CaptureResult ReadWord(IntPtr source, IntPtr focused)
    {
        var owned = new List<object>();
        try
        {
            for (var window = focused; window != IntPtr.Zero; window = GetParent(window))
            {
                if (GetForegroundWindow() != source) break;
                var name = new StringBuilder(128);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() == "_WwG")
                {
                    var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                    if (AccessibleObjectFromWindow(window, 0xFFFFFFF0, ref dispatch, out var native) < 0) break;
                    owned.Add(native);
                    dynamic wordWindow = native;
                    object selection = wordWindow.Selection;
                    owned.Add(selection);
                    object original = ((dynamic)selection).Range;
                    owned.Add(original);
                    object duplicate = ((dynamic)original).Duplicate;
                    owned.Add(duplicate);
                    dynamic range = duplicate;
                    var caret = (int)range.Start == (int)range.End;
                    if (caret) range.Expand(2); // wdWord; Duplicate never moves Selection.
                    if ((int)range.End - (int)range.Start > TextNormalization.MaximumQueryLength)
                        return new(CaptureStatus.TooLong);
                    string text = range.Text;
                    // Word includes paragraph/table terminators at a range end.
                    text = text.TrimEnd('\r', '\a');
                    var result = TextCaptureReader.Classify(caret ? text.Trim() : text);
                    return GetForegroundWindow() == source ? result : new(CaptureStatus.Unavailable);
                }
                if (window == source) break;
            }
        }
        catch (Exception) { /* Protected views, unavailable automation, or a closing document. */ }
        finally { foreach (var item in owned.AsEnumerable().Reverse()) Release(item); }
        return new(CaptureStatus.NoSelection);
    }

    private static CaptureResult ReadAccessibleText(IntPtr source, IntPtr window)
    {
        var owned = new List<object>();
        try
        {
            var accessibleId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            if (AccessibleObjectFromWindow(window, 0xFFFFFFFC, ref accessibleId, out var root) < 0)
                return new(CaptureStatus.NoSelection);
            owned.Add(root);
            var chain = new List<IAccessible>();
            var current = root as IAccessible;
            while (current is not null && chain.Count < 24 && GetForegroundWindow() == source)
            {
                chain.Add(current);
                object? next;
                try
                {
                    next = current.accFocus;
                    if (next is int child && child > 0) next = current.get_accChild(child);
                }
                catch (Exception) { break; } // Leaf objects need not implement accFocus.
                if (next is not IAccessible accessible) break;
                owned.Add(accessible);
                if (ReferenceEquals(current, accessible)) break;
                current = accessible;
            }

            foreach (var accessible in chain.AsEnumerable().Reverse())
            {
                if (GetForegroundWindow() != source) return new(CaptureStatus.Unavailable);
                try
                {
                    if (accessible is not IAccessibleServiceProvider services) continue;
                    var service = accessibleId;
                    var ia2Id = new Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
                    if (services.QueryService(ref service, ref ia2Id, out var pointer) < 0 || pointer == IntPtr.Zero) continue;
                    object native;
                    try { native = Marshal.GetObjectForIUnknown(pointer); }
                    finally { Marshal.Release(pointer); }
                    owned.Add(native);
                    if (native is not IAccessibleText text) continue;
                    if (text.GetSelectionCount(out var count) < 0) continue;
                    CaptureResult result;
                    if (count == 1)
                    {
                        if (text.GetSelection(0, out var start, out var end) < 0) continue;
                        (start, end) = (Math.Min(start, end), Math.Max(start, end));
                        if (end - start > TextNormalization.MaximumQueryLength) return new(CaptureStatus.TooLong);
                        if (end > start)
                        {
                            if (text.GetText(start, end, out var selected) < 0) continue;
                            result = TextCaptureReader.Classify(selected);
                        }
                        else result = ReadCaret(text);
                    }
                    else if (count == 0) result = ReadCaret(text);
                    else continue;
                    if (result.Status is CaptureStatus.Success or CaptureStatus.TooLong)
                        return GetForegroundWindow() == source ? result : new(CaptureStatus.Unavailable);
                }
                catch (Exception) { /* Try the next containing accessible text object. */ }
            }
        }
        catch (Exception) { /* Not every MSAA object implements IAccessible2. */ }
        finally { foreach (var item in owned.AsEnumerable().Reverse()) Release(item); }
        return new(CaptureStatus.NoSelection);
    }

    private static CaptureResult ReadCaret(IAccessibleText text)
    {
        if (text.GetCaretOffset(out var caret) < 0 || caret < 0) return new(CaptureStatus.NoSelection);
        var status = text.GetTextAtOffset(caret, 1, out _, out _, out var word);
        if ((status < 0 || string.IsNullOrWhiteSpace(word)) && caret > 0)
            status = text.GetTextBeforeOffset(caret, 1, out _, out _, out word);
        if (status < 0) return new(CaptureStatus.NoSelection);
        return TextCaptureReader.ReadRanges(new[] { "" }, range => range, _ => word ?? "");
    }

    private static void Release(object value)
    {
        try { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); } catch (Exception) { }
    }

    private static IntPtr FocusedWindow(IntPtr source)
    {
        var thread = GetWindowThreadProcessId(source, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) &&
               (info.Focus == source || IsChild(source, info.Focus)) ? info.Focus : IntPtr.Zero;
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
    }

    // IAccessibleText vtable prefix through textAtOffset, from the IAccessible2
    // IDL (BSD; see licenses/IAccessible2-LICENSE.txt). Unused slots preserve ABI.
    [ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleText
    {
        [PreserveSig] int AddSelection(int start, int end);
        [PreserveSig] int GetAttributes(int offset, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
        [PreserveSig] int GetCaretOffset(out int offset);
        [PreserveSig] int GetCharacterExtents(int offset, int coordinateType, out int x, out int y, out int width, out int height);
        [PreserveSig] int GetSelectionCount(out int count);
        [PreserveSig] int GetOffsetAtPoint(int x, int y, int coordinateType, out int offset);
        [PreserveSig] int GetSelection(int index, out int start, out int end);
        [PreserveSig] int GetText(int start, int end, [MarshalAs(UnmanagedType.BStr)] out string text);
        [PreserveSig] int GetTextBeforeOffset(int offset, int boundary, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
        [PreserveSig] int GetTextAfterOffset(int offset, int boundary, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
        [PreserveSig] int GetTextAtOffset(int offset, int boundary, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
}
