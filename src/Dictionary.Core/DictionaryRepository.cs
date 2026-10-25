using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PortableDictionary.Core;

public sealed record ExceptionMapping(string BaseKey, int SourceOrder, int Id);

public sealed class DictionaryRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private DatasetInfo? _datasetInfo;

    public DictionaryRepository(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("The bundled dictionary database was not found.", databasePath);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        _connection = new SqliteConnection(builder.ToString());
        try
        {
            _connection.Open();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "PRAGMA query_only = ON; PRAGMA mmap_size = 268435456; PRAGMA temp_store = MEMORY;";
                command.ExecuteNonQuery();
            }
            _datasetInfo = ReadAndValidateMetadata();
        }
        catch
        {
            // A rejected database must not stay open: on Windows an open file cannot be replaced or deleted,
            // which also made the schema-rejection tests fail there.
            _connection.Dispose();
            throw;
        }
    }

    public DatasetInfo DatasetInfo => _datasetInfo!;

    public bool HasAnyEntry(string lookupKey)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM entries WHERE lookup_key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", lookupKey);
        return command.ExecuteScalar() is not null;
    }

    public bool EntryExists(string lookupKey, string partOfSpeech)
    {
        partOfSpeech = PartOfSpeech.GroupKey(partOfSpeech);
        using var command = _connection.CreateCommand();
        command.CommandText = partOfSpeech == PartOfSpeech.Adjective
            ? "SELECT 1 FROM entries WHERE lookup_key = $key AND pos IN ('a', 's') LIMIT 1;"
            : "SELECT 1 FROM entries WHERE lookup_key = $key AND pos = $pos LIMIT 1;";
        command.Parameters.AddWithValue("$key", lookupKey);
        if (partOfSpeech != PartOfSpeech.Adjective)
        {
            command.Parameters.AddWithValue("$pos", partOfSpeech);
        }
        return command.ExecuteScalar() is not null;
    }

    public IReadOnlyList<ExceptionMapping> GetExceptionMappings(string lookupKey, string partOfSpeech)
    {
        var results = new List<ExceptionMapping>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT base_key, source_order, id
            FROM exceptions
            WHERE inflected_key = $key AND pos = $pos
            ORDER BY source_order, id;
            """;
        command.Parameters.AddWithValue("$key", lookupKey);
        command.Parameters.AddWithValue("$pos", PartOfSpeech.GroupKey(partOfSpeech));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ExceptionMapping(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }
        return results;
    }

    public HeadwordSection? BuildSection(ResolvedHeadword resolved, string originalQuery)
    {
        var entries = ReadEntries(resolved.LookupKey);
        if (entries.Count == 0)
        {
            return null;
        }

        var displayName = TextNormalization.DisplayText(entries[0].Lemma);
        var exampleCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var synonymCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var groups = new List<PartOfSpeechGroup>();

        foreach (var posGroup in entries.GroupBy(e => PartOfSpeech.GroupKey(e.Pos))
                     .OrderBy(g => PartOfSpeechRank(g.Key)))
        {
            var meanings = new List<Meaning>();
            var seen = new HashSet<(string SenseId, int DefinitionOrder)>();
            foreach (var entry in posGroup.OrderBy(e => e.SourceOrder))
            {
                foreach (var row in ReadDefinitions(entry.Id))
                {
                    if (!seen.Add((row.SenseId, row.DefinitionOrder)))
                    {
                        continue;
                    }

                    if (!exampleCache.TryGetValue(row.SynsetId, out var examples))
                    {
                        examples = ReadExamples(row.SynsetId);
                        exampleCache.Add(row.SynsetId, examples);
                    }
                    if (!synonymCache.TryGetValue(row.SynsetId, out var rawSynonyms))
                    {
                        rawSynonyms = ReadSynonyms(row.SynsetId);
                        synonymCache.Add(row.SynsetId, rawSynonyms);
                    }

                    var synonyms = SynonymFilter.Filter(rawSynonyms, originalQuery, displayName);
                    meanings.Add(new Meaning(
                        row.SenseId,
                        row.SynsetId,
                        TextNormalization.DisplayText(row.Definition),
                        examples,
                        synonyms));
                }
            }

            if (meanings.Count > 0)
            {
                groups.Add(new PartOfSpeechGroup(PartOfSpeech.DisplayName(posGroup.Key), meanings));
            }
        }

        return new HeadwordSection(resolved.LookupKey, displayName, resolved.IsExactSpelling, groups);
    }

    private IReadOnlyList<EntryRecord> ReadEntries(string lookupKey)
    {
        var entries = new List<EntryRecord>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, lemma, pos, source_order
            FROM entries
            WHERE lookup_key = $key
            ORDER BY source_order, id;
            """;
        command.Parameters.AddWithValue("$key", lookupKey);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new EntryRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        }
        return entries;
    }

    private IReadOnlyList<DefinitionRow> ReadDefinitions(long entryId)
    {
        var rows = new List<DefinitionRow>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.synset_id, d.definition_order, d.text
            FROM senses s
            CROSS JOIN definitions d ON d.synset_id = s.synset_id
            WHERE s.entry_id = $entry
            ORDER BY s.sense_order, d.definition_order;
            """;
        command.Parameters.AddWithValue("$entry", entryId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DefinitionRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        }
        return rows;
    }

    private IReadOnlyList<string> ReadExamples(string synsetId)
    {
        var examples = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT text FROM examples WHERE synset_id = $id ORDER BY example_order;";
        command.Parameters.AddWithValue("$id", synsetId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            examples.Add(TextNormalization.DisplayText(reader.GetString(0)));
        }
        return examples;
    }

    private IReadOnlyList<string> ReadSynonyms(string synsetId)
    {
        var synonyms = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT lemma
            FROM synset_members
            WHERE synset_id = $id
            ORDER BY source_order, sense_order, member_id;
            """;
        command.Parameters.AddWithValue("$id", synsetId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            synonyms.Add(reader.GetString(0));
        }
        return synonyms;
    }

    private DatasetInfo ReadAndValidateMetadata()
    {
        int userVersion;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;";
            userVersion = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM metadata;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                metadata[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("The dictionary database has no readable metadata table.", ex);
        }

        var schemaText = metadata.GetValueOrDefault("schema_version", "missing");
        if (userVersion != DataContract.SchemaVersion ||
            !int.TryParse(schemaText, out var schemaVersion) ||
            schemaVersion != DataContract.SchemaVersion)
        {
            throw new InvalidDataException(
                $"Incompatible dictionary data schema (PRAGMA {userVersion}, metadata {schemaText}). " +
                $"This application requires schema {DataContract.SchemaVersion}. Replace or rebuild data/dictionary.db.");
        }

        var datasetId = metadata.GetValueOrDefault("dataset_id", "missing");
        var datasetVersion = metadata.GetValueOrDefault("dataset_version", "missing");
        if (datasetId != DataContract.DatasetId || datasetVersion != DataContract.DatasetVersion)
        {
            throw new InvalidDataException(
                $"Unsupported dictionary dataset {datasetId}:{datasetVersion}. " +
                $"This application requires {DataContract.DatasetId}:{DataContract.DatasetVersion}.");
        }

        return new DatasetInfo(
            schemaVersion,
            datasetId,
            datasetVersion,
            metadata.GetValueOrDefault("dataset_name", DataContract.DatasetName),
            ReadCount("entry_count"),
            ReadCount("synset_count"),
            ReadCount("example_count"));

        int ReadCount(string key) => int.TryParse(metadata.GetValueOrDefault(key), out var count) ? count : 0;
    }

    private static int PartOfSpeechRank(string pos) => pos switch
    {
        PartOfSpeech.Noun => 0,
        PartOfSpeech.Verb => 1,
        PartOfSpeech.Adjective => 2,
        PartOfSpeech.Adverb => 3,
        _ => 4
    };

    public void Dispose() => _connection.Dispose();

    private sealed record EntryRecord(long Id, string Lemma, string Pos, int SourceOrder);
    private sealed record DefinitionRow(string SenseId, string SynsetId, int DefinitionOrder, string Definition);
}

public static class SynonymFilter
{
    public static IReadOnlyList<string> Filter(IEnumerable<string> candidates, string originalQuery, string resolvedHeadword)
    {
        var excludedQuery = TextNormalization.NormalizeSynonymForComparison(originalQuery);
        var excludedHeadword = TextNormalization.NormalizeSynonymForComparison(resolvedHeadword);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        foreach (var raw in candidates)
        {
            var display = string.Join(' ', TextNormalization.DisplayText(raw)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var key = TextNormalization.NormalizeSynonymForComparison(display);
            if (key.Length == 0 ||
                key.Equals(excludedQuery, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(excludedHeadword, StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(key))
            {
                continue;
            }
            results.Add(display);
        }
        return results;
    }
}
