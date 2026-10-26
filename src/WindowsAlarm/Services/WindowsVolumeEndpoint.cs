using System.Runtime.InteropServices;

namespace WindowsAlarm.Services;

// Keep the originally selected endpoint alive so restoration never changes a different device.
public sealed class WindowsVolumeEndpoint : IVolumeEndpoint
{
    private IAudioEndpointVolume? _volume;
    public WindowsVolumeEndpoint()
    {
        object? enumerator = null, endpoint = null, volume = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            Check(((IDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out endpoint));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Check(((IDevice)endpoint).Activate(ref iid, 23, IntPtr.Zero, out volume));
            _volume = (IAudioEndpointVolume)volume; volume = null;
        }
        finally { Release(volume); Release(endpoint); Release(enumerator); }
    }
    public float Level
    {
        get { Check(Volume.GetMasterVolumeLevelScalar(out var value)); return value; }
        set { if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value)); Check(Volume.SetMasterVolumeLevelScalar(value, IntPtr.Zero)); }
    }
    public bool Muted
    {
        get { Check(Volume.GetMute(out var value)); return value; }
        set => Check(Volume.SetMute(value, IntPtr.Zero));
    }
    private IAudioEndpointVolume Volume => _volume ?? throw new ObjectDisposedException(nameof(WindowsVolumeEndpoint));
    public void Dispose() { var volume = _volume; _volume = null; Release(volume); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out object devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, [MarshalAs(UnmanagedType.IUnknown)] out object device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    { [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
