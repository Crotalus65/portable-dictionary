using PortableDictionary.Services;
using Xunit;

namespace Dictionary.Tests;

public sealed class CaptureCoordinatorTests
{
    private static readonly CaptureResult Nothing = new(CaptureStatus.NoSelection);
    private static CaptureResult Word(string text = "feign") => new(CaptureStatus.Success, text);
    private static CaptureCoordinator Coordinator() => new(0, 1000, 5000, 0);
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task FirstLateAutomationResultSurvivesAFailedCopyFallback()
    {
        var automation = Completion<CaptureResult>();
        var copyStarted = Completion<bool>();
        var automationCalls = 0;
        var capture = Coordinator().CaptureAsync(
            () => { automationCalls++; return automation.Task; },
            () => { copyStarted.SetResult(true); return Task.FromResult(Nothing); },
            () => true);

        await copyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(capture.IsCompleted);
        automation.SetResult(Word());
        var result = await capture.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("feign", result.Text);
        Assert.Equal(1, automationCalls); // No overlapping replacement UIA worker.
    }

    [Fact]
    public async Task FirstProviderThatInitiallyReturnsNoSelectionIsReadAgain()
    {
        var automationCalls = 0;
        var copyCalls = 0;
        var result = await Coordinator().CaptureAsync(
            () => Task.FromResult(++automationCalls == 1 ? Nothing : Word("running")),
            () => { copyCalls++; return Task.FromResult(Nothing); },
            () => true);

        Assert.Equal(CaptureStatus.Success, result.Status);
        Assert.Equal("running", result.Text);
        Assert.Equal(2, automationCalls);
        Assert.Equal(1, copyCalls);
    }

    [Fact]
    public async Task AutomationFailureStillAllowsTheCopyFallback()
    {
        var result = await Coordinator().CaptureAsync(
            () => Task.FromException<CaptureResult>(new InvalidOperationException("Provider is unavailable.")),
            () => Task.FromResult(Word()),
            () => true);
        Assert.Equal("feign", result.Text);
    }

    [Theory]
    [InlineData(CaptureStatus.Success)]
    [InlineData(CaptureStatus.TooLong)]
    public async Task FinalCopyResultDoesNotWaitForAnUnresponsiveAutomationWorker(CaptureStatus status)
    {
        var automation = Completion<CaptureResult>();
        var copied = new CaptureResult(status, status == CaptureStatus.Success ? "feign" : null);
        var capture = Coordinator().CaptureAsync(() => automation.Task, () => Task.FromResult(copied), () => true);
        var result = await capture.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(copied, result);
        Assert.False(automation.Task.IsCompleted);
        automation.SetResult(Nothing);
    }

    [Fact]
    public async Task OversizedAutomationSelectionDoesNotTriggerCopyOrRetry()
    {
        var tooLong = new CaptureResult(CaptureStatus.TooLong);
        var result = await Coordinator().CaptureAsync(
            () => Task.FromResult(tooLong),
            () => throw new InvalidOperationException("Copy must not run."),
            () => true);
        Assert.Same(tooLong, result);
    }

    [Fact]
    public async Task LaterLookupUsesAutomationThatFinishesDuringCopy()
    {
        var coordinator = Coordinator();
        await coordinator.CaptureAsync(() => Task.FromResult(Word()), () => throw new Exception(), () => true);
        var automation = Completion<CaptureResult>();
        var result = await coordinator.CaptureAsync(
            () => automation.Task,
            () => { automation.SetResult(Word("running")); return Task.FromResult(Nothing); },
            () => true);
        Assert.Equal("running", result.Text);
    }

    [Fact]
    public async Task LaterMissingSelectionsDoNotRepeatTheInitializationGracePeriod()
    {
        var coordinator = Coordinator();
        await coordinator.CaptureAsync(() => Task.FromResult(Word()), () => throw new Exception(), () => true);
        var calls = 0;
        var result = await coordinator.CaptureAsync(
            () => Task.FromResult(++calls == 1 ? Nothing : Word()),
            () => Task.FromResult(Nothing),
            () => true);
        Assert.Equal(CaptureStatus.NoSelection, result.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ForegroundChangeDuringLateCompletionRejectsTheCapturedWord()
    {
        var automation = Completion<CaptureResult>();
        var copyStarted = Completion<bool>();
        var foreground = true;
        var capture = Coordinator().CaptureAsync(
            () => automation.Task,
            () => { copyStarted.SetResult(true); return Task.FromResult(Nothing); },
            () => foreground);
        await copyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreground = false;
        automation.SetResult(Word());
        var result = await capture.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CaptureStatus.Unavailable, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task ForegroundChangePreventsCopyAndInitializationRetries()
    {
        var foreground = true;
        var result = await Coordinator().CaptureAsync(
            () => { foreground = false; return Task.FromResult(Nothing); },
            () => throw new InvalidOperationException("Copy must not run after a focus change."),
            () => foreground);
        Assert.Equal(CaptureStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task FirstUnresponsiveProviderHasABoundedWait()
    {
        var automation = Completion<CaptureResult>();
        var coordinator = new CaptureCoordinator(0, 1000, 10, 0);
        var result = await coordinator.CaptureAsync(
            () => automation.Task, () => Task.FromResult(Nothing), () => true)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CaptureStatus.NoSelection, result.Status);
        Assert.False(automation.Task.IsCompleted);
        automation.SetResult(Nothing);
    }
}
