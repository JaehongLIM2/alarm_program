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
        if (!TryExtractVideoId(parsed, out _)) return false;
        uri = parsed;
        return true;
    }

    public static bool TryGetYouTubeVideoId(string? value, out string videoId)
    {
        videoId = "";
        return TryGetYouTubeUri(value, out var uri) && TryExtractVideoId(uri!, out videoId);
    }

    private static bool TryExtractVideoId(Uri uri, out string videoId)
    {
        videoId = "";
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = null;
        if (uri.IdnHost.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase) && path.Length == 1) candidate = path[0];
        else if (uri.AbsolutePath == "/watch")
        {
            var values = uri.Query.TrimStart('?').Split('&').Where(q => q.StartsWith("v=", StringComparison.Ordinal)).ToArray();
            if (values.Length == 1) candidate = values[0][2..];
        }
        else if (path.Length == 2 && path[0] is "shorts" or "live" or "embed") candidate = path[1];
        if (candidate is null) return false;
        candidate = Uri.UnescapeDataString(candidate);
        if (candidate.Length != 11 || !candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return false;
        videoId = candidate;
        return true;
    }
}
