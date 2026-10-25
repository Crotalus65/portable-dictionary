using PortableDictionary.Core;

namespace PortableDictionary.Forms;

public sealed class SynonymEventArgs(string synonym) : EventArgs
{
    public string Synonym { get; } = synonym;
}

public sealed class ResultsView : Panel
{
    private readonly ResultCanvas _canvas;
    private readonly List<Block> _blocks = new();
    private Font _titleFont = null!;
    private Font _headwordFont = null!;
    private Font _posFont = null!;
    private Font _bodyFont = null!;
    private Font _exampleFont = null!;
    private Font _linkFont = null!;
    private float _textSize;
    private int _contentHeight;
    private LookupResult? _currentResult;

    public ResultsView()
    {
        AutoScroll = true;
        BackColor = SystemColors.Window;
        _canvas = new ResultCanvas(this) { BackColor = SystemColors.Window };
        Controls.Add(_canvas);
        SetTextSize(10f);
    }

    public event EventHandler<SynonymEventArgs>? SynonymClicked;

    public void SetTextSize(float points)
    {
        _textSize = Math.Clamp(points, 9f, 20f);
        Font?[] previousFonts =
        {
            _titleFont, _headwordFont, _posFont, _bodyFont, _exampleFont, _linkFont
        };
        var family = SystemFonts.MessageBoxFont!.FontFamily;
        _titleFont = new Font(family, _textSize + 5f, FontStyle.Bold, GraphicsUnit.Point);
        _headwordFont = new Font(family, _textSize + 2f, FontStyle.Bold, GraphicsUnit.Point);
        _posFont = new Font(family, _textSize + 0.5f, FontStyle.Bold, GraphicsUnit.Point);
        _bodyFont = new Font(family, _textSize, FontStyle.Regular, GraphicsUnit.Point);
        _exampleFont = new Font(family, _textSize, FontStyle.Italic, GraphicsUnit.Point);
        _linkFont = new Font(family, _textSize, FontStyle.Underline, GraphicsUnit.Point);

        // Existing blocks hold font references. Replace every block before disposing the old fonts.
        if (_currentResult is not null)
        {
            PopulateBlocks(_currentResult);
            _canvas.SetBlocks(_blocks);
        }
        if (IsHandleCreated)
        {
            Relayout();
        }
        foreach (var font in previousFonts)
        {
            font?.Dispose();
        }
    }

    public void SetWelcome(string shortcut)
    {
        SetResult(new LookupResult(
            string.Empty,
            $"Dictionary {Application.ProductVersion}",
            Array.Empty<HeadwordSection>(),
            $"Type a word and press Enter, or press {shortcut} from another application.\n" +
            "Escape hides this window to the tray. Clicking X also keeps the shortcut active."));
    }

    public void SetResult(LookupResult result)
    {
        _currentResult = result;
        PopulateBlocks(result);
        _canvas.SetBlocks(_blocks);
        if (IsHandleCreated)
        {
            Relayout(resetScroll: true);
        }
    }

    private void PopulateBlocks(LookupResult result)
    {
        _blocks.Clear();
        if (result.Sections.Count == 0)
        {
            // Keep the title for welcome/error views; successful results already show
            // each resolved headword and the submitted query remains in the word field.
            _blocks.Add(new TextBlock(result.DisplayTitle, _titleFont, SystemColors.WindowText, 0, 14));
        }

        if (result.Message is not null)
        {
            _blocks.Add(new TextBlock(result.Message, _bodyFont, SystemColors.GrayText, 0, 8));
        }

        foreach (var section in result.Sections)
        {
            _blocks.Add(new TextBlock(section.DisplayName, _headwordFont, SystemColors.WindowText, 8, 8));
            foreach (var group in section.Groups)
            {
                _blocks.Add(new TextBlock(group.PartOfSpeech, _posFont, SystemColors.GrayText, 3, 3));
                for (var index = 0; index < group.Meanings.Count; index++)
                {
                    var meaning = group.Meanings[index];
                    _blocks.Add(new TextBlock($"{index + 1}. {meaning.Definition}", _bodyFont, SystemColors.WindowText, 2, 2, 1));
                    foreach (var example in meaning.Examples)
                    {
                        _blocks.Add(new TextBlock("Example: " + example, _exampleFont, SystemColors.GrayText, 0, 1, 2));
                    }
                    if (meaning.Synonyms.Count > 0)
                    {
                        _blocks.Add(new SynonymBlock(meaning.Synonyms, _bodyFont, _linkFont, 0, 5, 2));
                    }
                }
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _canvas.SetBlocks(_blocks);
        Relayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated && _canvas.IsHandleCreated)
        {
            Relayout();
        }
    }

    private void Relayout(bool resetScroll = false)
    {
        if (_canvas.IsDisposed)
        {
            return;
        }

        // A scrolled child has a negative Location. Normalize scrolling before setting
        // its bounds; otherwise resetting the scroll later shifts it down by the old offset.
        var previousScroll = resetScroll ? Point.Empty : AutoScrollPosition;
        AutoScrollPosition = Point.Empty;

        using var graphics = _canvas.CreateGraphics();
        var scale = _canvas.DeviceDpi / 96f;
        var width = Math.Max(160, ClientSize.Width - (int)(4 * scale));
        LayoutBlocks(graphics, width, scale);
        if (_contentHeight > ClientSize.Height)
        {
            width = Math.Max(160, ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            LayoutBlocks(graphics, width, scale);
        }
        _canvas.SetBounds(0, 0, width, Math.Max(_contentHeight, ClientSize.Height));
        AutoScrollMinSize = new Size(0, _contentHeight);
        if (!resetScroll && previousScroll != Point.Empty)
        {
            AutoScrollPosition = previousScroll;
        }
        _canvas.Invalidate();
    }

    private void LayoutBlocks(Graphics graphics, int width, float scale)
    {
        var y = (int)(14 * scale);
        foreach (var block in _blocks)
        {
            block.Y = y;
            block.Layout(graphics, width, scale);
            y += block.Height;
        }
        _contentHeight = y + (int)(14 * scale);
    }

    private void RaiseSynonymClicked(string synonym) => SynonymClicked?.Invoke(this, new SynonymEventArgs(synonym));

    private void DisposeFonts()
    {
        _titleFont?.Dispose(); _headwordFont?.Dispose(); _posFont?.Dispose();
        _bodyFont?.Dispose(); _exampleFont?.Dispose(); _linkFont?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeFonts();
        }
        base.Dispose(disposing);
    }

    private abstract class Block
    {
        public int Y { get; set; }
        public int Height { get; protected set; }
        public abstract void Layout(Graphics graphics, int width, float scale);
        public abstract void Draw(Graphics graphics, Rectangle clip);
    }

    private sealed class TextBlock(
        string text, Font font, Color color, int topMargin, int bottomMargin, int indent = 0) : Block
    {
        private int _top;
        private int _x;
        private int _width;

        public override void Layout(Graphics graphics, int width, float scale)
        {
            _top = (int)(topMargin * scale);
            var bottom = (int)(bottomMargin * scale);
            _x = (int)((12 + indent * 18) * scale);
            _width = Math.Max(40, width - _x - (int)(12 * scale));
            var size = TextRenderer.MeasureText(graphics, text, font,
                new Size(_width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.ExternalLeading);
            Height = _top + Math.Max(size.Height, font.Height) + bottom;
        }

        public override void Draw(Graphics graphics, Rectangle clip)
        {
            var bounds = new Rectangle(_x, Y + _top, _width, Height - _top);
            if (!bounds.IntersectsWith(clip))
            {
                return;
            }
            TextRenderer.DrawText(graphics, text, font, bounds, color,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.ExternalLeading | TextFormatFlags.Top);
        }
    }

    private sealed class SynonymBlock(
        IReadOnlyList<string> synonyms, Font normalFont, Font linkFont,
        int topMargin, int bottomMargin, int indent) : Block
    {
        private readonly List<LinkRegion> _links = new();
        private Rectangle _label;

        public IEnumerable<LinkRegion> Links => _links;

        public override void Layout(Graphics graphics, int width, float scale)
        {
            _links.Clear();
            var top = (int)(topMargin * scale);
            var bottom = (int)(bottomMargin * scale);
            var padding = (int)(12 * scale);
            var startX = padding + (int)(indent * 18 * scale);
            var x = startX;
            var relativeY = top;
            var lineHeight = Math.Max(normalFont.Height, linkFont.Height);
            var lineGap = (int)(2 * scale);
            const string label = "Synonyms: ";
            var labelSize = MeasureSingleLine(graphics, label, normalFont);
            _label = new Rectangle(x, Y + relativeY, labelSize.Width, lineHeight);
            x += labelSize.Width;

            for (var index = 0; index < synonyms.Count; index++)
            {
                var text = synonyms[index] + (index + 1 < synonyms.Count ? ", " : string.Empty);
                var singleSize = MeasureSingleLine(graphics, text, linkFont);
                if (x > startX && x + singleSize.Width > width - padding)
                {
                    x = startX;
                    relativeY += lineHeight + lineGap;
                    lineHeight = Math.Max(normalFont.Height, linkFont.Height);
                }

                var available = Math.Max(30, width - padding - x);
                Size size;
                if (singleSize.Width > available)
                {
                    size = TextRenderer.MeasureText(graphics, text, linkFont,
                        new Size(available, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    size.Width = available;
                }
                else
                {
                    size = singleSize;
                }

                var region = new LinkRegion(
                    new Rectangle(x, Y + relativeY, Math.Max(1, size.Width), Math.Max(1, size.Height)),
                    synonyms[index], text, linkFont);
                _links.Add(region);
                x += size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
            }

            Height = relativeY + lineHeight + bottom;
        }

        public override void Draw(Graphics graphics, Rectangle clip)
        {
            if (_label.IntersectsWith(clip))
            {
                TextRenderer.DrawText(graphics, "Synonyms: ", normalFont, _label, SystemColors.GrayText,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.Top);
            }
            foreach (var link in _links)
            {
                if (!link.Bounds.IntersectsWith(clip))
                {
                    continue;
                }
                var flags = link.Bounds.Height > linkFont.Height + 2
                    ? TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Top
                    : TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.Top;
                TextRenderer.DrawText(graphics, link.DisplayText, linkFont, link.Bounds, SystemColors.HotTrack, flags);
            }
        }

        private static Size MeasureSingleLine(Graphics graphics, string text, Font font) =>
            TextRenderer.MeasureText(graphics, text, font,
                new Size(int.MaxValue / 16, font.Height * 2), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private sealed record LinkRegion(Rectangle Bounds, string Lookup, string DisplayText, Font Font);

    private sealed class ResultCanvas : Control
    {
        private readonly ResultsView _owner;
        private IReadOnlyList<Block> _blocks = Array.Empty<Block>();

        public ResultCanvas(ResultsView owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public void SetBlocks(IReadOnlyList<Block> blocks)
        {
            _blocks = blocks;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            foreach (var block in _blocks)
            {
                block.Draw(e.Graphics, e.ClipRectangle);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = FindLink(e.Location) is null ? Cursors.Default : Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && FindLink(e.Location) is { } link)
            {
                _owner.RaiseSynonymClicked(link.Lookup);
            }
        }

        private LinkRegion? FindLink(Point point) => _blocks.OfType<SynonymBlock>()
            .SelectMany(block => block.Links)
            .FirstOrDefault(link => link.Bounds.Contains(point));
    }
}
