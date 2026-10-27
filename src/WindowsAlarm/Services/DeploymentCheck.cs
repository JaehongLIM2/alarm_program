using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Windows.Media.Playback;

namespace WindowsAlarm.Services;

internal sealed record DeploymentCheckResult(string Executable, string Runtime, bool EmbeddedResources,
    bool AlertsApi, string? WebView2Version, bool WebViewBridge, IReadOnlyList<string> Errors)
{
    public bool Success => EmbeddedResources && AlertsApi && WebViewBridge && Errors.Count == 0;
}

// Runs inside the published EXE, not a test assembly next to loose dependency DLLs.
// This is an explicit diagnostic switch; it never plays audio or restores an alarm.
internal static class DeploymentCheck
{
    public static async Task<DeploymentCheckResult> RunAsync(AppStore store)
    {
        var errors = new List<string>();
        var resources = true;
        foreach (var name in new[] { "YouTubePlayer.html", "YouTubeAudioRelay.js" })
        {
            using var resource = typeof(App).Assembly.GetManifestResourceStream("WindowsAlarm.Assets." + name);
            if (resource is null || resource.Length == 0) { resources = false; errors.Add("Missing embedded resource: " + name); }
        }
        try
        {
            var font = Application.Current.TryFindResource("AppFontFamily") as System.Windows.Media.FontFamily
                ?? throw new InvalidOperationException("Missing application font resource.");
            foreach (var (name, weight) in new[] { ("NanumSquareL.otf", FontWeights.Light), ("NanumSquareR.otf", FontWeights.Normal),
                ("NanumSquareB.otf", FontWeights.Bold), ("NanumSquareEB.otf", FontWeights.ExtraBold) })
            {
                var face = new System.Windows.Media.Typeface(font, FontStyles.Normal, weight, FontStretches.Normal);
                if (!face.TryGetGlyphTypeface(out var glyph) || glyph.Weight != weight ||
                    !glyph.FontUri.OriginalString.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) ||
                    !glyph.CharacterToGlyphMap.ContainsKey('알'))
                    throw new InvalidOperationException("Embedded NanumSquare font did not resolve: " + name);
            }
            var license = Application.GetResourceStream(new Uri("pack://application:,,,/WindowsAlarm;component/Assets/Fonts/LICENSE.txt"))
                ?? throw new InvalidOperationException("Missing embedded font license.");
            using var reader = new StreamReader(license.Stream);
            if (!reader.ReadToEnd().Contains("SIL OPEN FONT LICENSE")) throw new InvalidOperationException("Invalid embedded font license.");
        }
        catch (Exception ex) { resources = false; errors.Add("Font resources: " + ex.Message); }
        var alerts = false;
        try
        {
            using var player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Alerts, IsMuted = true, Volume = 0 };
            alerts = player.AudioCategory == MediaPlayerAudioCategory.Alerts;
        }
        catch (Exception ex) { errors.Add("Alerts API: " + ex.Message); }

        string? version = null;
        var bridge = false;
        WebView2? view = null;
        Window? window = null;
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            view = new WebView2();
            window = new Window { Title = "Windows Alarm 배포 검증", Width = 320, Height = 180,
                ShowActivated = false, ShowInTaskbar = false, Content = view };
            window.Show();
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(store.Root, "webview2-check"))
                .WaitAsync(TimeSpan.FromSeconds(20));
            await view.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(20));
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = Guid.NewGuid().ToString("N");
            view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            view.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            view.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            view.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (e.WebMessageAsJson == "\"" + token + "\"") ready.TrySetResult();
            };
            view.NavigateToString("<!doctype html><title>Deployment check</title><script>chrome.webview.postMessage('" + token + "')</script>");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bridge = true;
        }
        catch (Exception ex) { errors.Add("WebView2: " + ex.Message); }
        finally { view?.Dispose(); window?.Close(); }
        return new(AppExecutable.GetPath(), RuntimeInformation.FrameworkDescription, resources, alerts, version, bridge, errors);
    }
}
