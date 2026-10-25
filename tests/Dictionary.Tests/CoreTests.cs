using Microsoft.Data.Sqlite;
using PortableDictionary.Core;
using Xunit;

namespace Dictionary.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData("  “dog,”  ", "dog")]
    [InlineData("(mother-in-law's)", "mother-in-law's")]
    [InlineData("a.m.", "a.m.")]
    [InlineData("'twas", "'twas")]
    [InlineData(".22-caliber", ".22-caliber")]
    public void PunctuationNormalizationIsConservative(string input, string expected)
    {
        Assert.Equal(expected, TextNormalization.Prepare(input).ConservativeText);
    }

    [Fact]
    public void FallbacksAreAdditionalAndPreserveInternalCharacters()
    {
        var prepared = TextNormalization.Prepare("mother-in-law's");
        var primary = TextNormalization.GetPrimaryLookupKeys(prepared);
        Assert.Equal("mother-in-law's", Assert.Single(primary));
        var fallback = TextNormalization.GetFallbackLookupKeys(prepared, primary);
        Assert.Contains("mother in law's", fallback);
        Assert.Contains("mother-in-law’s", fallback);
    }

    [Fact]
    public void SynonymFilterExcludesQueryHeadwordAndNormalizedDuplicates()
    {
        var candidates = new[]
        {
            "Occasion", "occasions", "social_gathering", "Social Gathering",
            "Juncture", "juncture", "  "
        };
        var filtered = SynonymFilter.Filter(candidates, "OCCASIONS", "occasion");
        Assert.Equal(new[] { "social_gathering".Replace('_', ' '), "Juncture" }, filtered);
    }

    [Fact]
    public void SynonymFilterOmitsLineWhenNothingRemains()
    {
        Assert.Empty(SynonymFilter.Filter(new[] { "News", "news", "NEWS" }, "news", "news"));
    }

    [Fact]
    public void BackNavigationDoesNotCreateAnotherHistoryEntry()
    {
        var history = new NavigationHistory<string>();
        history.Navigate("first");
        history.Navigate("second");
        Assert.True(history.CanGoBack);
        Assert.Equal("first", history.GoBack());
        Assert.Equal(2, history.Count);
        Assert.False(history.CanGoBack);
        history.Navigate("third");
        Assert.Equal(2, history.Count);
        Assert.Equal("third", history.Current);
    }

    [Fact]
    public void SettingsRoundTripBesideExecutablePath()
    {
        var directory = Directory.CreateTempSubdirectory("dictionary-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            var store = new PortableSettingsStore(path);
            var settings = new AppSettings { TextSize = 14, WindowX = 120, WindowY = 140, Shortcut = "Ctrl+Shift+F7" };
            Assert.True(store.Save(settings, out var warning));
            Assert.Null(warning);
            var loaded = store.Load();
            Assert.Null(loaded.Warning);
            Assert.Equal(14, loaded.Settings.TextSize);
            Assert.Equal(120, loaded.Settings.WindowX);
            Assert.Equal("Ctrl+Shift+F7", loaded.Settings.Shortcut);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void UnwritableSettingsDestinationReturnsOneWarningAndKeepsMemoryModel()
    {
        var directory = Directory.CreateTempSubdirectory("dictionary-settings-blocked-");
        try
        {
            var store = new PortableSettingsStore(directory.FullName);
            var settings = new AppSettings { TextSize = 17 };
            Assert.False(store.Save(settings, out var firstWarning));
            Assert.NotNull(firstWarning);
            Assert.Equal(17, settings.TextSize);
            Assert.False(store.Save(settings, out var secondWarning));
            Assert.Null(secondWarning);
            Assert.False(File.Exists(Path.Combine(directory.Parent!.FullName, "settings.json")));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void InvalidSettingsFallsBackToDefaultsWithWarning()
    {
        var directory = Directory.CreateTempSubdirectory("dictionary-settings-invalid-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(path, "not json");
            var result = new PortableSettingsStore(path).Load();
            Assert.NotNull(result.Warning);
            Assert.Equal(10, result.Settings.TextSize);
            Assert.Equal("Ctrl+Alt+D", result.Settings.Shortcut);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void IncompatibleSchemaIsRejectedClearly()
    {
        var path = CreateMetadataDatabase(99, "99", "oewn", "2025");
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => new DictionaryRepository(path));
            Assert.Contains("schema", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsupportedDatasetVersionIsRejectedClearly()
    {
        var path = CreateMetadataDatabase(1, "1", "oewn", "1900");
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => new DictionaryRepository(path));
            Assert.Contains("Unsupported dictionary dataset", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateMetadataDatabase(int userVersion, string schema, string id, string version)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO metadata VALUES ('schema_version', '{schema}'), ('dataset_id', '{id}'), ('dataset_version', '{version}');
            PRAGMA user_version = {userVersion};
            """;
        command.ExecuteNonQuery();
        return path;
    }
}
