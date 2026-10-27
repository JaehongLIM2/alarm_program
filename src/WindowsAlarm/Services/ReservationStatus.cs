using WindowsAlarm.Models;

namespace WindowsAlarm.Services;

// Project committed reservation state, not the unsaved form or audio-playing state.
// Null hides a row. Failed cancellations keep their row so the user can retry.
internal sealed record ReservationStatus(string? AlarmText, string? ShutdownText, string? ShutdownOutcome)
{
    public static ReservationStatus Create(AlarmSchedule? schedule, ShutdownMode mode, DateTimeOffset? shutdownDueAt,
        bool wakeCancellationNeedsRetry = false, bool shutdownCancellationNeedsRetry = false)
    {
        var alarm = wakeCancellationNeedsRetry ? "취소 미완료 · 다시 시도해 주세요."
            : schedule is { Phase: AlarmPhase.Armed } ? $"{schedule.DueAt.LocalDateTime:yyyy-MM-dd HH:mm}"
            : null;
        var shutdown = mode switch
        {
            ShutdownMode.Scheduled => $"{shutdownDueAt?.LocalDateTime:yyyy-MM-dd HH:mm} 종료 확인 예정",
            ShutdownMode.Countdown => "60초 종료 카운트다운 진행 중",
            ShutdownMode.ActivityConfirmation => "사용 중 감지 · 무응답 시 종료 취소",
            _ => null
        };
        if (shutdownCancellationNeedsRetry && mode != ShutdownMode.Requested)
            shutdown = "취소 저장 미완료 · 다시 시도해 주세요.";
        else if (shutdown is null && schedule is { Phase: AlarmPhase.Armed, IsTest: false, ShutdownCancelled: false, Settings.AutoShutdownEnabled: true })
            shutdown = $"알람 시작 {schedule.Settings.ShutdownAfterMinutes}분 후 종료 확인";

        var outcome = mode switch
        {
            ShutdownMode.Requested => "Windows에 정상 종료를 요청했습니다. 이후 취소는 Windows 종료 화면에서 가능합니다.",
            ShutdownMode.Cancelled when shutdown is null => "PC 자동 종료 취소됨",
            _ => null
        };
        return new(alarm, shutdown, outcome);
    }
}
