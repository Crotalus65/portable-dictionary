using PortableDictionary.Core;

namespace PortableDictionary.Services;

public enum CaptureStatus
{
    Success,
    NoSelection,
    TooLong,
    Unavailable
}

public sealed record CaptureResult(CaptureStatus Status, string? Text = null, string? Detail = null);

// Kept independent of Windows/COM so the selection-versus-caret behavior can be
// tested on every build platform. The delegates operate on UI Automation ranges.
internal static class TextCaptureReader
{
    internal static CaptureResult ReadRanges<T>(IReadOnlyList<T>? ranges,
        Func<T, string> readText, Func<T, string> readWordAtCaret)
    {
        if (ranges is null || ranges.Count == 0)
        {
            return new CaptureResult(CaptureStatus.NoSelection);
        }

        var parts = ranges.Select(readText).ToArray();
        if (parts.Length == 1 && string.IsNullOrEmpty(parts[0]))
        {
            // GetSelection returns a zero-length range for an insertion point.
            // Expand only that range; never replace an actual selection with a word.
            var word = readWordAtCaret(ranges[0]);
            // Word units may include trailing whitespace, including the line
            // ending after the last word. Internal line breaks remain invalid.
            return !string.IsNullOrEmpty(word) && word.Any(char.IsLetterOrDigit)
                ? Classify(word.Trim())
                : new CaptureResult(CaptureStatus.NoSelection);
        }

        return Classify(string.Join("\n", parts));
    }

    internal static CaptureResult Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CaptureResult(CaptureStatus.NoSelection);
        }
        if (text.Contains('\r') || text.Contains('\n'))
        {
            return new CaptureResult(CaptureStatus.NoSelection, Detail: "Multiline text is not a dictionary entry.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length > TextNormalization.MaximumQueryLength)
        {
            return new CaptureResult(CaptureStatus.TooLong, Detail: $"The selection exceeds {TextNormalization.MaximumQueryLength} characters.");
        }
        return new CaptureResult(CaptureStatus.Success, trimmed);
    }
}
