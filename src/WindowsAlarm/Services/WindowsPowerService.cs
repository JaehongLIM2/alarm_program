using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WindowsAlarm.Services;

public sealed class WindowsPowerService : IPowerService
{
    // SYSTEM_POWER_CAPABILITIES, Windows 10/11 layout from the official Windows SDK.
    // BOOLEAN is one byte, unlike Win32 BOOL. Unused fields still occupy the full 76-byte native buffer.
    [StructLayout(LayoutKind.Explicit, Size = 76)]
    public struct PowerCapabilities
    {
        [FieldOffset(6)] public byte SystemS4;
        [FieldOffset(8)] public byte HiberFilePresent;
        [FieldOffset(22)] public byte HiberFileType;
        [FieldOffset(64)] public int RtcWake;
    }

    public PowerCapabilities ReadCapabilities()
    {
        if (!GetPwrCapabilities(out var caps)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return caps;
    }

    public string GetDiagnostics()
    {
        var caps = ReadCapabilities();
        var policy = ReadWakePolicy();
        return $"S4 지원={caps.SystemS4 != 0}, 최대 절전 파일={caps.HiberFilePresent != 0}, " +
               $"전체 파일={caps.HiberFileType == 2}, RTC Wake 지원 하한={PowerStateName(caps.RtcWake)}, Wake Timer 설정={policy?.ToString() ?? "확인 불가"}";
    }

    private static string PowerStateName(int state) => state switch { 1 => "S0", 2 => "S1", 3 => "S2", 4 => "S3", 5 => "S4", 6 => "S5", _ => "확인 불가" };

    public void EnsureHibernateAvailable()
    {
        var caps = ReadCapabilities();
        if (caps.SystemS4 == 0 || caps.HiberFilePresent == 0 || caps.HiberFileType != 2)
            throw new InvalidOperationException("Windows 최대 절전 모드가 활성화되어 있지 않습니다.\n\n관리자 터미널에서 powercfg /hibernate on 및 powercfg /h /type full 실행 후 powercfg /a로 확인하세요. 이 앱은 설정을 직접 변경하지 않습니다.");
        var wakePolicy = ReadWakePolicy();
        if (wakePolicy is null)
            throw new InvalidOperationException("현재 전원 계획의 Wake Timer 설정을 확인할 수 없습니다. 최대 절전 진입을 중단했습니다. 전원 옵션과 전원 상태 진단을 확인하세요.");
        if (wakePolicy != 1)
            throw new InvalidOperationException("현재 전원 계획의 Wake Timer가 '사용'이 아닙니다.\n전원 옵션 → 고급 설정 → 절전 → 절전 모드 해제 타이머 허용을 '사용'으로 바꿔 주세요. '중요한 타이머만'으로는 이 알람의 Wake를 보장할 수 없습니다.");
    }

    // Read only. No registry edits or changes to the user's power plan.
    public uint? ReadWakePolicy()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var memory) != 0) return null;
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(memory);
            var subgroup = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
            var setting = new Guid("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d");
            if (!GetSystemPowerStatus(out var status) || status.AcLineStatus == 255) return null;
            var error = status.AcLineStatus == 1
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
            return error == 0 ? value : null;
        }
        finally { LocalFree(memory); }
    }

    public void Hibernate()
    {
        EnsureHibernateAvailable();
        using var privilege = ShutdownPrivilege.Acquire();
        // TRUE = S4, never S3; FALSE keeps wake events enabled. Force parameter is unused by Windows.
        if (!SetSuspendState(true, false, false)) throw new Win32Exception(Marshal.GetLastWin32Error(), "S4 최대 절전 진입에 실패했습니다.");
    }

    public void Shutdown()
    {
        using var privilege = ShutdownPrivilege.Acquire();
        // Normal power-off only. NEVER EWX_FORCE / EWX_FORCEIFHUNG. Apps may block unsaved-work shutdown.
        if (!ExitWindowsEx(0x00000008, 0x80040000)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 종료 요청이 거부되었습니다.");
    }

    public void KeepAwake(bool enabled)
    {
        // Called and cleared on the same WPF dispatcher thread. Do not inhibit sleep while merely armed.
        if (SetThreadExecutionState(enabled ? 0x80000003u : 0x80000000u) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "알람 재생 중 절전 방지 요청 실패");
    }

    public void WakeDisplay()
    {
        // Timer wakes may leave the display off. Request monitor power without
        // injecting input or changing the user's lock/Windows Hello settings.
        if (SendMessageTimeout(new IntPtr(0xffff), 0x0112, new UIntPtr(0xf170),
                new IntPtr(-1), 0x0002, 250, out _) == IntPtr.Zero)
            throw new InvalidOperationException("화면 켜기 요청이 완료되지 않았습니다.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    private sealed class ShutdownPrivilege : IDisposable
    {
        private readonly SafeAccessTokenHandle _token;
        private TokenPrivileges _previous;
        private ShutdownPrivilege(SafeAccessTokenHandle token, TokenPrivileges previous) { _token = token; _previous = previous; }
        public static ShutdownPrivilege Acquire()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var value = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                var ok = AdjustTokenPrivileges(token, false, ref value, (uint)Marshal.SizeOf<TokenPrivileges>(), out var previous, out _);
                var error = Marshal.GetLastWin32Error();
                // TRUE can still mean ERROR_NOT_ALL_ASSIGNED (1300). Never mistake that for authorization.
                if (!ok || error != 0) throw new Win32Exception(error, "현재 사용자에게 Windows 전원 제어 권한이 없습니다. 로컬 보안 정책을 확인하세요.");
                return new ShutdownPrivilege(token, previous);
            }
            catch { token.Dispose(); throw; }
        }
        public void Dispose()
        {
            RestoreTokenPrivileges(_token, false, ref _previous, 0, IntPtr.Zero, IntPtr.Zero);
            _token.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct SystemPowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("powrprof.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool GetPwrCapabilities(out PowerCapabilities capabilities);
    [DllImport("powrprof.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvents);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges privileges, uint length, out TokenPrivileges previous, out uint returnedLength);
    [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RestoreTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges privileges, uint length, IntPtr previous, IntPtr returnedLength);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
