using PortableDictionary.Services;
using Xunit;

namespace Dictionary.Tests;

public sealed class ClipboardTextReaderTests
{
    private static readonly ClipboardStamp Original = new(10, new IntPtr(100));
    private sealed class Clipboard : IClipboardTextSource
    {
        public bool Available = true;
        public bool Unicode = true;
        public bool Closed;
        public bool Opened;
        public int Reads;
        public ClipboardStamp Current = Original;
        public string Text = "feign";
        public Action<Clipboard>? Rendering;
        public bool TryOpen() { Opened = Available; return Available; }
        public void Close() { Closed = true; Opened = false; }
        public ClipboardStamp Stamp => Current;
        public bool HasUnicodeText => Unicode;
        public string ReadUnicodeText(int maximumCharacters)
        {
            Assert.True(Opened);
            Reads++;
            Rendering?.Invoke(this);
            return Text[..Math.Min(Text.Length, maximumCharacters)];
        }
    }

    [Fact]
    public void PlainTextCopyReturnsTheCapturedWordAndRestorationStamp()
    {
        var source = new Clipboard();
        var read = ClipboardTextReader.Read(source, Original);
        Assert.Equal("feign", read.Result.Text);
        Assert.Equal(Original, read.Stamp);
        Assert.True(source.Closed);
    }

    [Fact]
    public void DelayedRenderingCanAdvanceTheSequenceWhileTheClipboardIsLocked()
    {
        var rendered = Original with { Sequence = 14 };
        var source = new Clipboard { Rendering = c => c.Current = rendered };
        var read = ClipboardTextReader.Read(source, Original);
        Assert.Equal(CaptureStatus.Success, read.Result.Status);
        Assert.Equal("feign", read.Result.Text);
        Assert.Equal(rendered, read.Stamp); // Restore against the rendered sequence.
        Assert.True(source.Closed);
    }

    [Fact]
    public void ANewerCopyBeforeOpeningIsNotReadOrRestored()
    {
        var source = new Clipboard { Current = Original with { Sequence = 11 } };
        var read = ClipboardTextReader.Read(source, Original);
        Assert.Equal(CaptureStatus.Unavailable, read.Result.Status);
        Assert.Null(read.Stamp);
        Assert.Equal(0, source.Reads);
        Assert.True(source.Closed);
    }

    [Fact]
    public void AnotherClipboardOwnerBeforeOpeningIsRejected()
    {
        var source = new Clipboard { Current = Original with { Owner = new IntPtr(200) } };
        Assert.Null(ClipboardTextReader.Read(source, Original).Stamp);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void BusyClipboardDoesNotAttemptToReadOrCloseSomeoneElsesLock()
    {
        var source = new Clipboard { Available = false };
        var read = ClipboardTextReader.Read(source, Original);
        Assert.Equal(CaptureStatus.Unavailable, read.Result.Status);
        Assert.Equal(0, source.Reads);
        Assert.False(source.Closed);
    }

    [Fact]
    public void NonTextCopyStillHasAStampForRestoringThePreviousClipboard()
    {
        var source = new Clipboard { Unicode = false };
        var read = ClipboardTextReader.Read(source, Original);
        Assert.Equal(CaptureStatus.NoSelection, read.Result.Status);
        Assert.Equal(Original, read.Stamp);
        Assert.Equal(0, source.Reads);
    }

    [Theory]
    [InlineData(160, CaptureStatus.Success)]
    [InlineData(161, CaptureStatus.TooLong)]
    public void ClipboardQueriesKeepTheExistingLengthLimit(int length, CaptureStatus expected)
    {
        var source = new Clipboard { Text = new string('a', length) };
        Assert.Equal(expected, ClipboardTextReader.Read(source, Original).Result.Status);
    }

    [Fact]
    public void MultilineTextIsRejected()
    {
        var source = new Clipboard { Text = "feign\r\nrunning" };
        Assert.Equal(CaptureStatus.NoSelection, ClipboardTextReader.Read(source, Original).Result.Status);
    }

    [Fact]
    public void FailedRenderingStillReleasesTheClipboardLock()
    {
        var source = new Clipboard { Rendering = _ => throw new InvalidOperationException("Rendering failed.") };
        Assert.Throws<InvalidOperationException>(() => ClipboardTextReader.Read(source, Original));
        Assert.True(source.Closed);
    }

    [Fact]
    public void OwnershipLossDuringRenderingDoesNotProduceAQueryOrRestorationStamp()
    {
        var source = new Clipboard { Rendering = c => c.Current = Original with { Owner = IntPtr.Zero } };
        var read = ClipboardTextReader.Read(source, Original);
        Assert.Equal(CaptureStatus.Unavailable, read.Result.Status);
        Assert.Null(read.Stamp);
        Assert.True(source.Closed);
    }
}
