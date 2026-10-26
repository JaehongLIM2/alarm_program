using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Security.Cryptography;

namespace WindowsAlarm.Services;

// Bounded, memory-only PCM handoff from our YouTube frame to an Alerts audio stream.
internal sealed class AlertPcmPlayer : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _queue = new();
    private readonly MediaPlayer _player;
    private readonly MediaStreamSource _stream;
    private readonly MediaSource _source;
    private MediaStreamSourceSampleRequest? _pending;
    private MediaStreamSourceSampleRequestDeferral? _deferral;
    private long _frames, _lastSignal;
    private bool _stopped;
    public long Packets { get; private set; }
    public event Action<string>? Failed;
    internal bool IsRendering => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing &&
        _player.PlaybackSession.Position > TimeSpan.FromMilliseconds(200);
    public bool HasDecodedSignal => Volatile.Read(ref _lastSignal) is var last && last != 0 && Stopwatch.GetElapsedTime(last) < TimeSpan.FromSeconds(2);
    public bool HasRecentSignal => HasDecodedSignal && !_player.IsMuted &&
        IsRendering &&
        _player.AudioStateMonitor.SoundLevel != Windows.Media.SoundLevel.Muted;

    public AlertPcmPlayer(bool mutedForTesting = false)
    {
        var descriptor = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(48000, 2, 16));
        _stream = new MediaStreamSource(descriptor) { CanSeek = false, BufferTime = TimeSpan.Zero };
        _stream.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        _stream.SampleRequested += (_, e) =>
        {
            lock (_gate)
            {
                if (_stopped) { e.Request.Sample = null; return; }
                if (_queue.TryDequeue(out var bytes)) e.Request.Sample = Sample(bytes);
                else { _pending = e.Request; _deferral = e.Request.GetDeferral(); }
            }
        };
        _source = MediaSource.CreateFromMediaStreamSource(_stream);
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts, IsMuted = mutedForTesting, Volume = 1 };
        _player.CommandManager.IsEnabled = false;
        _player.MediaFailed += (_, e) => { lock (_gate) { if (!_stopped) Failed?.Invoke($"Alerts 오디오 오류: {e.Error} · {e.ErrorMessage}"); } };
        _player.Source = _source;
        _player.Play();
    }
    private MediaStreamSample Sample(byte[] bytes)
    {
        var sample = MediaStreamSample.CreateFromBuffer(CryptographicBuffer.CreateFromByteArray(bytes), TimeSpan.FromTicks(_frames * TimeSpan.TicksPerSecond / 48000));
        var frames = bytes.Length / 4;
        sample.Duration = TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / 48000);
        _frames += frames;
        return sample;
    }
    public void Push(byte[] bytes)
    {
        if (bytes.Length is < 4 or > 16384 || bytes.Length % 4 != 0) throw new ArgumentException("Invalid PCM packet.");
        MediaStreamSourceSampleRequestDeferral? completed = null;
        lock (_gate)
        {
            if (_stopped) return;
            Packets++;
            for (var i = 0; i < bytes.Length; i += 2)
                if (Math.Abs((int)BitConverter.ToInt16(bytes, i)) > 32) { Volatile.Write(ref _lastSignal, Stopwatch.GetTimestamp()); break; }
            if (_pending is { } request)
            {
                request.Sample = Sample(bytes); _pending = null;
                completed = _deferral; _deferral = null;
            }
            else
            {
                while (_queue.Count >= 12) _queue.Dequeue();
                _queue.Enqueue(bytes);
            }
        }
        completed?.Complete();
    }
    public void Dispose()
    {
        MediaStreamSourceSampleRequestDeferral? completed = null;
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true; _queue.Clear();
            if (_pending is { } request) { request.Sample = null; _pending = null; completed = _deferral; _deferral = null; }
        }
        completed?.Complete();
        try { _player.Pause(); }
        finally { try { _player.Dispose(); } finally { _source.Dispose(); } }
    }
}
