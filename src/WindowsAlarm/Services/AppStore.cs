using System.IO;
using System.Text.Json;

namespace WindowsAlarm.Services;

public sealed class AppStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object _gate = new();
    public string Root { get; }
    public string? LoadWarning { get; private set; }

    public AppStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
    }

    public T? Load<T>(string name) where T : class
    {
        lock (_gate)
        {
            var path = Path.Combine(Root, name);
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new JsonException("JSON null"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                LoadWarning = $"{name}을 읽을 수 없습니다. 로그를 확인하세요.";
                Log($"Load failed: {name}", ex);
                try { File.Copy(path, path + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}", false); } catch (Exception) { /* best effort */ }
                return null;
            }
        }
    }

    public void Save<T>(string name, T value)
    {
        lock (_gate)
        {
            var path = Path.Combine(Root, name);
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
    }

    public void Log(string message, Exception? error = null)
    {
        lock (_gate)
        {
            try
            {
                var directory = Path.Combine(Root, "logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd}.log"),
                    $"{DateTimeOffset.Now:O} {message}{(error is null ? "" : " | " + error)}{Environment.NewLine}");
            }
            catch (Exception) { /* A full/read-only disk must not suppress an alarm. */ }
        }
    }
}
