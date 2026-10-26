namespace WindowsAlarm.Models;

// A schedule keeps its own snapshot. Future recurring/multiple alarms can share these settings.
public sealed record AlarmSettings
{
    public int Hour { get; init; } = 7;
    public int Minute { get; init; } = 30;
    public string YouTubeUrl { get; init; } = "";
    public string LocalMusicPath { get; init; } = "";
    public bool AutoShutdownEnabled { get; init; }
    public int ShutdownAfterMinutes { get; init; } = 30;

    public string? Validate()
    {
        if (Hour is < 0 or > 23 || Minute is < 0 or > 59) return "올바른 알람 시간을 선택하세요.";
        if (ShutdownAfterMinutes is < 1 or > 1440) return "자동 종료 시간은 1~1440분 사이로 입력하세요.";
        if (!string.IsNullOrWhiteSpace(YouTubeUrl) && !TryGetYouTubeUri(YouTubeUrl, out _))
            return "올바른 HTTPS YouTube 동영상 URL을 입력하세요.";
        if (!string.IsNullOrWhiteSpace(LocalMusicPath))
        {
            if (!System.IO.Path.IsPathFullyQualified(LocalMusicPath) || !System.IO.File.Exists(LocalMusicPath))
                return "로컬 음악 파일을 찾을 수 없습니다. 전체 경로를 확인하세요.";
            if (!string.Equals(System.IO.Path.GetExtension(LocalMusicPath), ".mp3", StringComparison.OrdinalIgnoreCase))
                return "로컬 알람 음악은 MP3 파일을 선택하세요.";
        }
        return null;
    }

    public static bool TryGetYouTubeUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !parsed.IsDefaultPort) return false;
        var host = parsed.IdnHost.ToLowerInvariant();
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be" or "www.youtu.be")) return false;
        var path = parsed.AbsolutePath;
        var hasVideo = host.EndsWith("youtu.be", StringComparison.Ordinal)
            ? path.Trim('/').Length > 0
            : (path == "/watch" && parsed.Query.TrimStart('?').Split('&').Any(q => q.StartsWith("v=", StringComparison.Ordinal) && q.Length > 2)) ||
              ((path.StartsWith("/shorts/", StringComparison.Ordinal) || path.StartsWith("/live/", StringComparison.Ordinal) ||
                path.StartsWith("/embed/", StringComparison.Ordinal)) && path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 2);
        if (!hasVideo) return false;
        uri = parsed;
        return true;
    }
}
