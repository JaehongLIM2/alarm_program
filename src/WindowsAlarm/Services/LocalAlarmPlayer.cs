using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;

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
    private MediaPlayer? _media;
    private SoundPlayer? _emergency;
    private MemoryStream? _wave;
    private TimeSpan _lastPosition;
    private bool _beepOnly;
    private string? _retryPath;
    public bool IsActive { get; private set; }
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
            var player = new MediaPlayer { Volume = 1.0, IsMuted = false };
            _media = player;
            player.MediaOpened += (_, _) =>
            {
                if (_media != player || !IsActive) return;
                try { player.Play(); _progress.Restart(); Report("로컬 MP3 반복 재생 중"); }
                catch (Exception ex) { StartEmergency("MP3 재생 시작 실패", ex); }
            };
            player.MediaEnded += (_, _) =>
            {
                if (_media != player || !IsActive) return;
                try { player.Position = TimeSpan.Zero; _lastPosition = TimeSpan.Zero; player.Play(); _progress.Restart(); }
                catch (Exception ex) { StartEmergency("MP3 반복 재생 실패", ex); }
            };
            player.MediaFailed += (_, e) => { if (_media == player && IsActive) StartEmergency("MP3 디코딩/오디오 장치 오류", e.ErrorException); };
            _lastPosition = TimeSpan.Zero; _progress.Restart();
            player.Open(new Uri(Path.GetFullPath(path), UriKind.Absolute));
            player.Play(); _watchdog.Start();
            Report("로컬 MP3 여는 중");
        }
        catch (Exception ex) { StartEmergency("MP3 실행 실패", ex); }
    }

    private void CheckPlayback()
    {
        if (!IsActive) return;
        // Audio endpoints can reappear late after S4 (e.g. USB/HDMI). Reopen periodically even if WAV was accepted.
        if (_media is null && _progress.Elapsed > TimeSpan.FromSeconds(30)) { Start(_retryPath); return; }
        if (_beepOnly) { SystemSounds.Exclamation.Play(); return; }
        if (_media is null) return;
        if (_media.Position != _lastPosition) { _lastPosition = _media.Position; _progress.Restart(); }
        else if (_progress.Elapsed > TimeSpan.FromSeconds(10)) StartEmergency("MP3 재생 진행을 10초 동안 확인하지 못함");
    }

    private void StartEmergency(string reason, Exception? error = null)
    {
        _store.Log($"Emergency fallback: {reason}", error);
        var media = _media; _media = null; media?.Close();
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
        IsActive = false; _watchdog.Stop(); _progress.Reset();
        _media?.Close(); _media = null;
        _emergency?.Stop(); _emergency?.Dispose(); _emergency = null;
        _wave?.Dispose(); _wave = null; _beepOnly = false;
    }
    private void Report(string message) { _store.Log(message); StatusChanged?.Invoke(message); }
    public void Dispose() => Stop();
}
