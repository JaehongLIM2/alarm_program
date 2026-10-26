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
        ShutdownCheck.IsChecked = settings.AutoShutdownEnabled;
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
            AutoShutdownEnabled = ShutdownCheck.IsChecked == true, ShutdownAfterMinutes = minutes
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
    private void PowerInfo_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        var info = new WindowsPowerService().GetDiagnostics(); _store.Log(info);
        MessageBox.Show(this, info + "\n\n실제 S4 Wake는 하드웨어·펌웨어·전원 공급 조건에 따라 달라집니다. 2분 S4 테스트로 확인하세요.", "전원 상태");
    });
    private void Test_Click(object sender, RoutedEventArgs e) => RunUi(() => _coordinator.TestAlarm(ReadSettings()));
    private void Arm(bool hibernate, bool test) => RunUi(() =>
    {
        var settings = ReadSettings();
        var due = test ? DateTimeOffset.Now.AddMinutes(2) : AlarmTiming.NextOccurrence(settings.Hour, settings.Minute, DateTimeOffset.Now, TimeZoneInfo.Local);
        _coordinator.Arm(settings, due, hibernate, test);
    });
    private void Hibernate_Click(object sender, RoutedEventArgs e) => Arm(true, false);
    private void WakeTest_Click(object sender, RoutedEventArgs e) => Arm(false, true);
    private void S4Test_Click(object sender, RoutedEventArgs e) => Arm(true, true);
    private void Stop_Click(object sender, RoutedEventArgs e) => RunUi(_coordinator.StopAlarm);
    private void CancelWake_Click(object sender, RoutedEventArgs e) => RunUi(_coordinator.CancelWake);
    private void CancelShutdown_Click(object sender, RoutedEventArgs e) => RunUi(() => _coordinator.CancelShutdown());
    private void RefreshStatus()
    {
        if (_closing) return;
        ScheduleText.Text = _coordinator.Schedule is { } s ? $"{PhaseLabel(s.Phase)} · {s.DueAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}" : "예약 없음";
        StatusText.Text = _coordinator.Status;
        AlarmPanel.Visibility = _coordinator.IsRinging ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.IsEnabled = !_coordinator.IsRinging && !_coordinator.Shutdown.IsPending;
        ShutdownText.Text = _coordinator.Shutdown.Mode switch
        {
            ShutdownMode.Scheduled => $"자동 종료 예정 {_coordinator.Shutdown.DueAt?.LocalDateTime:yyyy-MM-dd HH:mm:ss}",
            ShutdownMode.Countdown => "자동 종료 카운트다운 진행 중",
            ShutdownMode.ActivityConfirmation => "사용 중 감지 · 응답이 없으면 자동 종료 취소",
            ShutdownMode.Requested => "Windows에 정상 종료를 요청했습니다. 이후 취소는 Windows 종료 화면에서 가능합니다.",
            ShutdownMode.Cancelled => "자동 종료 취소됨",
            _ => "자동 종료 예약 없음"
        };
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

    private static string PhaseLabel(AlarmPhase phase) => phase switch
    {
        AlarmPhase.Armed => "예약됨", AlarmPhase.Ringing => "알람 재생 중", AlarmPhase.Stopped => "알람 정지",
        AlarmPhase.Completed => "완료", AlarmPhase.Cancelled => "취소됨", AlarmPhase.Missed => "만료됨", _ => "확인 필요"
    };
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
