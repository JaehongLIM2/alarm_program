using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WindowsAlarm.Models;
using WindowsAlarm.Services;
using ShutdownMode = WindowsAlarm.Services.ShutdownMode;

namespace WindowsAlarm;

public partial class MainWindow : Window
{
    private readonly AppStore _store;
    private readonly AlarmCoordinator _coordinator;
    private readonly DispatcherTimer _clock;
    private readonly AlarmPlaybackService _player;
    private ShutdownWindow? _shutdownWindow;
    private bool _closing;
    public MainWindow(AppStore store)
    {
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 16);
        _store = store;
        _player = new AlarmPlaybackService(store, createYouTube: () => new WebViewYouTubePlayer(store, this));
        _player.StatusChanged += message => AudioStatusText.Text = message;
        _coordinator = new AlarmCoordinator(store, new TaskSchedulerWakeService(store.Root), new WindowsPowerService(), _player, new WindowsActivityMonitor());
        _player.StopRequested += _coordinator.StopAlarm;
        _coordinator.Changed += RefreshStatus;
        _coordinator.AlarmStarted += () =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            AlarmPanel.Visibility = Visibility.Visible; Activate();
            Dispatcher.BeginInvoke(() => AlarmPanel.BringIntoView());
        };
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) =>
        {
            try { _coordinator.Tick(DateTimeOffset.Now); }
            catch (Exception ex) { _coordinator.CancelShutdown("타이머 처리 오류"); _coordinator.Report("타이머 처리 오류. 로그를 확인하세요.", ex); }
        };
        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(x => x.ToString("D2"));
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(x => x.ToString("D2"));
        var settings = _store.Load<AlarmSettings>("settings.json") ?? new AlarmSettings();
        HourBox.SelectedIndex = Math.Clamp(settings.Hour, 0, 23);
        MinuteBox.SelectedIndex = Math.Clamp(settings.Minute, 0, 59);
        YouTubeBox.Text = settings.YouTubeUrl;
        MusicBox.Text = settings.LocalMusicPath;
        MaxVolumeToggle.IsChecked = settings.MaximizeWindowsVolume;
        VolumeAfterStopBox.SelectedIndex = settings.VolumeAfterStop == AlarmVolumeAfterStop.SetZero ? 1 : 0;
        ShutdownToggle.IsChecked = settings.AutoShutdownEnabled;
        ShutdownMinutesBox.Text = settings.ShutdownAfterMinutes.ToString();
        StatusText.Text = _store.LoadWarning ?? "설정을 저장하고 알람을 테스트하세요.";
        Loaded += (_, _) => RunUi(() => { _coordinator.Recover(); _clock.Start(); });
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        Closing += (_, _) =>
        {
            _closing = true; _clock.Stop();
            SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
            _coordinator.Changed -= RefreshStatus;
            _coordinator.Dispose(); _shutdownWindow?.Dismiss();
        };
    }

    private AlarmSettings ReadSettings()
    {
        if (!int.TryParse(ShutdownMinutesBox.Text, out var minutes)) throw new InvalidOperationException("자동 종료 시간을 숫자로 입력하세요.");
        var settings = new AlarmSettings
        {
            Hour = HourBox.SelectedIndex, Minute = MinuteBox.SelectedIndex,
            YouTubeUrl = YouTubeBox.Text.Trim(), LocalMusicPath = MusicBox.Text.Trim(),
            AutoShutdownEnabled = ShutdownToggle.IsChecked == true, ShutdownAfterMinutes = minutes,
            MaximizeWindowsVolume = MaxVolumeToggle.IsChecked == true,
            VolumeAfterStop = VolumeAfterStopBox.SelectedIndex == 1 ? AlarmVolumeAfterStop.SetZero : AlarmVolumeAfterStop.RestorePrevious
        };
        if (settings.Validate() is { } error) throw new InvalidOperationException(error);
        return settings;
    }

    private void RunUi(Action action)
    {
        try { action(); }
        catch (Exception ex) { _store.Log("UI operation failed", ex); StatusText.Text = ex.Message; MessageBox.Show(this, ex.Message, "Windows Alarm", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => RunUi(() => { _store.Save("settings.json", ReadSettings()); StatusText.Text = "설정을 저장했습니다."; });
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "MP3 음악|*.mp3", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) MusicBox.Text = dialog.FileName;
    }
    private void Logs_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        var path = Path.Combine(_store.Root, "logs"); Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });
    private void More_Click(object sender, RoutedEventArgs e)
    {
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MoreButton.ContextMenu.IsOpen = true;
    }
    private void FontLicense_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/WindowsAlarm;component/Assets/Fonts/LICENSE.txt"))
            ?? throw new InvalidOperationException("글꼴 라이선스를 읽을 수 없습니다.");
        using var reader = new StreamReader(resource.Stream);
        var text = new System.Windows.Controls.TextBox
        {
            Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Margin = new Thickness(16), FontSize = 14
        };
        new Window
        {
            Title = "나눔스퀘어 · 글꼴 라이선스", Owner = this, Content = text,
            Width = 640, Height = 520, MinWidth = 420, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        }.ShowDialog();
    });
    private void PowerInfo_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        var power = new WindowsPowerService();
        new PowerStatusWindow(() =>
        {
            var snapshot = power.ReadStatus();
            _store.Log(PowerReadinessReport.Create(snapshot).Diagnostics);
            return snapshot;
        }) { Owner = this }.ShowDialog();
    });
    private void Test_Click(object sender, RoutedEventArgs e) => RunUi(() => _coordinator.TestAlarm(ReadSettings()));
    private void Hibernate_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        var settings = ReadSettings();
        var due = AlarmTiming.NextOccurrence(settings.Hour, settings.Minute, DateTimeOffset.Now, TimeZoneInfo.Local);
        _coordinator.Arm(settings, due, true, false);
    });
    private void Stop_Click(object sender, RoutedEventArgs e) => RunUi(_coordinator.StopAlarm);
    private void CancelWake_Click(object sender, RoutedEventArgs e) => RunUi(_coordinator.CancelWake);
    private void CancelShutdown_Click(object sender, RoutedEventArgs e) => RunUi(() => _coordinator.CancelShutdown());
    private void RefreshStatus()
    {
        if (_closing) return;
        ApplyReservationStatus(ReservationStatus.Create(_coordinator.Schedule, _coordinator.Shutdown.Mode, _coordinator.Shutdown.DueAt,
            _coordinator.WakeCancellationNeedsRetry, _coordinator.ShutdownCancellationNeedsRetry));
        StatusText.Text = _coordinator.Status;
        AlarmPanel.Visibility = _coordinator.IsRinging ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.IsEnabled = !_coordinator.IsRinging && !_coordinator.Shutdown.IsPending;
        if (_coordinator.Shutdown.Mode is ShutdownMode.Countdown or ShutdownMode.ActivityConfirmation)
        {
            if (_shutdownWindow is null) { _shutdownWindow = new ShutdownWindow(_coordinator.Shutdown) { Owner = this }; _shutdownWindow.Show(); }
            _shutdownWindow.Refresh(DateTimeOffset.Now);
        }
        else if (_shutdownWindow is not null)
        {
            var window = _shutdownWindow; _shutdownWindow = null; window.Dismiss();
        }
    }

    internal void ApplyReservationStatus(ReservationStatus status)
    {
        ScheduleText.Text = status.AlarmText;
        AlarmReservationRow.Visibility = status.AlarmText is null ? Visibility.Collapsed : Visibility.Visible;
        ShutdownText.Text = status.ShutdownText;
        ShutdownReservationRow.Visibility = status.ShutdownText is null ? Visibility.Collapsed : Visibility.Visible;
        ShutdownOutcomeText.Text = status.ShutdownOutcome;
        ShutdownOutcomeText.Visibility = status.ShutdownOutcome is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => { if (!_closing) _coordinator.OnResumeOrSessionChange(powerResume: true); });
        if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(() => { if (!_closing && _coordinator.Shutdown.IsPending) _coordinator.CancelShutdown("시스템 절전 진입"); });
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        _store.Log("Session switch: " + e.Reason);
        _coordinator.OnResumeOrSessionChange();
    });
    public void HandleExternalLaunch(bool wake)
    {
        if (_closing) return;
        if (wake) _coordinator.Tick(DateTimeOffset.Now);
        else { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Show(); Activate(); }
    }
    public void CancelForUnhandledError(Exception error)
    {
        _coordinator.CancelShutdown("예기치 않은 오류"); _coordinator.Report("오류가 발생했습니다. 로그를 확인하세요.", error);
    }
}
