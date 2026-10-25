namespace PortableDictionary.Services;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\PortableWindowsDictionary.SingleInstance";
    private const string EventName = @"Local\PortableWindowsDictionary.Restore";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _restoreEvent;
    private readonly RegisteredWaitHandle? _registration;
    private readonly Action? _restoreAction;

    private SingleInstance(bool isFirst, Mutex mutex, EventWaitHandle restoreEvent,
        RegisteredWaitHandle? registration, Action? restoreAction)
    {
        IsFirst = isFirst;
        _mutex = mutex;
        _restoreEvent = restoreEvent;
        _registration = registration;
        _restoreAction = restoreAction;
    }

    public bool IsFirst { get; }

    public static SingleInstance Start(Action restoreAction)
    {
        var mutex = new Mutex(true, MutexName, out var isFirst);
        var restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (!isFirst)
        {
            restoreEvent.Set();
            return new SingleInstance(false, mutex, restoreEvent, null, null);
        }

        var registration = ThreadPool.RegisterWaitForSingleObject(
            restoreEvent,
            (_, _) => restoreAction(),
            null,
            Timeout.Infinite,
            false);
        return new SingleInstance(true, mutex, restoreEvent, registration, restoreAction);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _restoreEvent.Dispose();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
        GC.KeepAlive(_restoreAction);
    }
}
