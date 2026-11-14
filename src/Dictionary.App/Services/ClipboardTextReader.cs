using PortableDictionary.Core;

namespace PortableDictionary.Services;

internal readonly record struct ClipboardStamp(uint Sequence, IntPtr Owner);
internal sealed record ClipboardRead(CaptureResult Result, ClipboardStamp? Stamp = null);

internal interface IClipboardTextSource
{
    bool TryOpen();
    void Close();
    ClipboardStamp Stamp { get; }
    bool HasUnicodeText { get; }
    string? ReadUnicodeText(int maximumCharacters);
}

internal static class ClipboardTextReader
{
    internal static ClipboardRead Read(IClipboardTextSource source, ClipboardStamp expected)
    {
        if (!source.TryOpen())
        {
            return new(new CaptureResult(CaptureStatus.Unavailable, Detail: "The clipboard is busy."));
        }
        try
        {
            if (source.Stamp != expected)
            {
                return new(new CaptureResult(CaptureStatus.Unavailable, Detail: "The clipboard changed before it could be read."));
            }
            if (!source.HasUnicodeText)
            {
                return new(new CaptureResult(CaptureStatus.NoSelection), source.Stamp);
            }

            // GetClipboardData can trigger delayed rendering by the owner. While
            // OpenClipboard holds the lock, another application cannot replace
            // the data; a sequence increase during this read is legitimate.
            var text = source.ReadUnicodeText(TextNormalization.MaximumQueryLength + 1);
            var rendered = source.Stamp;
            if (rendered.Owner != expected.Owner)
            {
                return new(new CaptureResult(CaptureStatus.Unavailable, Detail: "Clipboard ownership changed during capture."));
            }
            return new(TextCaptureReader.Classify(text), rendered);
        }
        finally
        {
            source.Close();
        }
    }
}
