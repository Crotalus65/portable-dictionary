using PortableDictionary.Core;
using PortableDictionary.Services;
using Xunit;

namespace Dictionary.Tests;

public sealed class TextCaptureTests(LookupFixture fixture) : IClassFixture<LookupFixture>
{
    private sealed record Range(string Selection, string WordAtCaret = "");

    private static CaptureResult Read(params Range[] ranges) => TextCaptureReader.ReadRanges(
        ranges, range => range.Selection, range => range.WordAtCaret);

    [Theory]
    [InlineData("feign ")]
    [InlineData("feign\r\n")]
    [InlineData("feign\n")]
    [InlineData(" feign\t")]
    public void EmptySelectionReadsTheWordAtTheInsertionPoint(string wordUnit)
    {
        var capture = Read(new Range("", wordUnit));
        Assert.Equal(CaptureStatus.Success, capture.Status);
        Assert.Equal("feign", capture.Text);
    }

    [Fact]
    public void ExplicitSelectionTakesPriorityOverTheCaretWord()
    {
        var capture = TextCaptureReader.ReadRanges(
            new[] { new Range("social gathering", "social") },
            range => range.Selection,
            _ => throw new InvalidOperationException("An actual selection must not be expanded."));
        Assert.Equal(CaptureStatus.Success, capture.Status);
        Assert.Equal("social gathering", capture.Text);
    }

    [Fact]
    public void NewCaretWordCanBeLookedUpAfterAPreviousQuery()
    {
        var previous = fixture.Lookup.Lookup("feign");
        var capture = Read(new Range("", "running"));
        Assert.Equal(CaptureStatus.Success, capture.Status);
        var next = fixture.Lookup.Lookup(capture.Text);
        Assert.True(next.IsSuccess);
        Assert.Equal("feign", previous.Query);
        Assert.Equal("running", next.Query);
        Assert.Equal("run", next.Sections[0].LookupKey);
    }

    [Theory]
    [InlineData("mother-in-law's")]
    [InlineData("a.m.")]
    [InlineData("“feign,”")]
    public void SelectedPunctuationAndInternalCharactersArePreserved(string text)
    {
        Assert.Equal(text, Read(new Range(text)).Text);
    }

    [Theory]
    [InlineData(160, CaptureStatus.Success)]
    [InlineData(161, CaptureStatus.TooLong)]
    public void BothSelectionAndCaretRespectTheQueryLimit(int length, CaptureStatus expected)
    {
        var text = new string('a', length);
        Assert.Equal(expected, Read(new Range(text)).Status);
        Assert.Equal(expected, Read(new Range("", text)).Status);
    }

    [Fact]
    public void MissingRangesDoNotInventACaretWord()
    {
        Assert.Equal(CaptureStatus.NoSelection, Read().Status);
        Assert.Equal(CaptureStatus.NoSelection, TextCaptureReader.ReadRanges<Range>(
            null, _ => throw new Exception(), _ => throw new Exception()).Status);
    }

    [Fact]
    public void WhitespaceSelectionIsNotReplacedWithAnotherWord()
    {
        var capture = TextCaptureReader.ReadRanges(
            new[] { new Range(" ") }, range => range.Selection,
            _ => throw new InvalidOperationException("Whitespace is not an insertion point."));
        Assert.Equal(CaptureStatus.NoSelection, capture.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("...")]
    [InlineData("\r\n")]
    public void EmptyOrPunctuationOnlyCaretRangesDoNotBecomeQueries(string word)
    {
        Assert.Equal(CaptureStatus.NoSelection, Read(new Range("", word)).Status);
    }

    [Fact]
    public void MultipleOrMultilineRangesAreNotCollapsedIntoAWord()
    {
        Assert.Equal(CaptureStatus.NoSelection, Read(new Range("feign"), new Range("running")).Status);
        Assert.Equal(CaptureStatus.NoSelection, Read(new Range("feign\nrunning", "feign")).Status);
        Assert.Equal(CaptureStatus.NoSelection, Read(new Range("", "feign\r\nrunning")).Status);
    }
}
