using System.Text.Json;

namespace WindowsAlarm.Services;

public sealed record YouTubeMessage(YouTubeSample? Sample, string? Failure)
{
    public static bool TryRead(string json, string session, bool audioPlaying, out YouTubeMessage? message)
    {
        message = null;
        if (json.Length > 4096) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("session", out var id) || id.GetString() != session ||
                !root.TryGetProperty("type", out var type)) return false;
            if (type.GetString() == "blocked") { message = new(null, "YouTube 자동재생이 차단되었습니다."); return true; }
            if (type.GetString() == "error")
            {
                if (!root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var number) || number is < 0 or > 999) return false;
                message = new(null, $"YouTube 플레이어 오류 (코드 {number})"); return true;
            }
            if (type.GetString() != "progress" || !root.TryGetProperty("state", out var state) || !state.TryGetInt32(out var value) || value is not (-1 or 0 or 1 or 2 or 3 or 5) ||
                !root.TryGetProperty("position", out var position) || !position.TryGetDouble(out var seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > 315360000 ||
                !root.TryGetProperty("muted", out var muted) || muted.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("volume", out var volume) || !volume.TryGetInt32(out var level) || level is < 0 or > 100) return false;
            message = new(new YouTubeSample(value, seconds, muted.GetBoolean(), level, audioPlaying), null);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return false; }
    }
}
