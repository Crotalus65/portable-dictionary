namespace PortableDictionary.Core;

public sealed record LookupResult(
    string Query,
    string DisplayTitle,
    IReadOnlyList<HeadwordSection> Sections,
    string? Message = null)
{
    public bool IsSuccess => Sections.Count > 0;
}

public sealed record HeadwordSection(
    string LookupKey,
    string DisplayName,
    bool IsExactSpelling,
    IReadOnlyList<PartOfSpeechGroup> Groups);

public sealed record PartOfSpeechGroup(string PartOfSpeech, IReadOnlyList<Meaning> Meanings);

public sealed record Meaning(
    string SenseId,
    string SynsetId,
    string Definition,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Synonyms);

public sealed record ResolvedHeadword(string LookupKey, bool IsExactSpelling);

public sealed record BaseFormCandidate(string LookupKey, string PartOfSpeech, int Order);
