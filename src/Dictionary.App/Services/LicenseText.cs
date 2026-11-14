using System.Text.RegularExpressions;

namespace PortableDictionary.Services;

public static class LicenseText
{
    public static string ForDisplay(string filename, string content)
    {
        // The OEWN WNDB notice carries a sequential line-number column, unlike
        // legal clause numbers. Strip that column only in this known file and
        // only when every content line has the complete 1..N sequence.
        if (!string.Equals(filename, "WNDB_License.txt", StringComparison.Ordinal)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var count = lines.Length;
        while (count > 0 && lines[count - 1].Length == 0) count--;
        if (count < 2) return content;
        var display = (string[])lines.Clone();
        for (var index = 0; index < count; index++)
        {
            var match = Regex.Match(lines[index], @"^[ \t]*(\d+)[ \t](.*)$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number) || number != index + 1)
                return content;
            display[index] = match.Groups[2].Value;
        }
        return string.Join("\n", display);
    }
}
