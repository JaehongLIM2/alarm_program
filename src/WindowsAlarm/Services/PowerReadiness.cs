namespace WindowsAlarm.Services;

public enum PowerCheckState { Ready, NeedsAttention, Unknown }

public sealed record PowerStatusSnapshot(WindowsPowerService.PowerCapabilities? Capabilities, bool? OnAcPower,
    uint? AcWakePolicy, uint? DcWakePolicy, IReadOnlyList<string> Errors)
{
    public DateTimeOffset ReadAt { get; init; } = DateTimeOffset.Now;
    public uint? CurrentWakePolicy => OnAcPower is true ? AcWakePolicy : OnAcPower is false ? DcWakePolicy : null;
}

public sealed record PowerCheck(string Title, string Value, string Explanation, PowerCheckState State)
{
    public bool IsReady => State == PowerCheckState.Ready;
    public bool IsUnknown => State == PowerCheckState.Unknown;
}

public sealed record PowerReadinessReport(PowerCheckState State, string Summary, string PowerSource,
    string ReadAt, IReadOnlyList<PowerCheck> Checks, string Diagnostics)
{
    public bool IsReady => State == PowerCheckState.Ready;

    public static PowerReadinessReport Create(PowerStatusSnapshot snapshot)
    {
        var checks = new List<PowerCheck>();
        var caps = snapshot.Capabilities;
        checks.Add(caps is null
            ? new("최대 절전 (S4)", "확인 불가", "이 앱은 절전 대신 최대 절전(S4)을 사용합니다. Windows에서 최대 절전을 사용할 수 있어야 합니다.", PowerCheckState.Unknown)
            : caps.Value.SystemS4 != 0
                ? new("최대 절전 (S4)", "사용 가능", "Windows에서 최대 절전(S4)을 사용할 수 있습니다.", PowerCheckState.Ready)
                : new("최대 절전 (S4)", "설정 확인 필요", "최대 절전이 꺼져 있거나 현재 환경에서 사용할 수 없습니다. 아래 안내로 활성화한 뒤 powercfg /a로 확인하세요.", PowerCheckState.NeedsAttention));

        checks.Add(caps is null
            ? new("최대 절전 파일", "확인 불가", "빠른 시작 전용 파일이 아닌 전체 최대 절전 파일이 필요합니다.", PowerCheckState.Unknown)
            : caps.Value.HiberFilePresent == 0
                ? new("최대 절전 파일", "파일 없음", "전체 최대 절전 파일이 필요합니다. 아래 최대 절전 활성화 방법을 확인하세요.", PowerCheckState.NeedsAttention)
                : caps.Value.HiberFileType == 2
                    ? new("최대 절전 파일", "전체 파일 사용 중", "S4 최대 절전에 필요한 전체 파일이 준비되어 있습니다.", PowerCheckState.Ready)
                    : caps.Value.HiberFileType == 1
                        ? new("최대 절전 파일", "빠른 시작 전용", "빠른 시작 전용 파일로는 충분하지 않습니다. 관리자 터미널에서 powercfg /h /type full을 실행하세요.", PowerCheckState.NeedsAttention)
                        : new("최대 절전 파일", "파일 종류 확인 필요", "전체 최대 절전 파일인지 확인할 수 없습니다. powercfg /a와 아래 활성화 방법을 확인하세요.", PowerCheckState.Unknown));

        var source = snapshot.OnAcPower switch { true => "전원 연결 (AC)", false => "배터리 사용 (DC)", _ => "전원 공급 상태 확인 불가" };
        var policy = snapshot.CurrentWakePolicy;
        var policyText = $"전원 연결: {PolicyName(snapshot.AcWakePolicy)} · 배터리: {PolicyName(snapshot.DcWakePolicy)}";
        var policyState = policy == 1 ? PowerCheckState.Ready
            : policy is 0 or 2 ? PowerCheckState.NeedsAttention : PowerCheckState.Unknown;
        checks.Add(new("절전 모드 해제 타이머", snapshot.OnAcPower is null ? "현재 전원 확인 필요" : PolicyName(policy),
            $"{policyText}\n현재 사용하는 전원의 항목이 '사용'이어야 합니다. '중요한 타이머만'으로는 충분하지 않습니다. 전원을 바꿔 사용할 때는 해당 항목도 확인하세요.", policyState));

        var rtc = caps?.RtcWake;
        checks.Add(new("예약 시각에 자동 복귀", rtc is >= 5 and <= 6 ? "Windows 보고: S4 복귀 지원"
                : rtc is >= 1 and <= 4 ? "S4 복귀 지원 확인 필요" : "확인 불가",
            rtc is >= 5 and <= 6
                ? "Windows가 S4 예약 복귀 지원을 보고했습니다. 실제 복귀는 PC와 BIOS/UEFI 설정에 따라 달라질 수 있습니다."
                : "Windows 정보만으로 S4 예약 복귀를 확인하지 못했습니다. 제조사 안내에서 RTC Alarm·Wake from S4 등의 지원과 설정을 확인하세요.",
            rtc is >= 5 and <= 6 ? PowerCheckState.Ready : rtc is >= 1 and <= 4 ? PowerCheckState.NeedsAttention : PowerCheckState.Unknown));

        var state = checks.Any(check => check.State == PowerCheckState.NeedsAttention) ? PowerCheckState.NeedsAttention
            : checks.Any(check => check.State == PowerCheckState.Unknown) ? PowerCheckState.Unknown : PowerCheckState.Ready;
        var summary = state switch
        {
            PowerCheckState.Ready => "현재 전원의 기본 조건이 충족되었습니다",
            PowerCheckState.NeedsAttention => "사용 전에 확인할 설정이 있습니다",
            _ => "일부 전원 상태를 확인하지 못했습니다"
        };
        var diagnostics = $"확인 시각: {snapshot.ReadAt:O}\n전원: {source}\n" +
            string.Join("\n", checks.Select(check => $"{check.Title}: {check.Value}")) +
            $"\nWake Timer AC={snapshot.AcWakePolicy?.ToString() ?? "unknown"}, DC={snapshot.DcWakePolicy?.ToString() ?? "unknown"}";
        if (snapshot.Errors.Count > 0) diagnostics += "\n\n조회 오류:\n" + string.Join("\n", snapshot.Errors);
        return new(state, summary, source, $"마지막 확인 {snapshot.ReadAt.LocalDateTime:HH:mm:ss}", checks, diagnostics);
    }

    private static string PolicyName(uint? policy) => policy switch { 0 => "사용 안 함", 1 => "사용", 2 => "중요한 타이머만", _ => "확인 불가" };
}
