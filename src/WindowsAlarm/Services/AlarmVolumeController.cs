using WindowsAlarm.Models;

namespace WindowsAlarm.Services;

public interface IAlarmVolumeControl
{
    void Begin(AlarmSettings settings);
    void End();
}
public interface IVolumeEndpoint : IDisposable
{
    float Level { get; set; }
    bool Muted { get; set; }
}

public sealed class AlarmVolumeController : IAlarmVolumeControl
{
    private readonly Func<IVolumeEndpoint> _open;
    private readonly Action<string> _log;
    private Snapshot? _active;
    private bool _ending;
    private sealed class Snapshot(IVolumeEndpoint endpoint, float level, bool muted, AlarmVolumeAfterStop afterStop)
    {
        public IVolumeEndpoint Endpoint { get; } = endpoint;
        public float Level { get; } = level;
        public bool Muted { get; } = muted;
        public AlarmVolumeAfterStop AfterStop { get; set; } = afterStop;
    }
    public AlarmVolumeController(Action<string> log, Func<IVolumeEndpoint>? open = null)
    { _log = log; _open = open ?? (() => new WindowsVolumeEndpoint()); }

    public void Begin(AlarmSettings settings)
    {
        End();
        if (!settings.MaximizeWindowsVolume) return;
        if (!Enum.IsDefined(settings.VolumeAfterStop)) throw new InvalidOperationException("알람 종료 후 볼륨 설정이 올바르지 않습니다.");
        var endpoint = _open();
        Snapshot snapshot;
        try { snapshot = new(endpoint, endpoint.Level, endpoint.Muted, settings.VolumeAfterStop); }
        catch { endpoint.Dispose(); throw; }
        _active = snapshot;
        try
        {
            endpoint.Level = 1;
            if (!ReferenceEquals(_active, snapshot)) return; // Stop may have reentered during a native call.
            endpoint.Muted = false;
            if (ReferenceEquals(_active, snapshot)) _log("Windows alarm volume set to 100; mute disabled");
        }
        catch
        {
            if (ReferenceEquals(_active, snapshot))
            {
                snapshot.AfterStop = AlarmVolumeAfterStop.RestorePrevious;
                End();
            }
            throw;
        }
    }
    public void End()
    {
        if (_active is not { } snapshot || _ending) return;
        _ending = true;
        try
        {
            snapshot.Endpoint.Level = snapshot.AfterStop == AlarmVolumeAfterStop.SetZero ? 0 : snapshot.Level;
            snapshot.Endpoint.Muted = snapshot.AfterStop == AlarmVolumeAfterStop.RestorePrevious && snapshot.Muted;
            _log(snapshot.AfterStop == AlarmVolumeAfterStop.SetZero ? "Windows alarm volume set to 0; mute disabled" : "Windows volume and mute restored");
            _active = null;
            snapshot.Endpoint.Dispose();
        }
        finally { _ending = false; } // Retain a failed restoration for the next stop/close attempt.
    }
}
