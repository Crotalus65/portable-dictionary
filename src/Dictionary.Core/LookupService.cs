namespace PortableDictionary.Core;

public sealed class LookupService(DictionaryRepository repository)
{
    private readonly WordFormResolver _resolver = new(repository);

    public LookupResult Lookup(string? rawQuery)
    {
        var prepared = TextNormalization.Prepare(rawQuery);
        if (prepared.Original.Length == 0)
        {
            return new LookupResult(string.Empty, string.Empty, Array.Empty<HeadwordSection>(), "Enter a word or short dictionary entry.");
        }
        if (prepared.IsTooLong)
        {
            return new LookupResult(
                prepared.Original,
                "Query too long",
                Array.Empty<HeadwordSection>(),
                $"Please use {TextNormalization.MaximumQueryLength} characters or fewer.");
        }

        var primaryKeys = TextNormalization.GetPrimaryLookupKeys(prepared);
        var sections = ResolveSections(primaryKeys, prepared.Original);
        if (sections.Count == 0)
        {
            var fallbackKeys = TextNormalization.GetFallbackLookupKeys(prepared, primaryKeys);
            sections = ResolveSections(fallbackKeys, prepared.Original);
        }

        return sections.Count > 0
            ? new LookupResult(prepared.Original, prepared.DisplayTitle, sections)
            : new LookupResult(
                prepared.Original,
                prepared.DisplayTitle.Length > 0 ? prepared.DisplayTitle : prepared.Original,
                Array.Empty<HeadwordSection>(),
                "No dictionary entry found. The search field is ready for another query.");
    }

    private IReadOnlyList<HeadwordSection> ResolveSections(IEnumerable<string> lookupKeys, string originalQuery)
    {
        var baseKeys = new List<string>();
        var exactKeys = new List<string>();
        var seenBases = new HashSet<string>(StringComparer.Ordinal);
        var seenExact = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in lookupKeys)
        {
            if (repository.HasAnyEntry(key))
            {
                seenExact.Add(key);
                if (!exactKeys.Contains(key, StringComparer.Ordinal))
                {
                    exactKeys.Add(key);
                }
            }

            foreach (var candidate in _resolver.ResolveBaseForms(key).OrderBy(c => c.Order))
            {
                if (seenBases.Add(candidate.LookupKey))
                {
                    baseKeys.Add(candidate.LookupKey);
                }
            }
        }

        var resolved = new List<ResolvedHeadword>();
        resolved.AddRange(baseKeys.Select(key => new ResolvedHeadword(key, false)));
        resolved.AddRange(exactKeys.Where(key => !seenBases.Contains(key))
            .Select(key => new ResolvedHeadword(key, true)));

        var sections = new List<HeadwordSection>();
        foreach (var item in resolved)
        {
            var section = repository.BuildSection(item, originalQuery);
            if (section is not null)
            {
                sections.Add(section);
            }
        }
        return sections;
    }
}
