using System.Text.Json;

namespace WindowsAlarm.Services;

internal static class YouTubePcmMessage
{
    public static bool TryRead(string json, string session, out byte[]? pcm, out string? failure, out bool ready)
    {
        pcm = null; failure = null; ready = false;
        if (json.Length > 24000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("session", out var id) || id.GetString() != session ||
                !root.TryGetProperty("type", out var type)) return false;
            switch (type.GetString())
            {
                case "pcm":
                    if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) return false;
                    var encoded = data.GetString()!;
                    if (encoded.Length > 21848) return false;
                    var decoded = Convert.FromBase64String(encoded);
                    if (decoded.Length is < 4 or > 16384 || decoded.Length % 4 != 0) return false;
                    pcm = decoded; return true;
                case "relay-ready":
                    ready = root.TryGetProperty("state", out var state) && state.GetString() == "running";
                    return true;
                case "relay-error":
                    failure = "YouTube 오디오 전달을 시작할 수 없습니다.";
                    return true;
                default: return false;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return false; }
    }
}
