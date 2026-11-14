using PortableDictionary.Services;

namespace PortableDictionary.Forms;

public sealed class LicenseDialog : Form
{
    private readonly Font _noticeFont = new(SystemFonts.MessageBoxFont!.FontFamily, 10f);

    public LicenseDialog(string title, string path)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 600);
        MinimumSize = new Size(380, 300);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        var content = File.Exists(path)
            ? LicenseText.ForDisplay(Path.GetFileName(path), File.ReadAllText(path))
            : "The license file could not be found. Restore the licenses folder from the portable distribution.";
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            TabStop = false, HideSelection = true,
            ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = _noticeFont,
            Text = content.Replace("\r\n", "\n").Replace("\n", "\r\n")
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(8)
        };
        var close = new Button
        {
            Text = "Close", AutoSize = true, MinimumSize = new Size(90, 32), DialogResult = DialogResult.OK
        };
        footer.Controls.Add(close);
        Controls.Add(box);
        Controls.Add(footer);
        AcceptButton = close;
        CancelButton = close;
        ActiveControl = close;
        Shown += (_, _) =>
        {
            box.Select(0, 0);
            box.ScrollToCaret();
            ActiveControl = close;
            close.Focus();
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _noticeFont.Dispose();
    }
}
