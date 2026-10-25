using PortableDictionary.Core;
using Xunit;

namespace Dictionary.Tests;

public sealed class LookupFixture : IDisposable
{
    public LookupFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "dictionary.db");
        Repository = new DictionaryRepository(path);
        Lookup = new LookupService(Repository);
    }

    public DictionaryRepository Repository { get; }
    public LookupService Lookup { get; }
    public void Dispose() => Repository.Dispose();
}

public sealed class LookupTests(LookupFixture fixture) : IClassFixture<LookupFixture>
{
    [Fact]
    public void ExactPluralEntryFollowsItsBaseForm()
    {
        var result = fixture.Lookup.Lookup("occasions");
        Assert.True(result.IsSuccess);
        Assert.Equal("occasions", result.DisplayTitle);
        Assert.Equal(new[] { "occasion", "occasions" }, result.Sections.Select(s => s.LookupKey));
        Assert.False(result.Sections[0].IsExactSpelling);
        Assert.True(result.Sections[1].IsExactSpelling);
        Assert.Equal("something you have to do", result.Sections[1].Groups[0].Meanings[0].Definition);
    }

    [Theory]
    [InlineData("running", "run")]
    [InlineData("ran", "run")]
    [InlineData("mice", "mouse")]
    [InlineData("went", "go")]
    public void InflectedFormsResolveToExpectedBase(string query, string expectedBase)
    {
        var result = fixture.Lookup.Lookup(query);
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBase, result.Sections[0].LookupKey);
        Assert.False(result.Sections[0].IsExactSpelling);
    }

    [Fact]
    public void RunningRetainsItsExactEntryAfterRun()
    {
        var result = fixture.Lookup.Lookup("running");
        Assert.Equal(new[] { "run", "running" }, result.Sections.Select(s => s.LookupKey));
    }

    [Fact]
    public void LegitimateWordEndingInSDoesNotResolveToAnotherPartOfSpeech()
    {
        var result = fixture.Lookup.Lookup("news");
        var section = Assert.Single(result.Sections);
        Assert.Equal("news", section.LookupKey);
        Assert.True(section.IsExactSpelling);
        Assert.Equal(5, section.Groups.SelectMany(g => g.Meanings).Count());
    }

    [Fact]
    public void AmbiguousAxesKeepsAllValidatedRootsInStableOrder()
    {
        var first = fixture.Lookup.Lookup("axes");
        var second = fixture.Lookup.Lookup("AXES");
        Assert.Equal(new[] { "ax", "axis", "axe" }, first.Sections.Select(s => s.LookupKey).Take(3));
        Assert.Equal(first.Sections.Select(s => s.LookupKey), second.Sections.Select(s => s.LookupKey));
    }

    [Fact]
    public void BetterKeepsGoodAndWellBeforeExactEntry()
    {
        var result = fixture.Lookup.Lookup("better");
        Assert.Equal(new[] { "good", "well", "better" }, result.Sections.Select(s => s.LookupKey));
        Assert.False(result.Sections[0].IsExactSpelling);
        Assert.False(result.Sections[1].IsExactSpelling);
        Assert.True(result.Sections[2].IsExactSpelling);
    }

    [Fact]
    public void LookupIsCaseInsensitiveButTitleUsesSubmittedQuery()
    {
        var result = fixture.Lookup.Lookup("OcCaSiOnS");
        Assert.Equal("OcCaSiOnS", result.DisplayTitle);
        Assert.Equal(new[] { "occasion", "occasions" }, result.Sections.Select(s => s.LookupKey));
        Assert.Equal("occasion", result.Sections[0].DisplayName);
    }

    [Fact]
    public void MeaningRetainsSourceExamplesAndMeaningSpecificSynonyms()
    {
        var result = fixture.Lookup.Lookup("occasion");
        var meaning = result.Sections[0].Groups.First(g => g.PartOfSpeech == "Noun").Meanings[0];
        Assert.Equal("an event that occurs at a critical time", meaning.Definition);
        Assert.Equal(new[]
        {
            "at such junctures he always had an impulse to leave",
            "it was needed only on special occasions"
        }, meaning.Examples);
        Assert.Contains("juncture", meaning.Synonyms);
        Assert.DoesNotContain("occasion", meaning.Synonyms, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("occasions", meaning.Synonyms, StringComparer.OrdinalIgnoreCase);

        var otherMeaning = result.Sections[0].Groups.First(g => g.PartOfSpeech == "Noun").Meanings[1];
        Assert.Contains("affair", otherMeaning.Synonyms);
        Assert.DoesNotContain("juncture", otherMeaning.Synonyms);
    }

    [Fact]
    public void MultiwordInputAcceptsUnderscoresAndDisplaysSpaces()
    {
        var result = fixture.Lookup.Lookup("social_gathering");
        Assert.True(result.IsSuccess);
        Assert.Equal("social gathering", result.DisplayTitle);
        Assert.DoesNotContain(result.Sections.Select(s => s.DisplayName), name => name.Contains('_'));
    }

    [Fact]
    public void SurroundingPunctuationDoesNotReplacePreservedOriginal()
    {
        var result = fixture.Lookup.Lookup("  “occasions,”  ");
        Assert.True(result.IsSuccess);
        Assert.Equal("“occasions,”", result.DisplayTitle);
        Assert.Equal(new[] { "occasion", "occasions" }, result.Sections.Select(s => s.LookupKey));
    }

    [Fact]
    public void MultipleDefinitionsRemainDistinctRecords()
    {
        var result = fixture.Lookup.Lookup("better");
        var keys = result.Sections.SelectMany(s => s.Groups)
            .SelectMany(g => g.Meanings)
            .Select(m => (m.SenseId, m.Definition))
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void UnknownWordHasBriefFailureAndNoSections()
    {
        var result = fixture.Lookup.Lookup("zzzznotaword");
        Assert.False(result.IsSuccess);
        Assert.Contains("No dictionary entry found", result.Message);
    }

    [Fact]
    public void ExcessivelyLongInputIsRejectedWithoutTruncation()
    {
        var query = new string('x', TextNormalization.MaximumQueryLength + 1);
        var result = fixture.Lookup.Lookup(query);
        Assert.False(result.IsSuccess);
        Assert.Equal(query, result.Query);
        Assert.Contains("160", result.Message);
    }
}
