using System.Text.RegularExpressions;
using PortableDictionary.Core;

namespace PortableDictionary.Services;

public enum GuidePartKind { Title, Heading, Paragraph, NoticeLink }

public sealed record GuidePart(GuidePartKind Kind, string Text, string? NoticeFile = null);

public static class UserGuideDocument
{
    private static readonly HashSet<string> NoticeFiles = new(StringComparer.Ordinal)
    {
        "OEWN-LICENSE.md", "CC-BY-4.0.txt", "WNDB_License.txt"
    };

    public static IReadOnlyList<GuidePart> Create(string version, string shortcut, DatasetInfo data)
    {
        using var stream = typeof(UserGuideDocument).Assembly
            .GetManifestResourceStream("PortableDictionary.UserGuide.md")
            ?? throw new InvalidOperationException("The embedded user guide is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        var values = new Dictionary<string, string>
        {
            ["version"] = version, ["shortcut"] = shortcut,
            ["datasetName"] = data.DatasetName, ["datasetId"] = data.DatasetId,
            ["datasetVersion"] = data.DatasetVersion, ["schemaVersion"] = data.SchemaVersion.ToString(),
            ["entryCount"] = data.EntryCount.ToString("N0"),
            ["synsetCount"] = data.SynsetCount.ToString("N0"),
            ["exampleCount"] = data.ExampleCount.ToString("N0")
        };
        foreach (var (key, value) in values)
            text = text.Replace("{{" + key + "}}", value);

        var parts = new List<GuidePart>();
        foreach (var raw in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var block = raw.Trim();
            if (block.StartsWith("# ", StringComparison.Ordinal))
                parts.Add(new(GuidePartKind.Title, block[2..]));
            else if (block.StartsWith("## ", StringComparison.Ordinal))
                parts.Add(new(GuidePartKind.Heading, block[3..]));
            else
            {
                var link = Regex.Match(block, @"^\[([^\]]+)\]\(\.\./licenses/([A-Za-z0-9._-]+)\)$");
                if (link.Success && NoticeFiles.Contains(link.Groups[2].Value))
                    parts.Add(new(GuidePartKind.NoticeLink, link.Groups[1].Value, link.Groups[2].Value));
                else
                    parts.Add(new(GuidePartKind.Paragraph, string.Join(" ", block.Split('\n').Select(line => line.Trim()))));
            }
        }
        return parts;
    }
}
