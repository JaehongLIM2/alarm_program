using System.Windows;
using System.Diagnostics;
using System.IO;
using WindowsAlarm.Models;
using WindowsAlarm.Services;

namespace WindowsAlarm;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppStore? _store;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        try
        {
            var dataIndex = Array.IndexOf(e.Args, "--data-dir");
            if (dataIndex >= 0 && dataIndex + 1 >= e.Args.Length) throw new ArgumentException("--data-dir 경로가 필요합니다.");
            var root = dataIndex >= 0 ? e.Args[dataIndex + 1]
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsAlarm");
            _store = new AppStore(root);
            var wakeIndex = Array.IndexOf(e.Args, "--wake");
            if (wakeIndex >= 0)
            {
                var state = _store.Load<AlarmSchedule>("schedule.json");
                if (wakeIndex + 1 >= e.Args.Length || !Guid.TryParse(e.Args[wakeIndex + 1], out var id) || state?.Id != id)
                {
                    _store.Log("Ignoring stale/invalid scheduler launch"); Shutdown(); return;
                }
            }
            _instance = new SingleInstance(_store.Root);
            if (!_instance.IsOwner) { _instance.Notify(wakeIndex >= 0); Shutdown(); return; }
            if (wakeIndex >= 0)
            {
                // Keep the scheduler action short: WakeToRun otherwise holds the PC awake for the whole GUI lifetime.
                // Release ownership before the child starts so it can become the persistent interactive instance.
                _instance.Dispose(); _instance = null;
                var launch = new ProcessStartInfo(Path.ChangeExtension(typeof(App).Assembly.Location, ".exe")) { UseShellExecute = true };
                launch.ArgumentList.Add("--data-dir"); launch.ArgumentList.Add(_store.Root);
                using var child = Process.Start(launch) ?? throw new InvalidOperationException("알람 화면을 실행할 수 없습니다.");
                _store.Log("Scheduler launcher handed off to interactive alarm process");
                Shutdown(); return;
            }
            _store.Log("Application started");
            DispatcherUnhandledException += (_, args) =>
            {
                _store.Log("Unhandled dispatcher exception", args.Exception);
                if (MainWindow is MainWindow main) main.CancelForUnhandledError(args.Exception);
                args.Handled = true;
                MessageBox.Show("오류가 발생하여 자동 종료를 취소했습니다. 로그 폴더를 확인하세요.", "Windows Alarm", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => _store.Log("Fatal exception: " + args.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (_, args) => { _store.Log("Unobserved task error", args.Exception); args.SetObserved(); };
            var window = new MainWindow(_store);
            MainWindow = window;
            _instance.Listen(wake => Dispatcher.BeginInvoke(() => window.HandleExternalLaunch(wake)));
            window.Show();
        }
        catch (Exception ex)
        {
            _store?.Log("Startup failed", ex);
            MessageBox.Show("프로그램을 시작할 수 없습니다.\n" + ex.Message, "Windows Alarm", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
