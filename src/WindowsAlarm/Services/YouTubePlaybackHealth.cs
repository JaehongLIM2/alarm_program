namespace WindowsAlarm.Services;

public sealed record YouTubeSample(int State, double Position, bool Muted, int Volume, bool AudioPlaying);

public interface IYouTubeAlarmPlayer : IDisposable
{
    event Action<YouTubeSample>? SampleReceived;
    event Action<string>? Failed;
    event Action? StopRequested;
    Task StartAsync(string videoId, CancellationToken cancellationToken);
    // Synchronously mute and dispose this app's player before returning. No default-browser process is used.
    void Stop();
}

public sealed class YouTubePlaybackHealth
{
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(10);
    private double? _previousPosition;
    private TimeSpan? _lastHealthy;
    private double _progress;
    private bool _previousAudible;
    public bool Confirmed { get; private set; }

    public void Observe(YouTubeSample sample, TimeSpan elapsed)
    {
        var audible = sample.State == 1 && !sample.Muted && sample.Volume > 0 && sample.AudioPlaying;
        if (audible && _previousAudible && _previousPosition is { } previous)
        {
            var delta = sample.Position - previous;
            // Require actual progress on consecutive audible PLAYING samples, not just page/player readiness.
            if (delta is > 0.05 and < 5)
            {
                _progress += delta;
                if (_progress >= 1) { Confirmed = true; _lastHealthy = elapsed; }
            }
            else if (delta < 0) _progress = 0; // Loop/seek: wait for progress again, without immediately failing.
        }
        else _progress = 0;
        _previousPosition = sample.Position;
        _previousAudible = audible;
    }

    public string? FailureAt(TimeSpan elapsed)
    {
        if (!Confirmed && elapsed >= StartupTimeout) return "20초 안에 YouTube 재생·오디오 출력을 확인하지 못했습니다.";
        if (Confirmed && _lastHealthy is { } last && elapsed - last >= StallTimeout)
            return "YouTube 재생 진행 또는 오디오 출력을 10초 동안 확인하지 못했습니다.";
        return null;
    }
}
