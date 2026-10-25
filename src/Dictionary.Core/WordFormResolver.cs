namespace PortableDictionary.Core;

public sealed class WordFormResolver(DictionaryRepository repository)
{
    private static readonly string[] PartOfSpeechOrder =
    {
        PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Adjective, PartOfSpeech.Adverb
    };

    private static readonly IReadOnlyDictionary<string, MorphRule[]> Rules =
        new Dictionary<string, MorphRule[]>(StringComparer.Ordinal)
        {
            [PartOfSpeech.Noun] = new[]
            {
                new MorphRule("s", ""), new MorphRule("ies", "y"),
                new MorphRule("men", "man"), new MorphRule("ves", "f"),
                new MorphRule("ves", "fe"), new MorphRule("ses", "s"),
                new MorphRule("xes", "x"), new MorphRule("ches", "ch"),
                new MorphRule("shes", "sh"), new MorphRule("sses", "ss")
            },
            [PartOfSpeech.Verb] = new[]
            {
                new MorphRule("s", ""), new MorphRule("ies", "y"),
                new MorphRule("ied", "y"), new MorphRule("es", "e"),
                new MorphRule("es", ""), new MorphRule("ed", "e", true),
                new MorphRule("ed", "", true), new MorphRule("ing", "e", true),
                new MorphRule("ing", "", true)
            },
            [PartOfSpeech.Adjective] = new[]
            {
                new MorphRule("er", ""), new MorphRule("est", ""),
                new MorphRule("er", "e", true), new MorphRule("est", "e", true),
                new MorphRule("er", "", true), new MorphRule("est", "", true)
            },
            [PartOfSpeech.Adverb] = Array.Empty<MorphRule>()
        };

    public IReadOnlyList<BaseFormCandidate> ResolveBaseForms(string lookupKey)
    {
        var results = new List<BaseFormCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { lookupKey };

        foreach (var pos in PartOfSpeechOrder)
        {
            foreach (var mapping in repository.GetExceptionMappings(lookupKey, pos))
            {
                AddCandidate(mapping.BaseKey, pos);
            }

            foreach (var rule in Rules[pos])
            {
                if (!lookupKey.EndsWith(rule.Suffix, StringComparison.Ordinal) ||
                    lookupKey.Length <= rule.Suffix.Length)
                {
                    continue;
                }

                var stem = lookupKey[..^rule.Suffix.Length];
                AddCandidate(stem + rule.Replacement, pos);
                if (rule.AllowDoubledConsonant && HasDoubledFinalConsonant(stem))
                {
                    AddCandidate(stem[..^1] + rule.Replacement, pos);
                }
            }
        }

        return results;

        void AddCandidate(string candidate, string pos)
        {
            candidate = TextNormalization.NormalizeLookupKey(candidate);
            if (candidate.Length == 0 || seen.Contains(candidate) ||
                !repository.EntryExists(candidate, pos))
            {
                return;
            }
            seen.Add(candidate);
            results.Add(new BaseFormCandidate(candidate, pos, results.Count));
        }
    }

    private static bool HasDoubledFinalConsonant(string stem)
    {
        if (stem.Length < 2 || stem[^1] != stem[^2])
        {
            return false;
        }
        var c = stem[^1];
        return c is >= 'a' and <= 'z' && !"aeiou".Contains(c, StringComparison.Ordinal);
    }

    private sealed record MorphRule(string Suffix, string Replacement, bool AllowDoubledConsonant = false);
}
