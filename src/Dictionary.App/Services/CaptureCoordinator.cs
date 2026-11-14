using System.Diagnostics;

namespace PortableDictionary.Services;

// Coordinates the real capture workers without depending on Windows. In
// particular, a slow first UI Automation result must survive the Copy fallback.
internal sealed class CaptureCoordinator(
    int automationTimeoutMilliseconds = 1500,
    int copyTimeoutMilliseconds = 6000,
    int initializationTimeoutMilliseconds = 8000,
    int retryDelayMilliseconds = 150)
{
    private int _firstRequest = 1;

    internal async Task<CaptureResult> CaptureAsync(
        Func<Task<CaptureResult>> startAutomation,
        Func<Task<CaptureResult>> startCopy,
        Func<bool> sourceStillForeground)
    {
        var initializing = Interlocked.Exchange(ref _firstRequest, 0) == 1;
        var started = Stopwatch.GetTimestamp();
        if (!sourceStillForeground())
        {
            return SourceChanged();
        }

        var automation = startAutomation();
        var result = await ReadWithin(automation, automationTimeoutMilliseconds).ConfigureAwait(false);
        if (!sourceStillForeground())
        {
            return SourceChanged();
        }
        if (IsFinal(result))
        {
            return result!;
        }

        // Copy runs only once. Retrying it could repeat keyboard commands or
        // interfere with a newer clipboard change while a provider initializes.
        var copied = await ReadWithin(startCopy(), copyTimeoutMilliseconds).ConfigureAwait(false)
            ?? new CaptureResult(CaptureStatus.Unavailable, Detail: "Clipboard fallback capture timed out.");
        if (!sourceStillForeground())
        {
            return SourceChanged();
        }
        if (IsFinal(copied))
        {
            return copied;
        }

        // Even on a later lookup, use a result that finished during Copy rather
        // than discarding it merely because it missed the initial head start.
        if (automation.IsCompleted)
        {
            result = await ReadWithin(automation, 0).ConfigureAwait(false);
            if (IsFinal(result))
            {
                return sourceStillForeground() ? result! : SourceChanged();
            }
        }

        while (initializing)
        {
            var remaining = initializationTimeoutMilliseconds - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (remaining <= 0 || !sourceStillForeground())
            {
                break;
            }

            // Await the original request first: a timeout never cancels its
            // worker. Never start a second UIA worker while that one is pending.
            result = await ReadWithin(automation, remaining).ConfigureAwait(false);
            if (!sourceStillForeground())
            {
                return SourceChanged();
            }
            if (IsFinal(result))
            {
                return result!;
            }
            if (!automation.IsCompleted)
            {
                break;
            }

            remaining = initializationTimeoutMilliseconds - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (remaining <= 0)
            {
                break;
            }
            await Task.Delay(Math.Min(retryDelayMilliseconds, remaining)).ConfigureAwait(false);
            if (!sourceStillForeground())
            {
                return SourceChanged();
            }
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= initializationTimeoutMilliseconds)
            {
                break;
            }

            // A provider can return an empty tree before enabling accessibility.
            // Retry the read only, keeping the original application in front.
            automation = startAutomation();
        }

        return sourceStillForeground() ? copied : SourceChanged();
    }

    private static bool IsFinal(CaptureResult? result) =>
        result?.Status is CaptureStatus.Success or CaptureStatus.TooLong;

    private static CaptureResult SourceChanged() => new(CaptureStatus.Unavailable,
        Detail: "The originating window changed during capture.");

    private static async Task<CaptureResult?> ReadWithin(Task<CaptureResult> capture, int timeoutMilliseconds)
    {
        try
        {
            return await capture.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new CaptureResult(CaptureStatus.Unavailable, Detail: ex.Message);
        }
    }
}
