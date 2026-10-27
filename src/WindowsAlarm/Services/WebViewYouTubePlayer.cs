using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WindowsAlarm.Services;

public sealed class WebViewYouTubePlayer : IYouTubeAlarmPlayer
{
    public const string HostName = "windows-alarm.local";
    private readonly AppStore _store;
    private readonly Window? _owner;
    private readonly Dispatcher _dispatcher;
    private WebView2? _view;
    private PlayerWindow? _window;
    private CancellationTokenSource? _lifetime;
    private readonly string _session = Guid.NewGuid().ToString("N");
    private string? _pageUri;
    private bool _stopped;
    private AlertPcmPlayer? _pcm;
    internal bool SuppressSoundForTesting { get; init; }
    internal bool HasDecodedSignalForTesting => _pcm?.HasDecodedSignal == true;
    internal bool IsRenderingForTesting => _pcm?.IsRendering == true;
    internal long PcmPacketsForTesting => _pcm?.Packets ?? 0;
    // Deterministic SDK tests replace only the remote script response, while using the real Runtime and page/bridge.
    internal Action<CoreWebView2>? ConfigureForTesting { get; init; }
    public event Action<YouTubeSample>? SampleReceived;
    public event Action<string>? Failed;
    public event Action? StopRequested;

    public WebViewYouTubePlayer(AppStore store, Window? owner = null) { _store = store; _owner = owner; _dispatcher = owner?.Dispatcher ?? Dispatcher.CurrentDispatcher; }

    public Task StartAsync(string videoId, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => StartOnDispatcherAsync(videoId, cancellationToken)).Task.Unwrap();

    private async Task StartOnDispatcherAsync(string videoId, CancellationToken cancellationToken)
    {
        if (_stopped) throw new OperationCanceledException(cancellationToken);
        if (_view is not null) throw new InvalidOperationException("YouTube 플레이어는 한 번만 시작할 수 있습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _lifetime.Token;
        _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); // Throws if Runtime is unavailable; caller falls back locally.
        var content = Path.Combine(_store.Root, "youtube-content");
        Directory.CreateDirectory(content);
        using (var resource = typeof(WebViewYouTubePlayer).Assembly.GetManifestResourceStream("WindowsAlarm.Assets.YouTubePlayer.html")
            ?? throw new InvalidOperationException("내장 YouTube 페이지를 읽을 수 없습니다."))
        using (var reader = new StreamReader(resource, Encoding.UTF8))
            File.WriteAllText(Path.Combine(content, "player.html"), await reader.ReadToEndAsync(token), new UTF8Encoding(false));
        token.ThrowIfCancellationRequested();
        _view = new WebView2();
        _window = new PlayerWindow(_view, () => StopRequested?.Invoke(), _owner);
        _window.Show();
        // An unattended alarm cannot supply the click normally required for audible autoplay.
        // Apply this only to our dedicated WebView2 profile, leaving other browsers unchanged.
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling");
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_store.Root, "webview2"), options).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        var view = _view ?? throw new OperationCanceledException(token);
        await view.EnsureCoreWebView2Async(environment).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (_stopped) throw new OperationCanceledException(token);
        var core = view.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.SetVirtualHostNameToFolderMapping(HostName, content, CoreWebView2HostResourceAccessKind.DenyCors);
        _pageUri = $"https://{HostName}/player.html?v={Uri.EscapeDataString(videoId)}&session={_session}";
        core.NavigationStarting += (_, e) =>
        {
            if (_stopped || e.Uri != _pageUri) { e.Cancel = true; if (!_stopped) Failed?.Invoke("YouTube 플레이어가 다른 페이지로 이동하려고 했습니다."); }
        };
        core.NavigationCompleted += (_, e) => { if (!_stopped && !e.IsSuccess) Failed?.Invoke("YouTube 페이지를 로드할 수 없습니다: " + e.WebErrorStatus); };
        core.ProcessFailed += (_, _) => { if (!_stopped) Failed?.Invoke("내장 YouTube 프로세스가 종료되었습니다."); };
        core.WebMessageReceived += OnWebMessage;
        using var relayResource = typeof(WebViewYouTubePlayer).Assembly.GetManifestResourceStream("WindowsAlarm.Assets.YouTubeAudioRelay.js")
            ?? throw new InvalidOperationException("YouTube 오디오 전달 코드를 읽을 수 없습니다.");
        using var relayReader = new StreamReader(relayResource, Encoding.UTF8);
        var relayScript = (await relayReader.ReadToEndAsync(token)).Replace("__ALARM_SESSION__", _session, StringComparison.Ordinal);
        token.ThrowIfCancellationRequested();
        _pcm = new AlertPcmPlayer(SuppressSoundForTesting);
        _pcm.Failed += reason => _dispatcher.BeginInvoke(() => { if (!_stopped) Failed?.Invoke(reason); });
        core.FrameCreated += (_, e) =>
        {
            var frame = e.Frame;
            frame.WebMessageReceived += (_, message) =>
            {
                if (_stopped || !Uri.TryCreate(message.Source, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    uri.Host != "www.youtube.com" || uri.AbsolutePath != "/embed/" + videoId) return;
                if (!YouTubePcmMessage.TryRead(message.WebMessageAsJson, _session, out var bytes, out var failure, out var ready)) return;
                if (bytes is not null) _pcm?.Push(bytes);
                if (ready) _store.Log("YouTube PCM relay running; output category=Alerts");
                if (failure is not null) Failed?.Invoke(failure);
            };
            async Task InstallRelay()
            {
                try { if (!_stopped) await frame.ExecuteScriptAsync(relayScript); }
                catch (Exception ex) { if (!_stopped) _store.Log("YouTube frame audio relay injection failed", ex); }
            }
            frame.DOMContentLoaded += (_, _) => _ = InstallRelay();
            frame.NavigationCompleted += (_, _) => _ = InstallRelay();
        };
        core.IsMuted = false;
        ConfigureForTesting?.Invoke(core);
        core.Navigate(_pageUri);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_stopped || e.Source != _pageUri || _view?.CoreWebView2 is not { } core) return;
        try
        {
            if (!YouTubeMessage.TryRead(e.WebMessageAsJson, _session, !core.IsMuted && _pcm?.HasRecentSignal == true, out var message)) return;
            if (message!.Sample is { } sample) SampleReceived?.Invoke(sample);
            if (message.Failure is { } failure) Failed?.Invoke(failure);
        }
        catch (Exception ex) { _store.Log("YouTube message processing failed", ex); if (!_stopped) Failed?.Invoke("YouTube 재생 상태를 확인할 수 없습니다."); }
    }

    public void Stop()
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.Invoke(Stop); return; }
        if (_stopped) return;
        _stopped = true; _lifetime?.Cancel();
        var pcm = _pcm; _pcm = null;
        try { if (pcm is not null) { _store.Log($"YouTube PCM relay stopped; packets={pcm.Packets}"); pcm.Dispose(); } }
        catch (Exception ex) { _store.Log("YouTube Alerts output disposal failed", ex); }
        var view = _view; _view = null;
        // Silence first, then destroy the controller. Delayed API/initialization events can never revive it.
        try { if (view?.CoreWebView2 is { } core) { core.IsMuted = true; core.Stop(); } }
        catch (Exception ex) { _store.Log("YouTube mute/navigation stop failed; disposing controller", ex); }
        try { view?.Dispose(); } catch (Exception ex) { _store.Log("YouTube controller disposal failed", ex); }
        var window = _window; _window = null;
        try { window?.Dismiss(); }
        finally { _lifetime?.Dispose(); _lifetime = null; }
    }
    public void Dispose() => Stop();

    private sealed class PlayerWindow : Window
    {
        private bool _closing, _dismissed;
        public PlayerWindow(WebView2 view, Action stop, Window? owner)
        {
            SetResourceReference(StyleProperty, "AppWindowStyle");
            Title = "YouTube 알람"; Width = 680; Height = 460; MinWidth = 520; MinHeight = 400;
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            if (owner is { IsVisible: true }) Owner = owner;
            var panel = new DockPanel();
            var controls = new StackPanel { Margin = new Thickness(20) };
            controls.Children.Add(new TextBlock { Text = "재생이 확인되면 YouTube만 유지합니다. 확인 실패 시 이 플레이어를 종료하고 로컬 음악으로 전환합니다.", TextWrapping = TextWrapping.Wrap });
            var button = new Button { Content = "알람 정지", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) };
            button.SetResourceReference(StyleProperty, "StopButtonStyle");
            button.Click += (_, _) => stop(); controls.Children.Add(button);
            DockPanel.SetDock(controls, Dock.Bottom); panel.Children.Add(controls); panel.Children.Add(view); Content = panel;
            Closing += (_, _) => { _closing = true; if (!_dismissed) stop(); };
        }
        public void Dismiss() { _dismissed = true; if (!_closing) Close(); }
    }
}
