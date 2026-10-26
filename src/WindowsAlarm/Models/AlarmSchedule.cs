namespace WindowsAlarm.Models;

public enum AlarmPhase { Armed, Ringing, Stopped, Completed, Cancelled, Missed }

public sealed record AlarmSchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset DueAt { get; init; }
    public AlarmSettings Settings { get; init; } = new();
    public AlarmPhase Phase { get; init; } = AlarmPhase.Armed;
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? ShutdownAt { get; init; }
    public bool ShutdownCancelled { get; init; }
    public bool IsTest { get; init; }
}

public static class AlarmTiming
{
    public static readonly TimeSpan RecoveryGrace = TimeSpan.FromHours(2);

    public static DateTimeOffset NextOccurrence(int hour, int minute, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (hour is < 0 or > 23 || minute is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(hour));
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        for (var day = 0; day < 3; day++)
        {
            var local = DateTime.SpecifyKind(localNow.Date.AddDays(day).AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
            // Spring DST gap: first valid local minute; fall overlap: first still-future occurrence.
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var offsets = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local) : [zone.GetUtcOffset(local)];
            foreach (var candidate in offsets.Select(o => new DateTimeOffset(local, o)).OrderBy(t => t.UtcDateTime))
                if (candidate > now) return candidate;
        }
        throw new InvalidOperationException("다음 알람 시간을 계산할 수 없습니다.");
    }
}
