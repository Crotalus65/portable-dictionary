using System.Runtime.InteropServices;
using PortableDictionary.Services;

namespace PortableDictionary.Forms;

public sealed class ShortcutDialog : Form
{
    private readonly TextBox _shortcutBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        Text = "Press at least one modifier (Ctrl, Alt, Shift, or Win) and another key.",
        AutoEllipsis = true
    };
    private HotkeyGesture? _selected;

    public ShortcutDialog(HotkeyGesture current, string? registrationWarning = null)
    {
        _selected = current;
        Text = "Lookup shortcut";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(500, 240);
        KeyPreview = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Global selected-text lookup shortcut:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(_shortcutBox, 0, 1);
        layout.Controls.Add(_hint, 0, 2);
        if (registrationWarning is not null)
        {
            _hint.Text = registrationWarning + "\n" + _hint.Text;
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(16, 4, 16, 12)
        };
        var ok = new Button
        {
            Text = "OK",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.None
        };
        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.Cancel
        };
        ok.Click += (_, _) => AcceptGesture();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        _shortcutBox.Text = current.ToString();
        KeyDown += OnShortcutKeyDown;
        _shortcutBox.KeyDown += OnShortcutKeyDown;
        Shown += (_, _) => _shortcutBox.Focus();
    }

    public HotkeyGesture SelectedGesture => _selected ?? HotkeyGesture.Default;

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        var code = e.KeyCode & Keys.KeyCode;
        if (code == Keys.Escape && e.Modifiers == Keys.None)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }
        if (code == Keys.Enter && e.Modifiers == Keys.None && _selected is not null)
        {
            DialogResult = DialogResult.OK;
            return;
        }
        if (code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (e.Control) modifiers |= HotkeyModifiers.Control;
        if (e.Alt) modifiers |= HotkeyModifiers.Alt;
        if (e.Shift) modifiers |= HotkeyModifiers.Shift;
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)
        {
            modifiers |= HotkeyModifiers.Windows;
        }
        var candidate = new HotkeyGesture(modifiers, code);
        if (!candidate.IsValid)
        {
            _hint.Text = "Add at least one modifier, such as Ctrl or Alt.";
            return;
        }
        _selected = candidate;
        _shortcutBox.Text = candidate.ToString();
        _hint.Text = "Select OK to register this combination. If Windows reports a conflict, choose another.";
    }

    private void AcceptGesture()
    {
        if (_selected is null || !_selected.IsValid)
        {
            _hint.Text = "Press a valid key combination before selecting OK.";
            return;
        }
        DialogResult = DialogResult.OK;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
