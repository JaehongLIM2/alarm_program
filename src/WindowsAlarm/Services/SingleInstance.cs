using System.Security.Principal;

namespace WindowsAlarm.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _wake;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wakeRegistration;
    private RegisteredWaitHandle? _activateRegistration;
    public bool IsOwner { get; }

    public SingleInstance(string dataRoot)
    {
        var name = $"Local\\WindowsAlarm-{WindowsIdentity.GetCurrent().User?.Value}-{TaskSchedulerWakeService.ProfileKey(dataRoot)}";
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-wake");
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate");
        _mutex = new Mutex(false, name);
        try { IsOwner = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }
    public void Listen(Action<bool> callback)
    {
        if (!IsOwner) throw new InvalidOperationException("Only the owner can listen.");
        _wakeRegistration = ThreadPool.RegisterWaitForSingleObject(_wake, (_, _) => callback(true), null, Timeout.Infinite, false);
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => callback(false), null, Timeout.Infinite, false);
    }
    public void Notify(bool wake) { if (wake) _wake.Set(); else _activate.Set(); }
    public void Dispose()
    {
        _wakeRegistration?.Unregister(null); _activateRegistration?.Unregister(null);
        if (IsOwner) _mutex.ReleaseMutex();
        _mutex.Dispose(); _wake.Dispose(); _activate.Dispose();
    }
}
