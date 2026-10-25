using System.Runtime.InteropServices;
using PortableDictionary.Core;
using PortableDictionary.Services;

namespace PortableDictionary.Forms;

public sealed class MainForm : Form
{
    private readonly LookupService _lookup;
    private readonly DatasetInfo _datasetInfo;
    private readonly PortableSettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly string? _startupWarning;
    private readonly SelectedTextCapture _capture = new();
    private readonly NavigationHistory<LookupResult> _history = new();
    private readonly TextBox _wordBox = new();
    private readonly Button _backButton = new();
    private readonly Button _menuButton = new();
    private readonly ResultsView _results = new();
    private readonly TableLayoutPanel _topPanel = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly System.Windows.Forms.Timer _warningTimer = new() { Interval = 9000 };
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly Icon _appIcon;
    private GlobalHotkey? _hotkey;
    private string? _hotkeyError;
    private Font? _entryFont;
    private bool _exiting;
    private bool _captureInProgress;

    public MainForm(LookupService lookup, DatasetInfo datasetInfo, PortableSettingsStore settingsStore,
        SettingsLoadResult settingsLoad)
    {
        _lookup = lookup;
        _datasetInfo = datasetInfo;
        _settingsStore = settingsStore;
        _settings = settingsLoad.Settings;
        _startupWarning = settingsLoad.Warning;

        Text = $"Dictionary {Application.ProductVersion}";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        MinimumSize = new Size(440, 340);
        StartPosition = FormStartPosition.Manual;
        Bounds = WindowPlacement.Restore(_settings);
        KeyPreview = true;
        _appIcon = LoadIcon();
        Icon = _appIcon;

        BuildInterface();
        BuildMenusAndTray();
        ApplyTextSize(_settings.TextSize);
        _results.SetWelcome(_settings.Shortcut);
        _warningTimer.Tick += (_, _) => _statusStrip.Visible = false;
        Shown += OnInitialShown;
        ResizeEnd += (_, _) => SaveWindowPlacement();
    }

    private void BuildInterface()
    {
        _wordBox.Dock = DockStyle.Fill;
        _wordBox.PlaceholderText = "Word or short dictionary entry";
        _wordBox.Margin = new Padding(5, 3, 5, 3);
        _wordBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                PerformLookup(_wordBox.Text);
            }
        };

        _backButton.Text = "Back";
        _backButton.Dock = DockStyle.Fill;
        _backButton.AutoSize = true;
        _backButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _backButton.MinimumSize = new Size(62, 0);
        _backButton.Padding = new Padding(6, 0, 6, 0);
        _backButton.Enabled = false;
        _backButton.Margin = new Padding(0, 3, 5, 3);
        _backButton.Click += (_, _) => GoBack();

        _menuButton.Text = "Menu";
        _menuButton.Dock = DockStyle.Fill;
        _menuButton.AutoSize = true;
        _menuButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _menuButton.MinimumSize = new Size(70, 0);
        _menuButton.Padding = new Padding(8, 0, 8, 0);
        _menuButton.Margin = new Padding(5, 3, 0, 3);
        _menuButton.Click += (_, _) => _menu.Show(_menuButton, new Point(0, _menuButton.Height));

        _topPanel.Dock = DockStyle.Top;
        _topPanel.ColumnCount = 3;
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.Padding = new Padding(8, 7, 8, 5);
        _topPanel.Height = 48;
        _topPanel.Controls.Add(_backButton, 0, 0);
        _topPanel.Controls.Add(_wordBox, 1, 0);
        _topPanel.Controls.Add(_menuButton, 2, 0);

        _results.Dock = DockStyle.Fill;
        _results.SynonymClicked += (_, e) => PerformLookup(e.Synonym);
        _statusStrip.Dock = DockStyle.Bottom;
        _statusStrip.SizingGrip = false;
        _statusStrip.Visible = false;
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_results);
        Controls.Add(_topPanel);
        Controls.Add(_statusStrip);
    }

    private void BuildMenusAndTray()
    {
        PopulateMenu(_menu, false);
        PopulateMenu(_trayMenu, true);
        _menuButton.ContextMenuStrip = _menu;
        _tray.Icon = _appIcon;
        _tray.Text = $"Dictionary {Application.ProductVersion} — offline lookup";
        _tray.ContextMenuStrip = _trayMenu;
        _tray.Visible = true;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Summon(null);
            }
        };
    }

    private void PopulateMenu(ContextMenuStrip menu, bool tray)
    {
        if (tray)
        {
            menu.Items.Add("Open Dictionary", null, (_, _) => Summon(null));
            menu.Items.Add(new ToolStripSeparator());
        }
        var textSize = new ToolStripMenuItem("Text size");
        textSize.DropDownItems.Add("Smaller", null, (_, _) => ApplyTextSize(_settings.TextSize - 1f, true));
        textSize.DropDownItems.Add("Larger", null, (_, _) => ApplyTextSize(_settings.TextSize + 1f, true));
        textSize.DropDownItems.Add("Reset", null, (_, _) => ApplyTextSize(10f, true));
        menu.Items.Add(textSize);
        menu.Items.Add("Lookup shortcut…", null, (_, _) => ChangeShortcut());
        menu.Items.Add("About", null, (_, _) => ShowAbout());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
    }

    private void OnInitialShown(object? sender, EventArgs e)
    {
        _hotkey = new GlobalHotkey(Handle);
        if (!HotkeyGesture.TryParse(_settings.Shortcut, out var gesture))
        {
            gesture = HotkeyGesture.Default;
            _settings.Shortcut = gesture.ToString();
        }
        if (!_hotkey.Register(gesture, out _hotkeyError))
        {
            ShowWarning(_hotkeyError + " Manual entry and the tray icon still work; use Lookup shortcut… to choose another.");
        }
        if (_startupWarning is not null)
        {
            ShowWarning(_startupWarning);
        }
        FocusWord();
    }

    private void PerformLookup(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            FocusWord();
            return;
        }
        var result = _lookup.Lookup(query);
        _history.Navigate(result);
        ShowResult(result);
        if (!result.IsSuccess)
        {
            FocusWord();
        }
    }

    private void ShowResult(LookupResult result)
    {
        _wordBox.Text = result.Query;
        _results.SetResult(result);
        _backButton.Enabled = _history.CanGoBack;
    }

    private void GoBack()
    {
        if (_history.GoBack() is { } previous)
        {
            ShowResult(previous);
        }
    }

    private void ApplyTextSize(float size, bool save = false)
    {
        _settings.TextSize = Math.Clamp(size, 9f, 20f);
        var newFont = new Font(SystemFonts.MessageBoxFont!.FontFamily, _settings.TextSize, GraphicsUnit.Point);
        _wordBox.Font = newFont;
        _entryFont?.Dispose();
        _entryFont = newFont;
        _results.SetTextSize(_settings.TextSize);
        _topPanel.Height = Math.Max(44, _entryFont.Height + 22);
        if (save)
        {
            SaveSettings();
        }
    }

    private void ChangeShortcut()
    {
        if (_hotkey is null)
        {
            return;
        }
        if (!HotkeyGesture.TryParse(_settings.Shortcut, out var current))
        {
            current = HotkeyGesture.Default;
        }
        using var dialog = new ShortcutDialog(_hotkey.Current ?? current, _hotkeyError);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var previous = _settings.Shortcut;
        var selected = dialog.SelectedGesture;
        if (_hotkey.Register(selected, out var error))
        {
            _settings.Shortcut = selected.ToString();
            _hotkeyError = null;
            SaveSettings();
            ShowWarning("Lookup shortcut registered: " + _settings.Shortcut);
            return;
        }

        var restored = HotkeyGesture.TryParse(previous, out var previousGesture) &&
                       _hotkey.Register(previousGesture, out _);
        _hotkeyError = error;
        ShowWarning(error + (restored ? " The previous shortcut was restored." : " Use Lookup shortcut… to try another combination."));
    }

    private void ShowAbout()
    {
        using var dialog = new AboutDialog(_datasetInfo);
        dialog.ShowDialog(this);
    }

    private void FocusWord()
    {
        _wordBox.Focus();
        _wordBox.SelectAll();
    }

    public void Summon(string? capturedQuery)
    {
        if (!Visible)
        {
            Show();
        }
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        BringToFront();
        Activate();
        NativeMethods.SetForegroundWindow(Handle);

        if (!string.IsNullOrWhiteSpace(capturedQuery))
        {
            PerformLookup(capturedQuery);
        }
        FocusWord();
    }

    public void RequestRestore()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Summon(null)));
            }
            else
            {
                Summon(null);
            }
        }
        catch (InvalidOperationException) { /* The window is shutting down. */ }
    }

    private async Task BeginHotkeySummonAsync()
    {
        if (_captureInProgress)
        {
            return;
        }
        _captureInProgress = true;
        try
        {
            var source = NativeMethods.GetForegroundWindow();
            if (SelectedTextCapture.BelongsToCurrentProcess(source))
            {
                // The shortcut was pressed while Dictionary itself was active. There is nothing to capture
                // from our own word field, so just bring the window forward ready for typing.
                Summon(null);
                return;
            }

            var result = await _capture.CaptureAsync(source);
            if (result.Status == CaptureStatus.Success)
            {
                Summon(result.Text);
            }
            else
            {
                Summon(null);
                if (result.Status == CaptureStatus.TooLong)
                {
                    ShowWarning(result.Detail ?? "The selection is too long to look up.");
                }
                // No selection, no word under the pointer or at the text cursor, and unreadable text are normal
                // hotkey cases. The window still opens with the existing query selected for manual replacement.
            }
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkey.MessageId && m.WParam.ToInt32() == GlobalHotkey.HotkeyId)
        {
            _ = BeginHotkeySummonAsync();
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HideToTray();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Left))
        {
            GoBack();
            return true;
        }
        if (keyData == (Keys.Control | Keys.L))
        {
            FocusWord();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void HideToTray()
    {
        SaveSettings();
        Hide();
    }

    private void ExitApplication()
    {
        _exiting = true;
        SaveSettings();
        _tray.Visible = false;
        _hotkey?.Unregister();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        SaveWindowPlacement();
        _settingsStore.Save(_settings, out _);
        _tray.Visible = false;
        _hotkey?.Unregister();
        base.OnFormClosing(e);
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width < 100 || bounds.Height < 100)
        {
            return;
        }
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
    }

    private void SaveSettings()
    {
        SaveWindowPlacement();
        if (!_settingsStore.Save(_settings, out var warning) && warning is not null)
        {
            ShowWarning(warning);
        }
    }

    private void ShowWarning(string warning)
    {
        _statusLabel.Text = warning;
        _statusStrip.Visible = true;
        _warningTimer.Stop();
        _warningTimer.Start();
        try
        {
            _tray.BalloonTipTitle = "Dictionary";
            _tray.BalloonTipText = warning.Length > 240 ? warning[..240] + "…" : warning;
            _tray.BalloonTipIcon = ToolTipIcon.Warning;
            _tray.ShowBalloonTip(4000);
        }
        catch (InvalidOperationException) { }
    }

    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Dictionary.ico");
        return File.Exists(path) ? new Icon(path) : (Icon)SystemIcons.Application.Clone();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkey?.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _trayMenu.Dispose();
            _warningTimer.Dispose();
            _entryFont?.Dispose();
            _appIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
