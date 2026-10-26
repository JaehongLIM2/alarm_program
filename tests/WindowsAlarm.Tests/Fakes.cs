using WindowsAlarm.Models;
using WindowsAlarm.Services;

namespace WindowsAlarm.Tests;

internal sealed class FakePower : IPowerService
{
    public int HibernateCalls, ShutdownCalls;
    public bool HibernateAllowed = true, FailShutdown, Awake;
    public Action? BeforeHibernate;
    public string GetDiagnostics() => "fake";
    public void EnsureHibernateAvailable() { if (!HibernateAllowed) throw new InvalidOperationException("disabled"); }
    public void Hibernate() { BeforeHibernate?.Invoke(); HibernateCalls++; }
    public void Shutdown() { ShutdownCalls++; if (FailShutdown) throw new InvalidOperationException("permission denied"); }
    public void KeepAwake(bool enabled) => Awake = enabled;
}
internal sealed class FakeScheduler : IWakeScheduler
{
    public bool FailRegistration;
    public AlarmSchedule? Registered;
    public void Register(AlarmSchedule schedule) { if (FailRegistration) throw new InvalidOperationException("denied"); Registered = schedule; }
    public void Remove() => Registered = null;
    public bool Exists() => Registered is not null;
}
internal sealed class FakePlayback : IAlarmPlayback
{
    public int Starts;
    public bool IsActive { get; private set; }
    public AlarmSettings? LastSettings;
    public Task StartAsync(AlarmSettings settings) { Starts++; LastSettings = settings; IsActive = true; return Task.CompletedTask; }
    public void Stop() => IsActive = false;
    public void Dispose() => Stop();
}
internal sealed class FakeLocalOutput : ILocalAlarmOutput
{
    public int Starts;
    public event Action<string>? StatusChanged;
    public void Start(string? path) { Starts++; StatusChanged?.Invoke("local started"); }
    public void Stop() { }
    public void Dispose() { }
}
internal sealed class FakeActivity : IActivityMonitor
{
    public InputSnapshot Value = new(true, true, 123, TimeSpan.FromHours(1));
    public bool Throw;
    public int Reads;
    public Func<int, InputSnapshot>? Script;
    public InputSnapshot Read() { Reads++; if (Throw) throw new InvalidOperationException("API failure"); return Script?.Invoke(Reads) ?? Value; }
}
internal sealed class Harness : IDisposable
{
    public AppStore Store { get; }
    public FakePower Power { get; } = new();
    public FakeScheduler Scheduler { get; } = new();
    public FakePlayback Playback { get; } = new();
    public FakeActivity Activity { get; } = new();
    public AlarmCoordinator Coordinator { get; }
    public Harness(string root)
    {
        Store = new AppStore(System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")));
        Coordinator = new AlarmCoordinator(Store, Scheduler, Power, Playback, Activity);
    }
    public AlarmSchedule Seed(DateTimeOffset due, AlarmPhase phase = AlarmPhase.Armed, bool autoShutdown = true)
    {
        var schedule = new AlarmSchedule { DueAt = due, Phase = phase, Settings = new AlarmSettings { AutoShutdownEnabled = autoShutdown, ShutdownAfterMinutes = 1 } };
        Store.Save("schedule.json", schedule); return schedule;
    }
    public void Dispose() => Coordinator.Dispose();
}
