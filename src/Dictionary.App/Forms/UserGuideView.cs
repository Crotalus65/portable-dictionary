using PortableDictionary.Services;

namespace PortableDictionary.Forms;

public sealed class NoticeEventArgs(string title, string filename) : EventArgs
{
    public string Title { get; } = title;
    public string Filename { get; } = filename;
}

public sealed class UserGuideView : UserControl
{
    private readonly TableLayoutPanel _layout = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        Padding = new Padding(14)
    };
    private readonly List<(Label Label, GuidePartKind Kind)> _labels = new();
    private Font _bodyFont = null!;
    private Font _titleFont = null!;
    private Font _headingFont = null!;

    public UserGuideView()
    {
        AutoScaleMode = AutoScaleMode.Inherit;
        AutoScroll = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_layout);
        _layout.Layout += (_, _) => UpdateWidths();
        SetTextSize(10f);
    }

    public event EventHandler<NoticeEventArgs>? NoticeRequested;

    public void SetDocument(IReadOnlyList<GuidePart> parts)
    {
        AutoScrollPosition = Point.Empty;
        _layout.SuspendLayout();
        foreach (var (label, _) in _labels) label.Dispose();
        _labels.Clear();
        _layout.Controls.Clear();
        _layout.RowStyles.Clear();
        _layout.RowCount = parts.Count;
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            Label label;
            if (part.Kind == GuidePartKind.NoticeLink)
            {
                var link = new LinkLabel { TabStop = true, LinkBehavior = LinkBehavior.AlwaysUnderline };
                link.LinkClicked += (_, _) => NoticeRequested?.Invoke(this, new(part.Text, part.NoticeFile!));
                label = link;
            }
            else label = new Label();
            label.Text = part.Text;
            label.UseMnemonic = false;
            label.AutoSize = true;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.TopLeft;
            label.Margin = new Padding(0, part.Kind == GuidePartKind.Title ? 0 : 8, 0, 8);
            label.MaximumSize = new Size(TextWidth(), 0);
            label.Font = FontFor(part.Kind);
            label.TabIndex = index;
            _labels.Add((label, part.Kind));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.Controls.Add(label, 0, index);
        }
        _layout.ResumeLayout(true);
        UpdateWidths();
        ScrollToTop();
    }

    public void ScrollToTop()
    {
        // Clear a focused notice link before resetting the viewport.
        ActiveControl = null;
        AutoScrollPosition = Point.Empty;
    }

    public void SetTextSize(float size)
    {
        size = Math.Clamp(size, 9f, 20f);
        var previous = new[] { _bodyFont, _titleFont, _headingFont };
        var family = SystemFonts.MessageBoxFont!.FontFamily;
        _bodyFont = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
        _titleFont = new Font(family, size + 5f, FontStyle.Bold, GraphicsUnit.Point);
        _headingFont = new Font(family, size + 1.5f, FontStyle.Bold, GraphicsUnit.Point);
        _layout.SuspendLayout();
        foreach (var (label, kind) in _labels) label.Font = FontFor(kind);
        _layout.ResumeLayout(true);
        foreach (var font in previous) font?.Dispose();
    }

    private Font FontFor(GuidePartKind kind) => kind switch
    {
        GuidePartKind.Title => _titleFont,
        GuidePartKind.Heading => _headingFont,
        _ => _bodyFont
    };

    private void UpdateWidths()
    {
        var width = TextWidth();
        foreach (var (label, _) in _labels)
        {
            var available = Math.Max(1, width - label.Margin.Horizontal);
            if (label.MaximumSize.Width != available) label.MaximumSize = new Size(available, 0);
        }
    }

    // Bound text to the viewport, reserving a scrollbar gutter so AutoSize
    // cannot make the table wider in response to a long paragraph or link.
    private int TextWidth() => Math.Max(1,
        ClientSize.Width - _layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated) UpdateWidths();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _bodyFont?.Dispose(); _titleFont?.Dispose(); _headingFont?.Dispose();
        }
    }
}
