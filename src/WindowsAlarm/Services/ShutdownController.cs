namespace WindowsAlarm.Services;

public enum ShutdownMode { None, Scheduled, Countdown, ActivityConfirmation, Cancelled, Requested }

public sealed record InputSnapshot(bool Reliable, bool SafeSession, uint LastInputTick, TimeSpan IdleTime);
public interface IActivityMonitor { InputSnapshot Read(); }

// No Windows shutdown is queued during the countdown: cancellation just clears this in-process state.
public sealed class ShutdownController
{
    private readonly IPowerService _power;
    private readonly IActivityMonitor _activity;
    private readonly Action<string> _log;
    private uint? _baselineInput;
    private bool _activitySeen;
    private DateTimeOffset? _lastTick;
    public ShutdownMode Mode { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset? Deadline { get; private set; }
    public event Action? Changed;
    public bool IsPending => Mode is ShutdownMode.Scheduled or ShutdownMode.Countdown or ShutdownMode.ActivityConfirmation;

    public ShutdownController(IPowerService power, IActivityMonitor activity, Action<string> log)
    {
        _power = power; _activity = activity; _log = log;
    }

    public void Begin(DateTimeOffset due, DateTimeOffset now)
    {
        DueAt = due; Deadline = null; _lastTick = now; Mode = ShutdownMode.Scheduled;
        var sample = ReadSafely();
        _baselineInput = sample.Reliable ? sample.LastInputTick : null;
        _activitySeen = !sample.Reliable || !sample.SafeSession;
        _log($"Automatic shutdown scheduled: {due:O}"); Changed?.Invoke();
    }

    public void Tick(DateTimeOffset now)
    {
        if (!IsPending) return;
        if (_lastTick is { } last && (now < last.AddSeconds(-2) || now - last > TimeSpan.FromSeconds(15)))
        {
            Cancel("절전 복귀·시계 변경 또는 긴 처리 지연 감지"); return;
        }
        _lastTick = now;
        var sample = ReadSafely();
        Observe(sample);
        if (Mode == ShutdownMode.Scheduled && now >= DueAt)
        {
            Mode = NeedsConfirmation(sample) ? ShutdownMode.ActivityConfirmation : ShutdownMode.Countdown;
            Deadline = now.AddSeconds(60);
            _log(Mode == ShutdownMode.Countdown ? "Shutdown countdown started" : "User activity detected; explicit shutdown confirmation required");
            Changed?.Invoke();
        }
        else if (Mode == ShutdownMode.Countdown && NeedsConfirmation(sample))
        {
            Mode = ShutdownMode.ActivityConfirmation; Deadline = now.AddSeconds(60);
            _log("User activity detected during countdown; default changed to cancel"); Changed?.Invoke();
        }
        if (Deadline is not { } deadline || now < deadline) return;
        if (Mode == ShutdownMode.ActivityConfirmation) { Cancel("사용 중 확인에 응답이 없어 안전하게 취소"); return; }
        if (Mode == ShutdownMode.Countdown)
        {
            // Fresh check immediately before the automatic request, including session/API failure.
            var final = ReadSafely(); Observe(final);
            if (NeedsConfirmation(final))
            {
                Mode = ShutdownMode.ActivityConfirmation; Deadline = now.AddSeconds(60); Changed?.Invoke(); return;
            }
            RequestNow();
        }
    }

    private InputSnapshot ReadSafely()
    {
        try { return _activity.Read(); }
        catch (Exception ex) { _log("Activity detection failed: " + ex.Message); return new InputSnapshot(false, false, 0, TimeSpan.Zero); }
    }
    private void Observe(InputSnapshot sample)
    {
        if (!sample.Reliable || !sample.SafeSession || _baselineInput is null || sample.LastInputTick != _baselineInput)
        {
            if (!_activitySeen) _log("User activity detected since alarm start");
            _activitySeen = true;
        }
    }
    private bool NeedsConfirmation(InputSnapshot sample) => _activitySeen || !sample.Reliable || !sample.SafeSession || sample.IdleTime < TimeSpan.FromMinutes(5);

    public void Cancel(string reason = "사용자가 취소")
    {
        if (!IsPending) return;
        Mode = ShutdownMode.Cancelled; DueAt = null; Deadline = null;
        _log("Automatic shutdown cancelled: " + reason); Changed?.Invoke();
    }
    public void RequestNow()
    {
        if (!IsPending) return;
        // Mark consumed before the API: a crash/restart can never repeat an already-requested shutdown.
        Mode = ShutdownMode.Requested; DueAt = null; Deadline = null; Changed?.Invoke();
        try { _power.Shutdown(); _log("Normal Windows shutdown requested (Windows/apps may still cancel)"); }
        catch (Exception ex)
        {
            Mode = ShutdownMode.Cancelled; _log("Shutdown request failed: " + ex); Changed?.Invoke();
        }
    }
}
