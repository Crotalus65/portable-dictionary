using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PortableDictionary.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, Keys Key)
{
    public static HotkeyGesture Default => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.D);

    public bool IsValid
    {
        get
        {
            var code = Key & Keys.KeyCode;
            var isModifier = code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
                or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey;
            return Modifiers != HotkeyModifiers.None && !isModifier && (int)code is > 0 and <= 255;
        }
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        Keys key = Keys.None;
        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= HotkeyModifiers.Control; continue;
                case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                case "win": case "windows": modifiers |= HotkeyModifiers.Windows; continue;
            }

            if (key != Keys.None || !TryParseKey(rawPart, out key))
            {
                return false;
            }
        }

        gesture = new HotkeyGesture(modifiers, key & Keys.KeyCode);
        return gesture.IsValid;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        var key = Key & Keys.KeyCode;
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key.ToString());
        return string.Join('+', parts);
    }

    private static bool TryParseKey(string value, out Keys key)
    {
        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
        {
            key = char.IsLetter(value[0])
                ? (Keys)char.ToUpperInvariant(value[0])
                : (Keys)((int)Keys.D0 + value[0] - '0');
            return true;
        }
        return Enum.TryParse(value, true, out key);
    }
}

public sealed class GlobalHotkey : IDisposable
{
    public const int MessageId = 0x0312;
    public const int HotkeyId = 0x5344;
    private const uint ModNoRepeat = 0x4000;
    private readonly IntPtr _windowHandle;

    public GlobalHotkey(IntPtr windowHandle) => _windowHandle = windowHandle;

    public HotkeyGesture? Current { get; private set; }
    public bool IsRegistered => Current is not null;

    public bool Register(HotkeyGesture gesture, out string? error)
    {
        Unregister();
        if (!gesture.IsValid)
        {
            error = "Choose at least one modifier and one non-modifier key.";
            return false;
        }

        if (NativeMethods.RegisterHotKey(_windowHandle, HotkeyId,
                (uint)gesture.Modifiers | ModNoRepeat, (uint)(gesture.Key & Keys.KeyCode)))
        {
            Current = gesture;
            error = null;
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code == 1409
            ? "That shortcut is already registered by another application. Choose another combination."
            : $"Windows could not register that shortcut (error {code}). Choose another combination.";
        return false;
    }

    public void Unregister()
    {
        if (Current is not null)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            Current = null;
        }
    }

    public void Dispose() => Unregister();

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
