using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace WindowsAlarm.Services;

public interface ILocalAlarmOutput : IDisposable
{
    event Action<string>? StatusChanged;
    void Start(string? path);
    void Stop();
}

public sealed class LocalAlarmPlayer : ILocalAlarmOutput
{
    private readonly AppStore _store;
    private readonly DispatcherTimer _watchdog;
    private readonly Stopwatch _progress = new();
    private readonly Stopwatch _emergencyRetry = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private MediaPlayer? _media;
    private MediaSource? _source;
    private SoundPlayer? _emergency;
    private MemoryStream? _wave;
    private TimeSpan _lastPosition;
    private bool _beepOnly;
    private string? _retryPath;
    public bool IsActive { get; private set; }
    internal bool SuppressSoundForTesting { get; init; }
    internal MediaPlayer? NativePlayerForTesting => _media;
    public event Action<string>? StatusChanged;

    public LocalAlarmPlayer(AppStore store)
    {
        _store = store;
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchdog.Tick += (_, _) => CheckPlayback();
    }

    public void Start(string? path)
    {
        Stop(); IsActive = true; _retryPath = path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { StartEmergency("MP3 미지정 또는 파일 없음"); return; }
        try
        {
            OpenAlertMedia(path, emergency: false);
        }
        catch (Exception ex) { StartEmergency("MP3 실행 실패", ex); }
    }

    private void OpenAlertMedia(string path, bool emergency)
    {
        // Explicitly identify this stream as an alarm/alert rather than ordinary media.
        var player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Alerts,
            Volume = 1.0, IsMuted = SuppressSoundForTesting, IsLoopingEnabled = true, AutoPlay = false
        };
        _media = player;
        player.CommandManager.IsEnabled = false;
        void OnDispatcher(Action action) => _dispatcher.BeginInvoke(() =>
        {
            if (_media == player && IsActive) action();
        });
        player.MediaOpened += (_, _) => OnDispatcher(() =>
        {
            _progress.Restart();
            Report(emergency ? "내장 경고음 반복 재생 중 · Alerts 오디오" : "로컬 MP3 반복 재생 중 · Alerts 오디오");
            _store.Log("Alert audio system sound level: " + player.AudioStateMonitor.SoundLevel);
        });
        player.AudioStateMonitor.SoundLevelChanged += (_, _) => OnDispatcher(() =>
            _store.Log("Alert audio system sound level changed: " + player.AudioStateMonitor.SoundLevel));
        player.MediaFailed += (_, e) => OnDispatcher(() =>
        {
            var error = new InvalidOperationException($"{e.Error}: {e.ErrorMessage}");
            if (emergency) StartLegacyEmergency("Alerts 경고음 출력 실패", error);
            else StartEmergency("MP3 디코딩/오디오 장치 오류", error);
        });
        _source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(path), UriKind.Absolute));
        player.Source = _source;
        _lastPosition = TimeSpan.Zero; _progress.Restart();
        player.Play(); _watchdog.Start();
        Report(emergency ? "내장 경고음 여는 중 · Alerts 오디오" : "로컬 MP3 여는 중 · Alerts 오디오");
    }

    private void CheckPlayback()
    {
        if (!IsActive) return;
        // Audio endpoints can reappear late after S4 (e.g. USB/HDMI). Reopen periodically even if WAV was accepted.
        if (_emergencyRetry.IsRunning && _emergencyRetry.Elapsed > TimeSpan.FromSeconds(30)) { Start(_retryPath); return; }
        if (_beepOnly) { SystemSounds.Exclamation.Play(); return; }
        if (_media is null) return;
        if (_media.PlaybackSession.Position != _lastPosition) { _lastPosition = _media.PlaybackSession.Position; _progress.Restart(); }
        else if (_progress.Elapsed > TimeSpan.FromSeconds(10)) StartEmergency("MP3 재생 진행을 10초 동안 확인하지 못함");
    }

    private void StartEmergency(string reason, Exception? error = null)
    {
        _store.Log($"Emergency fallback: {reason}", error);
        _emergencyRetry.Restart();
        CloseMedia();
        try
        {
            var path = Path.Combine(_store.Root, "alarm-alert.wav");
            File.WriteAllBytes(path, CreateEmergencyWave());
            OpenAlertMedia(path, emergency: true);
        }
        catch (Exception ex) { StartLegacyEmergency("Alerts 경고음 초기화 실패", ex); }
    }

    private void StartLegacyEmergency(string reason, Exception error)
    {
        _store.Log(reason + "; using legacy alarm output", error);
        CloseMedia();
        _progress.Restart();
        try
        {
            _wave = new MemoryStream(CreateEmergencyWave(), writable: false);
            _emergency = new SoundPlayer(_wave) { LoadTimeout = 3000 };
            _emergency.Load(); _emergency.PlayLooping();
            Report("내장 경고음 반복 재생 중 · PC 볼륨/출력 장치를 확인하세요.");
        }
        catch (Exception ex)
        {
            _store.Log("Emergency WAV failed; using Windows system alert", ex);
            _beepOnly = true; SystemSounds.Exclamation.Play();
            Report("오디오 재생 오류 · Windows 경고음 재시도 중. 출력 장치를 확인하세요.");
        }
        _watchdog.Start();
    }

    // Generated PCM avoids downloads, codecs and external assets for the final fallback.
    public static byte[] CreateEmergencyWave()
    {
        const int rate = 22050, seconds = 2, samples = rate * seconds;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var cycle = i % (rate / 2);
            var envelope = Math.Clamp(Math.Min(cycle / 200.0, (rate / 3.0 - cycle) / 200.0), 0, 1);
            writer.Write((short)(Math.Sin(2 * Math.PI * 880 * i / rate) * 16000 * envelope));
        }
        return buffer.ToArray();
    }

    public void Stop()
    {
        IsActive = false; _watchdog.Stop(); _progress.Reset(); _emergencyRetry.Reset();
        CloseMedia();
        _emergency?.Stop(); _emergency?.Dispose(); _emergency = null;
        _wave?.Dispose(); _wave = null; _beepOnly = false;
    }
    private void CloseMedia()
    {
        var media = _media; _media = null;
        try { media?.Dispose(); }
        finally { _source?.Dispose(); _source = null; }
    }
    private void Report(string message) { _store.Log(message); StatusChanged?.Invoke(message); }
    public void Dispose() => Stop();
}
