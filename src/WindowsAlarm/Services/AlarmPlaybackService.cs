using System.Diagnostics;
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
    private readonly Action<Uri> _launchBrowser;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _pending;
    public bool IsActive { get; private set; }
    public event Action<string>? StatusChanged;

    public AlarmPlaybackService(AppStore store, ILocalAlarmOutput? local = null, Action<Uri>? launchBrowser = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _store = store; _local = local ?? new LocalAlarmPlayer(store);
        _launchBrowser = launchBrowser ?? (uri => { using var browser = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); });
        _delay = delay ?? Task.Delay;
        _local.StatusChanged += message => StatusChanged?.Invoke(message);
    }

    public async Task StartAsync(AlarmSettings settings)
    {
        Stop(); IsActive = true;
        var source = new CancellationTokenSource(); _pending = source;
        var token = source.Token;
        try
        {
            if (AlarmSettings.TryGetYouTubeUri(settings.YouTubeUrl, out var youtube))
            {
                var builder = new UriBuilder(youtube!);
                builder.Query = string.IsNullOrEmpty(builder.Query) ? "autoplay=1" : builder.Query.TrimStart('?') + "&autoplay=1";
                // Shell launch is NOT proof of playback. Never wait for it before starting the fallback deadline.
                _ = Task.Run(() =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        _launchBrowser(builder.Uri);
                        _store.Log("YouTube launched; actual playback cannot be verified");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _store.Log("YouTube launch failed; local fallback remains scheduled", ex); }
                });
                StatusChanged?.Invoke("YouTube 실행 시도 중 · 5초 후 로컬 백업도 재생합니다.");
                await _delay(TimeSpan.FromSeconds(5), token);
            }
            else if (!string.IsNullOrWhiteSpace(settings.YouTubeUrl)) _store.Log("Invalid recovered YouTube URL ignored");
            if (!token.IsCancellationRequested) _local.Start(settings.LocalMusicPath);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Stop()
    {
        IsActive = false;
        _pending?.Cancel(); _pending?.Dispose(); _pending = null;
        _local.Stop();
        // Do not terminate a browser process: it may belong to an existing browsing session.
    }
    public void Dispose() { Stop(); _local.Dispose(); }
}
