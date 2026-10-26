using WindowsAlarm.Models;

namespace WindowsAlarm.Services;

public interface IAlarmPlayback : IDisposable
{
    bool IsActive { get; }
    Task StartAsync(AlarmSettings settings);
    void Stop();
}

public sealed class AlarmPlaybackService : IAlarmPlayback
{
    private readonly AppStore _store;
    private readonly ILocalAlarmOutput _local;
    private readonly Func<IYouTubeAlarmPlayer> _createYouTube;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeProvider _clock;
    private Session? _current;
    public bool IsActive => _current is not null;
    public event Action<string>? StatusChanged;
    public event Action? StopRequested;

    private sealed class Session(AlarmSettings settings, long started)
    {
        public AlarmSettings Settings { get; } = settings;
        public long Started { get; } = started;
        public CancellationTokenSource Cancellation { get; } = new();
        public YouTubePlaybackHealth Health { get; } = new();
        public IYouTubeAlarmPlayer? YouTube;
        public bool LocalStarted;
    }

    public AlarmPlaybackService(AppStore store, ILocalAlarmOutput? local = null, Func<IYouTubeAlarmPlayer>? createYouTube = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? clock = null)
    {
        _store = store; _local = local ?? new LocalAlarmPlayer(store);
        _createYouTube = createYouTube ?? (() => new WebViewYouTubePlayer(store));
        _delay = delay ?? Task.Delay; _clock = clock ?? TimeProvider.System;
        _local.StatusChanged += message => StatusChanged?.Invoke(message);
    }

    public async Task StartAsync(AlarmSettings settings)
    {
        Stop();
        var run = new Session(settings, _clock.GetTimestamp()); _current = run;
        if (!AlarmSettings.TryGetYouTubeVideoId(settings.YouTubeUrl, out var videoId))
        {
            SwitchToLocal(run, string.IsNullOrWhiteSpace(settings.YouTubeUrl) ? "YouTube 미지정" : "저장된 YouTube URL이 유효하지 않음"); return;
        }
        var token = run.Cancellation.Token;
        try
        {
            var player = _createYouTube(); run.YouTube = player;
            player.SampleReceived += sample =>
            {
                if (!IsYouTubeCurrent(run)) return;
                var wasConfirmed = run.Health.Confirmed;
                run.Health.Observe(sample, _clock.GetElapsedTime(run.Started));
                if (!wasConfirmed && run.Health.Confirmed) Report("YouTube 재생·오디오 출력 확인 · 로컬 음악은 대기합니다.");
            };
            player.Failed += reason => SwitchToLocal(run, reason);
            player.StopRequested += () => { if (IsYouTubeCurrent(run)) { Stop(); StopRequested?.Invoke(); } };
            Report("YouTube 재생 확인 중 · 최대 20초, 실패 시 로컬 음악으로 전환합니다.");
            _ = MonitorAsync(run, token);
            await player.StartAsync(videoId, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { _store.Log("YouTube initialization failed", ex); SwitchToLocal(run, "YouTube 플레이어를 시작할 수 없습니다."); }
    }

    private async Task MonitorAsync(Session run, CancellationToken token)
    {
        try
        {
            while (IsYouTubeCurrent(run))
            {
                await _delay(TimeSpan.FromMilliseconds(500), token);
                if (!IsYouTubeCurrent(run)) return;
                if (run.Health.FailureAt(_clock.GetElapsedTime(run.Started)) is { } reason) { SwitchToLocal(run, reason); return; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { _store.Log("YouTube monitor failed", ex); SwitchToLocal(run, "YouTube 재생 감시 오류"); }
    }

    private bool IsYouTubeCurrent(Session run) => _current == run && !run.LocalStarted;
    private void SwitchToLocal(Session run, string reason)
    {
        if (!IsYouTubeCurrent(run)) return;
        // Commit the source switch before cancelling/closing: late, duplicate and reentrant events are ignored.
        run.LocalStarted = true; run.Cancellation.Cancel();
        CloseYouTube(run);
        Report(reason + " · YouTube를 종료하고 로컬 백업을 시작합니다.");
        try { _local.Start(run.Settings.LocalMusicPath); }
        catch (Exception ex) { _store.Log("Local fallback could not start", ex); Report("로컬 알람 출력 오류. 출력 장치와 로그를 확인하세요."); }
    }
    private void CloseYouTube(Session run)
    {
        var player = run.YouTube; run.YouTube = null;
        try { player?.Stop(); } catch (Exception ex) { _store.Log("YouTube stop failed", ex); }
        try { player?.Dispose(); } catch (Exception ex) { _store.Log("YouTube dispose failed", ex); }
    }
    public void Stop()
    {
        var run = _current; _current = null;
        if (run is not null) { run.Cancellation.Cancel(); CloseYouTube(run); run.Cancellation.Dispose(); }
        _local.Stop();
    }
    private void Report(string message) { _store.Log(message); StatusChanged?.Invoke(message); }
    public void Dispose() { Stop(); _local.Dispose(); }
}
