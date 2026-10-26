using WindowsAlarm.Models;

namespace WindowsAlarm.Services;

// Dispatcher-thread orchestration: button, timer, resume and scheduler notifications cannot race.
public sealed class AlarmCoordinator : IDisposable
{
    private readonly AppStore _store;
    private readonly IWakeScheduler _scheduler;
    private readonly IPowerService _power;
    private readonly IAlarmPlayback _playback;
    private bool _testAlarm;
    private bool _keepAwake;
    public ShutdownController Shutdown { get; }
    public AlarmSchedule? Schedule { get; private set; }
    public string Status { get; private set; } = "예약 없음";
    public bool IsRinging => _testAlarm || Schedule?.Phase == AlarmPhase.Ringing;
    public event Action? Changed;
    public event Action? AlarmStarted;

    public AlarmCoordinator(AppStore store, IWakeScheduler scheduler, IPowerService power, IAlarmPlayback playback, IActivityMonitor activity)
    {
        _store = store; _scheduler = scheduler; _power = power; _playback = playback;
        Shutdown = new ShutdownController(power, activity, message => _store.Log(message));
        Shutdown.Changed += OnShutdownChanged;
    }

    public void Recover(DateTimeOffset? currentTime = null)
    {
        var now = currentTime ?? DateTimeOffset.Now;
        Schedule = _store.Load<AlarmSchedule>("schedule.json");
        if (Schedule is { } loaded && (loaded.Id == Guid.Empty || !Enum.IsDefined(loaded.Phase) || loaded.DueAt == default))
        {
            _store.Log("Invalid schedule state rejected"); Schedule = null;
        }
        if (Schedule is { Settings: null } damaged)
        {
            var fallback = _store.Load<AlarmSettings>("settings.json") ?? new AlarmSettings();
            Schedule = damaged with { Settings = fallback with { AutoShutdownEnabled = false }, ShutdownCancelled = true, ShutdownAt = null };
            _store.Log("Missing settings snapshot recovered; emergency/local alarm available, automatic shutdown disabled");
            TryPersist();
        }
        if (Schedule is not { } schedule) { TryRemoveTask(); Report(_store.LoadWarning ?? "예약 없음"); return; }
        switch (schedule.Phase)
        {
            case AlarmPhase.Armed:
                if (now >= schedule.DueAt) { Tick(now); break; }
                try
                {
                    _scheduler.Register(schedule); // Also refresh the action path after an app move.
                    Report($"다음 알람 {schedule.DueAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex) { Report("Wake 예약 복구 실패. 앱을 열어 두고 로그를 확인하세요.", ex); }
                break;
            case AlarmPhase.Ringing:
                if (now - schedule.DueAt > AlarmTiming.RecoveryGrace || now < schedule.DueAt.AddMinutes(-1)) { Expire(schedule); break; }
                // Crash/reboot may resume audio, but NEVER restore an unattended shutdown.
                Schedule = schedule with { ShutdownAt = null, ShutdownCancelled = true };
                TryPersist(); StartPlayback(Schedule.Settings); UpdateKeepAwake();
                Report("알람 상태를 복구했습니다. 안전을 위해 자동 종료는 취소했습니다.");
                break;
            case AlarmPhase.Stopped:
                Schedule = schedule with { Phase = AlarmPhase.Completed, ShutdownCancelled = true, ShutdownAt = null };
                TryPersist(); TryRemoveTask(); Report("정지 상태 복구 · 자동 종료 취소됨"); break;
            default:
                TryRemoveTask(); Report("완료되거나 취소된 알람입니다. 새 알람을 설정하세요."); break;
        }
        Changed?.Invoke();
    }

    public void Arm(AlarmSettings settings, DateTimeOffset due, bool hibernate, bool test)
    {
        if (IsRinging || Shutdown.IsPending) throw new InvalidOperationException("진행 중인 알람과 자동 종료를 먼저 정지/취소하세요.");
        if (settings.Validate() is { } error) throw new InvalidOperationException(error);
        if (due <= DateTimeOffset.Now.AddSeconds(5)) throw new InvalidOperationException("예약 시간은 현재보다 최소 5초 뒤여야 합니다.");
        if (hibernate) _power.EnsureHibernateAvailable();
        var schedule = new AlarmSchedule { DueAt = due, Settings = settings, IsTest = test, ShutdownCancelled = test };
        _store.Save("settings.json", settings);
        _store.Save("schedule.json", schedule);
        Schedule = schedule;
        try { _scheduler.Register(schedule); }
        catch (Exception ex)
        {
            Schedule = schedule with { Phase = AlarmPhase.Cancelled, ShutdownCancelled = true }; TryPersist(); TryRemoveTask();
            throw new InvalidOperationException("Windows Wake Timer 등록 실패. 최대 절전은 실행하지 않았습니다. 작업 스케줄러 서비스/사용자 권한을 확인하세요.", ex);
        }
        Report($"다음 알람 {due.LocalDateTime:yyyy-MM-dd HH:mm:ss}{(test ? " · 테스트: 자동 종료 없음" : "")}");
        _store.Log($"Wake timer registered: {schedule.Id}, due={due:O}, S4={hibernate}");
        if (hibernate)
        {
            if (DateTimeOffset.Now >= due.AddSeconds(-5)) { Report("예약 시간이 임박하여 S4 진입을 생략했습니다. 알람 예약은 유지됩니다."); return; }
            try { _power.Hibernate(); _store.Log("SetSuspendState returned (resume or transition accepted)"); }
            catch (Exception ex) { Report("S4 진입 실패. 알람 예약은 유지됩니다.", ex); throw; }
        }
    }

    public void Tick(DateTimeOffset now)
    {
        if (Schedule is { Phase: AlarmPhase.Armed } schedule && now >= schedule.DueAt)
        {
            if (now - schedule.DueAt > AlarmTiming.RecoveryGrace) { Expire(schedule); return; }
            if (_testAlarm) { _testAlarm = false; _playback.Stop(); }
            var shutdownAt = !schedule.IsTest && !schedule.ShutdownCancelled && schedule.Settings.AutoShutdownEnabled
                ? now.AddMinutes(Math.Clamp(schedule.Settings.ShutdownAfterMinutes, 1, 1440)) : (DateTimeOffset?)null;
            Schedule = schedule with { Phase = AlarmPhase.Ringing, StartedAt = now, ShutdownAt = shutdownAt };
            TryPersist();
            _store.Log("Wake timer triggered");
            // Establish the input baseline and cancellation state before media initialization can
            // pump UI messages. Otherwise startup input/cancellation could be forgotten or reenabled.
            if (shutdownAt is { } due) Shutdown.Begin(due, now);
            if (Schedule?.Id == schedule.Id && Schedule.Phase == AlarmPhase.Ringing)
            {
                StartPlayback(Schedule.Settings);
                _store.Log("Alarm playback initialization requested");
            }
            UpdateKeepAwake();
            if (IsRinging) Report("알람 시작");
        }
        Shutdown.Tick(now);
        Changed?.Invoke();
    }

    public void TestAlarm(AlarmSettings settings)
    {
        if (IsRinging || Shutdown.IsPending) throw new InvalidOperationException("진행 중인 알람과 자동 종료를 먼저 정지/취소하세요.");
        if (settings.Validate() is { } error) throw new InvalidOperationException(error);
        _testAlarm = true; StartPlayback(settings); UpdateKeepAwake(); Report("테스트 알람 · 자동 종료는 실행하지 않습니다.");
    }

    private void StartPlayback(AlarmSettings settings)
    {
        UpdateKeepAwake();
        try { _power.WakeDisplay(); _store.Log("Alarm display wake request completed; lock state unchanged"); }
        catch (Exception ex) { _store.Log("Alarm display wake request failed; continuing audio", ex); }
        _ = StartPlaybackSafeAsync(settings);
        AlarmStarted?.Invoke();
    }
    private async Task StartPlaybackSafeAsync(AlarmSettings settings)
    {
        try { await _playback.StartAsync(settings); }
        catch (Exception ex) { Report("알람 재생 오류. 출력 장치와 로그를 확인하세요.", ex); }
    }

    public void StopAlarm()
    {
        _playback.Stop();
        if (_testAlarm) _testAlarm = false;
        else if (Schedule?.Phase == AlarmPhase.Ringing)
        {
            Schedule = Schedule with { Phase = Shutdown.IsPending ? AlarmPhase.Stopped : AlarmPhase.Completed };
            TryPersist(); if (!Shutdown.IsPending) TryRemoveTask();
        }
        UpdateKeepAwake(); Report(Shutdown.IsPending ? "알람 정지 · 자동 종료 예약은 유지됩니다." : "알람을 정지했습니다.");
    }

    public void CancelWake()
    {
        var persisted = true;
        if (Schedule?.Phase is AlarmPhase.Armed or AlarmPhase.Cancelled)
        {
            Schedule = Schedule with { Phase = AlarmPhase.Cancelled, ShutdownCancelled = true };
            persisted = TryPersist(); // A second click must retry a previously failed durable cancellation.
        }
        var removed = TryRemoveTask();
        if (!persisted || !removed)
            Report($"Wake Timer 취소를 완료하지 못했습니다. {(!persisted ? "취소 상태를 파일에 저장하지 못했습니다. " : "")}{(!removed ? "Windows 예약 작업을 삭제하지 못했습니다. " : "")}로그와 저장 경로를 확인하고 앱을 닫기 전에 취소를 다시 누르세요.");
        else Report("Wake Timer를 취소했습니다.");
    }

    public void CancelShutdown(string reason = "사용자가 취소")
    {
        Shutdown.Cancel(reason);
        var persisted = true;
        if (Schedule is { } schedule)
        {
            Schedule = schedule with { ShutdownCancelled = true, ShutdownAt = null }; persisted = TryPersist();
        }
        Report(persisted ? "예약된 자동 종료를 취소했습니다." : "현재 실행의 자동 종료는 취소했지만 취소 상태를 저장하지 못했습니다. 앱을 닫기 전에 저장 경로를 확인하고 자동 종료 취소를 다시 누르세요.");
    }

    public void OnResumeOrSessionChange(bool powerResume = false)
    {
        // Expected S4 wake notifications may arrive just after the first due tick. Do not cancel that new alarm's shutdown.
        var justStarted = Schedule?.StartedAt is { } start && DateTimeOffset.Now - start < TimeSpan.FromSeconds(30);
        if (Shutdown.IsPending && (!powerResume || !justStarted)) CancelShutdown("절전 복귀 또는 사용자 세션 변경");
        _store.Log("Power resume/session change received"); Tick(DateTimeOffset.Now);
    }

    private void OnShutdownChanged()
    {
        if (Schedule is { } schedule)
        {
            Schedule = schedule with { ShutdownAt = Shutdown.DueAt, ShutdownCancelled = !Shutdown.IsPending,
                Phase = !Shutdown.IsPending && schedule.Phase == AlarmPhase.Stopped ? AlarmPhase.Completed : schedule.Phase };
            TryPersist(); if (Schedule.Phase == AlarmPhase.Completed) TryRemoveTask();
        }
        UpdateKeepAwake(); Changed?.Invoke();
    }

    private void Expire(AlarmSchedule schedule)
    {
        Schedule = schedule with { Phase = AlarmPhase.Missed, ShutdownCancelled = true, ShutdownAt = null };
        TryPersist(); TryRemoveTask(); Report("2시간 이상 지났거나 시각이 유효하지 않은 예약은 만료했습니다. 알람을 다시 설정하세요.");
    }
    private bool TryPersist()
    {
        if (Schedule is null) return true;
        try { _store.Save("schedule.json", Schedule); return true; }
        catch (Exception ex) { _store.Log("State save failed; in-memory state retained, persisted state may be stale", ex); return false; }
    }
    private bool TryRemoveTask()
    {
        try { _scheduler.Remove(); return true; }
        catch (Exception ex) { _store.Log("Windows task deletion failed; cancellation may need retry", ex); return false; }
    }
    private void UpdateKeepAwake()
    {
        var required = IsRinging || Shutdown.IsPending;
        if (required == _keepAwake) return;
        try { _power.KeepAwake(required); _keepAwake = required; }
        catch (Exception ex) { _store.Log("Keep-awake request failed", ex); }
    }
    public void Report(string message, Exception? error = null) { Status = message; _store.Log(message, error); Changed?.Invoke(); }

    public void Dispose()
    {
        // Closing the window must not disable shutdown for a future Armed alarm.
        if (Schedule?.Phase != AlarmPhase.Armed) CancelShutdown("앱 종료");
        StopAlarm(); _playback.Dispose();
    }
}
