using PortableDictionary.Core;
using PortableDictionary.Services;
using Xunit;

namespace Dictionary.Tests;

public sealed class UserGuideTests
{
    private static LookupResult Result(string word) => new(word, word, Array.Empty<HeadwordSection>());

    [Fact]
    public void StartupGuideHasNoLookupToReturnTo()
    {
        var navigation = new LookupNavigation();
        Assert.True(navigation.IsGuideVisible);
        Assert.False(navigation.CanGoBack);
        Assert.Null(navigation.GoBack());
        Assert.True(navigation.IsGuideVisible);
    }

    [Fact]
    public void GuideReturnsToCurrentLookupThenBackReachesEarlierLookup()
    {
        var navigation = new LookupNavigation();
        var first = Result("feign");
        var second = Result("running");
        navigation.Navigate(first);
        navigation.Navigate(second);
        navigation.ShowGuide();
        Assert.True(navigation.CanGoBack);
        Assert.Same(second, navigation.GoBack());
        Assert.False(navigation.IsGuideVisible);
        Assert.Same(first, navigation.GoBack());
        Assert.False(navigation.CanGoBack);
    }

    [Fact]
    public void ReopeningGuideDoesNotAddHistoryEntries()
    {
        var navigation = new LookupNavigation();
        var result = Result("feign");
        navigation.Navigate(result);
        navigation.ShowGuide();
        navigation.ShowGuide();
        Assert.Same(result, navigation.GoBack());
        Assert.False(navigation.CanGoBack);
        Assert.Null(navigation.GoBack());
    }

    [Fact]
    public void LookupFromGuideRetainsPreviousLookup()
    {
        var navigation = new LookupNavigation();
        var first = Result("feign");
        navigation.Navigate(first);
        navigation.ShowGuide();
        navigation.Navigate(Result("running"));
        Assert.False(navigation.IsGuideVisible);
        Assert.Same(first, navigation.GoBack());
    }

    [Fact]
    public void GuideAfterBackRetainsCurrentHistoryPosition()
    {
        var navigation = new LookupNavigation();
        var first = Result("feign");
        navigation.Navigate(first);
        navigation.Navigate(Result("running"));
        navigation.GoBack();
        navigation.ShowGuide();
        Assert.Same(first, navigation.GoBack());
        navigation.Navigate(Result("better"));
        Assert.Same(first, navigation.GoBack());
        Assert.False(navigation.CanGoBack);
    }

    [Fact]
    public void EmbeddedGuideUsesRequestedShortcutInstructionsVersionAndDataset()
    {
        var data = new DatasetInfo(1, "oewn", "2025", "Open English WordNet", 135969, 107519, 49596);
        var guide = UserGuideDocument.Create("1.0.5", "Ctrl+Shift+F8", data);
        var text = string.Join("\n", guide.Select(part => part.Text));
        Assert.Contains("press Ctrl+Alt+D", text);
        Assert.Contains("Portable Dictionary and Thesaurus 1.0.5", text);
        Assert.Contains("Open English WordNet (oewn:2025)", text);
        Assert.Contains(data.EntryCount.ToString("N0"), text);
        Assert.DoesNotContain("{{", text);
        Assert.Equal("Dictionary User Guide", guide[0].Text);
        Assert.Equal(GuidePartKind.Title, guide[0].Kind);
        var headings = guide.Where(part => part.Kind == GuidePartKind.Heading).Select(part => part.Text).ToList();
        Assert.Equal(headings.IndexOf("Change the lookup shortcut") + 1, headings.IndexOf("Reopen this guide"));
        Assert.Equal(headings.IndexOf("Reopen this guide") + 1, headings.IndexOf("If a lookup shortcut fails"));
        Assert.DoesNotContain("Definitions, examples, and synonyms are available", text);
        Assert.Contains("some PDF readers", text);
        var changed = UserGuideDocument.Create("1.0.5", "Alt+F9", data);
        Assert.Contains(changed, part => part.Text.Contains("press Ctrl+Alt+D"));
        Assert.DoesNotContain(changed, part => part.Text.Contains("Ctrl+Shift+F8"));
    }

    [Fact]
    public void AboutSectionEndsWithTheThreeBundledNoticeLinks()
    {
        var data = new DatasetInfo(1, "oewn", "2025", "Open English WordNet", 1, 1, 1);
        var guide = UserGuideDocument.Create("1.0.5", "Ctrl+Alt+D", data).ToList();
        var about = guide.FindIndex(part => part.Kind == GuidePartKind.Heading && part.Text == "About Dictionary");
        Assert.True(about > 0);
        Assert.Equal(new[] { "OEWN-LICENSE.md", "CC-BY-4.0.txt", "WNDB_License.txt" },
            guide.Where(part => part.Kind == GuidePartKind.NoticeLink).Select(part => part.NoticeFile));
        Assert.All(guide.TakeLast(3), part => Assert.Equal(GuidePartKind.NoticeLink, part.Kind));
        var text = string.Join(" ", guide.Select(part => part.Text));
        Assert.Contains("Libre Office, OnlyOffice, Textmaker", text);
    }

    [Fact]
    public void PrincetonDisplayOmitsLineNumbersAndKeepsNoticeWording()
    {
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licenses", "WNDB_License.txt"));
        var display = LicenseText.ForDisplay("WNDB_License.txt", original);
        Assert.StartsWith("This software and database is being provided to you, the LICENSEE, by", display);
        Assert.Contains("WordNet 3.1 Copyright 2011 by Princeton University.  All rights reserved.", display);
        Assert.Contains("INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR", display);
        Assert.EndsWith("Princeton University and LICENSEE agrees to preserve same.", display.TrimEnd());
        Assert.Equal(30, display.Split('\n').Count(line => line.Length > 0));
        // Reattach the original number-column prefixes. Every other byte must survive.
        var before = original.Replace("\r\n", "\n").Split('\n');
        var after = display.Split('\n');
        for (var index = 0; index < 30; index++)
        {
            var prefixLength = before[index].IndexOf((index + 1).ToString(), StringComparison.Ordinal)
                               + (index + 1).ToString().Length + 1;
            Assert.Equal(before[index], before[index][..prefixLength] + after[index]);
        }
    }

    [Theory]
    [InlineData("CC-BY-4.0.txt", "1 First clause\n2 Second clause\n")]
    [InlineData("WNDB_License.txt", "1 First line\n3 Not a complete sequence\n")]
    [InlineData("WNDB_License.txt", "Permission to use\n1 A numbered clause\n")]
    public void OtherNoticesAndUnrecognizedNumberingAreUnchanged(string filename, string original)
    {
        Assert.Equal(original, LicenseText.ForDisplay(filename, original));
    }
}
