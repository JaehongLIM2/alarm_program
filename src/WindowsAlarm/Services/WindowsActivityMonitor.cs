using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsAlarm.Services;

public sealed class WindowsActivityMonitor : IActivityMonitor
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Time; }
    [StructLayout(LayoutKind.Sequential)] private struct SessionInfo { public uint SessionId; public IntPtr StationName; public int State; }

    public InputSnapshot Read()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return new(false, false, 0, TimeSpan.Zero);
        // Both values use the same 32-bit tick clock. Unsigned subtraction handles its 49.7-day wrap.
        var idleMs = unchecked((uint)Environment.TickCount - info.Time);
        // A future/nonmonotonic input timestamp can otherwise look like ~49 days of inactivity.
        if (idleMs > int.MaxValue) return new(false, false, info.Time, TimeSpan.Zero);
        return new(true, IsOnlyConsoleUser(), info.Time, TimeSpan.FromMilliseconds(idleMs));
    }

    private static bool IsOnlyConsoleUser()
    {
        using var process = Process.GetCurrentProcess();
        var ownSession = (uint)process.SessionId;
        // GetLastInputInfo sees only our session. Do not auto-shut down an RDP/other active console session.
        if (WTSGetActiveConsoleSessionId() != ownSession) return false;
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count)) return false;
        try
        {
            var size = Marshal.SizeOf<SessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var session = Marshal.PtrToStructure<SessionInfo>(IntPtr.Add(buffer, i * size));
                if (session.SessionId == ownSession || session.SessionId == 0) continue;
                // Also protect a disconnected user's unsaved work; username query failure is uncertain => protect.
                if (!WTSQuerySessionInformation(IntPtr.Zero, session.SessionId, 5, out var username, out _)) return false;
                try { if (!string.IsNullOrEmpty(Marshal.PtrToStringUni(username))) return false; }
                finally { WTSFreeMemory(username); }
            }
            return true;
        }
        finally { WTSFreeMemory(buffer); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSEnumerateSessionsW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr buffer, out int count);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
}
