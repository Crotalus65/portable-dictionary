using System.Globalization;
using System.Text;

namespace PortableDictionary.Core;

public sealed record PreparedQuery(string Original, string DisplayTitle, string ConservativeText, bool IsTooLong)
{
    public bool IsEmpty => ConservativeText.Length == 0 && Original.Length == 0;
}

public static class TextNormalization
{
    public const int MaximumQueryLength = 160;

    private static readonly (char Open, char Close)[] EnclosingPairs =
    {
        ('(', ')'), ('[', ']'), ('{', '}'), ('<', '>'),
        ('“', '”'), ('‘', '’'), ('«', '»'), ('"', '"')
    };

    public static PreparedQuery Prepare(string? value)
    {
        var original = (value ?? string.Empty).Trim();
        var conservative = StripSurroundingPunctuation(original);
        return new PreparedQuery(
            original,
            DisplayText(original),
            conservative,
            original.Length > MaximumQueryLength);
    }

    public static string NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = DisplayText(value).Normalize(NormalizationForm.FormC);
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    public static string DisplayText(string? value) => (value ?? string.Empty).Replace('_', ' ');

    public static string NormalizeSynonymForComparison(string? value) =>
        NormalizeLookupKey(DisplayText(value).Trim());

    public static IReadOnlyList<string> GetPrimaryLookupKeys(PreparedQuery query)
    {
        var keys = new List<string>();
        AddDistinct(keys, NormalizeLookupKey(query.Original));
        AddDistinct(keys, NormalizeLookupKey(query.ConservativeText));
        return keys;
    }

    public static IReadOnlyList<string> GetFallbackLookupKeys(PreparedQuery query, IEnumerable<string> alreadyTried)
    {
        var tried = new HashSet<string>(alreadyTried, StringComparer.Ordinal);
        var keys = new List<string>();
        var text = query.ConservativeText;
        if (text.Length == 0)
        {
            return keys;
        }

        AddVariant(text.Replace('-', ' '));
        AddVariant(text.Replace(' ', '-'));
        AddVariant(text.TrimEnd('.', '…').Trim());
        AddVariant(text.Replace('’', '\''));
        AddVariant(text.Replace('\'', '’'));
        return keys;

        void AddVariant(string variant)
        {
            var key = NormalizeLookupKey(variant);
            if (key.Length > 0 && tried.Add(key))
            {
                keys.Add(key);
            }
        }
    }

    private static string StripSurroundingPunctuation(string value)
    {
        var result = value.Trim();
        var changed = true;
        while (changed && result.Length >= 2)
        {
            changed = false;
            foreach (var (open, close) in EnclosingPairs)
            {
                if (result[0] == open && result[^1] == close)
                {
                    result = result[1..^1].Trim();
                    changed = true;
                    break;
                }
            }
        }

        result = result.TrimStart('(', '[', '{', '<', '“', '”', '«', '"');
        result = result.TrimEnd(')', ']', '}', '>', '“', '”', '»', '"', ',', ';', ':', '!', '?', '…');
        return result.Trim();
    }

    private static void AddDistinct(List<string> keys, string key)
    {
        if (key.Length > 0 && !keys.Contains(key, StringComparer.Ordinal))
        {
            keys.Add(key);
        }
    }
}

public static class PartOfSpeech
{
    public const string Noun = "n";
    public const string Verb = "v";
    public const string Adjective = "a";
    public const string Adverb = "r";

    public static string GroupKey(string pos) => pos == "s" ? Adjective : pos;

    public static string DisplayName(string pos) => GroupKey(pos) switch
    {
        Noun => "Noun",
        Verb => "Verb",
        Adjective => "Adjective",
        Adverb => "Adverb",
        _ => pos.ToUpper(CultureInfo.InvariantCulture)
    };
}
