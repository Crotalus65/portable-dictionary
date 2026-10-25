using PortableDictionary.Core;

namespace PortableDictionary.Forms;

public sealed class AboutDialog : Form
{
    public AboutDialog(DatasetInfo data)
    {
        Text = $"About Dictionary {Application.ProductVersion}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(620, 460);

        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10f),
            Text = $"Portable Dictionary and Thesaurus {Application.ProductVersion}\r\n\r\n" +
                   $"Dictionary data: {data.DatasetName} ({data.DatasetId}:{data.DatasetVersion}).\r\n" +
                   $"Prepared-data schema: {data.SchemaVersion}.\r\n" +
                   $"{data.EntryCount:N0} lexical entries; {data.SynsetCount:N0} synsets; {data.ExampleCount:N0} examples.\r\n\r\n" +
                   "Open English WordNet is developed by the Open English WordNet team. " +
                   "Copyright (c) 2019–present, The Open English WordNet Team. " +
                   "Licensed under Creative Commons Attribution 4.0 International (CC BY 4.0).\r\n\r\n" +
                   "This resource is derived from Princeton WordNet under the WordNet License. " +
                   "WordNet 3.1 Copyright 2011 by Princeton University. All rights reserved.\r\n\r\n" +
                   "Definitions and examples are preserved from the structured source data. " +
                   "Neither dataset is described as public domain. Full notices are included in the licenses folder."
        };

        var licenses = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(8, 8, 8, 0),
            Margin = Padding.Empty
        };
        licenses.Controls.Add(LicenseButton("OEWN && CC BY 4.0 notice…", "OEWN-LICENSE.md"));
        licenses.Controls.Add(LicenseButton("CC BY 4.0 legal code…", "CC-BY-4.0.txt"));
        licenses.Controls.Add(LicenseButton("Princeton WordNet notice…", "WNDB_License.txt"));

        var closePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8, 4, 12, 12),
            Margin = Padding.Empty
        };
        var close = new Button
        {
            Text = "Close",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.OK
        };
        closePanel.Controls.Add(close);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(licenses, 0, 1);
        layout.Controls.Add(closePanel, 0, 2);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
        Shown += (_, _) =>
        {
            text.SelectionStart = 0;
            text.SelectionLength = 0;
            close.Focus();
        };
    }

    private Button LicenseButton(string label, string filename)
    {
        var button = new Button
        {
            Text = label,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 32),
            Padding = new Padding(4, 2, 4, 2),
            Margin = new Padding(4)
        };
        button.Click += (_, _) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "licenses", filename);
            using var dialog = new LicenseDialog(label.TrimEnd('…'), path);
            dialog.ShowDialog(this);
        };
        return button;
    }
}

public sealed class LicenseDialog : Form
{
    public LicenseDialog(string title, string path)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 600);
        AutoScaleMode = AutoScaleMode.Dpi;
        var content = File.Exists(path)
            ? File.ReadAllText(path)
            : "The license file could not be found. Restore the licenses folder from the portable distribution.";
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10f),
            Text = content.Replace("\r\n", "\n").Replace("\n", "\r\n")
        };
        Controls.Add(box);
    }
}
