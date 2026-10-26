using WindowsAlarm.Models;
using WindowsAlarm.Services;

namespace WindowsAlarm.Tests;

internal sealed class FakePower : IPowerService
{
    public int HibernateCalls, ShutdownCalls;
    public bool HibernateAllowed = true, FailShutdown, Awake;
    public Action? BeforeHibernate;
    public Action? OnWakeDisplay;
    public string GetDiagnostics() => "fake";
    public void EnsureHibernateAvailable() { if (!HibernateAllowed) throw new InvalidOperationException("disabled"); }
    public void Hibernate() { BeforeHibernate?.Invoke(); HibernateCalls++; }
    public void Shutdown() { ShutdownCalls++; if (FailShutdown) throw new InvalidOperationException("permission denied"); }
    public void KeepAwake(bool enabled) => Awake = enabled;
    public void WakeDisplay() => OnWakeDisplay?.Invoke();
}
internal sealed class FakeScheduler : IWakeScheduler
{
    public bool FailRegistration, FailRemoval;
    public AlarmSchedule? Registered;
    public void Register(AlarmSchedule schedule) { if (FailRegistration) throw new InvalidOperationException("denied"); Registered = schedule; }
    public void Remove() { if (FailRemoval) throw new InvalidOperationException("delete denied"); Registered = null; }
    public bool Exists() => Registered is not null;
}
internal sealed class FakePlayback : IAlarmPlayback
{
    public int Starts;
    public bool IsActive { get; private set; }
    public AlarmSettings? LastSettings;
    public Action? OnStart;
    public Task StartAsync(AlarmSettings settings) { Starts++; LastSettings = settings; IsActive = true; OnStart?.Invoke(); return Task.CompletedTask; }
    public void Stop() => IsActive = false;
    public void Dispose() => Stop();
}
internal sealed class FakeLocalOutput : ILocalAlarmOutput
{
    public int Starts;
    public bool Active;
    public Action? OnStart;
    public event Action<string>? StatusChanged;
    public void Start(string? path) { OnStart?.Invoke(); Starts++; Active = true; StatusChanged?.Invoke("local started"); }
    public void Stop() => Active = false;
    public void Dispose() { }
}
internal sealed class FakeYouTube : IYouTubeAlarmPlayer
{
    public int Stops;
    public Action? OnStop;
    public Task Initialization = Task.CompletedTask;
    public bool FailStart;
    public event Action<YouTubeSample>? SampleReceived;
    public event Action<string>? Failed;
    public event Action? StopRequested;
    public Task StartAsync(string videoId, CancellationToken cancellationToken) => FailStart ? Task.FromException(new InvalidOperationException("Runtime unavailable")) : Initialization;
    public void Emit(double position, bool audible = true, bool muted = false, int state = 1, int volume = 100) => SampleReceived?.Invoke(new(state, position, muted, volume, audible));
    public void Error() => Failed?.Invoke("offline/autoplay blocked");
    public void RequestStop() => StopRequested?.Invoke();
    public void Stop() { Stops++; OnStop?.Invoke(); }
    public void Dispose() { }
}
internal sealed class ManualPlaybackClock : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public void Advance(double seconds) => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(seconds).Ticks);
}
internal sealed class PlaybackHarness : IDisposable
{
    private TaskCompletionSource _delay = NewDelay();
    public readonly ManualPlaybackClock Clock = new();
    public readonly FakeYouTube YouTube = new();
    public readonly FakeLocalOutput Local = new();
    public readonly List<string> Events = [];
    public readonly AlarmPlaybackService Playback;
    public int Waits;
    public PlaybackHarness(string root)
    {
        YouTube.OnStop = () => Events.Add("youtube-stop"); Local.OnStart = () => Events.Add("local-start");
        Playback = new AlarmPlaybackService(new AppStore(System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"))), Local, () => YouTube,
            (_, token) => { Interlocked.Increment(ref Waits); return _delay.Task.WaitAsync(token); }, Clock);
    }
    private static TaskCompletionSource NewDelay() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Tick(double seconds)
    {
        Clock.Advance(seconds); var waits = Volatile.Read(ref Waits); var delay = _delay; _delay = NewDelay(); delay.TrySetResult();
        if (!SpinWait.SpinUntil(() => Volatile.Read(ref Waits) > waits || Local.Active || !Playback.IsActive, TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("Playback watchdog did not advance.");
    }
    public void Start() => Playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" }).GetAwaiter().GetResult();
    public void Dispose() => Playback.Dispose();
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
