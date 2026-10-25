namespace PortableDictionary.Core;

public static class DataContract
{
    public const int SchemaVersion = 1;
    public const string DatasetId = "oewn";
    public const string DatasetVersion = "2025";
    public const string DatasetName = "Open English WordNet 2025 Edition";
}

public sealed record DatasetInfo(
    int SchemaVersion,
    string DatasetId,
    string DatasetVersion,
    string DatasetName,
    int EntryCount,
    int SynsetCount,
    int ExampleCount);
