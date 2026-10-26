using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using WindowsAlarm.Models;
using WindowsAlarm.Services;
using ShutdownMode = WindowsAlarm.Services.ShutdownMode;

namespace WindowsAlarm.Tests;

internal static class Program
{
    private static int _passed, _failed;
    private static string _root = "";
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 7, 30, 0, TimeSpan.FromHours(9));
    [STAThread]
    private static int Main(string[] args)
    {
        _root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("artifacts", "test-data", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_root);
        Run("next alarm today/tomorrow/exact minute", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            Equal(Now, AlarmTiming.NextOccurrence(7, 30, Now.AddMinutes(-1), zone));
            Equal(Now.AddDays(1), AlarmTiming.NextOccurrence(7, 30, Now, zone));
            Equal(Now.AddDays(1), AlarmTiming.NextOccurrence(7, 30, Now.AddSeconds(1), zone));
        });
        Run("DST missing minute and repeated hour", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            var gap = AlarmTiming.NextOccurrence(2, 30, new DateTimeOffset(2026, 3, 8, 1, 0, 0, TimeSpan.FromHours(-8)), zone);
            Equal(3, TimeZoneInfo.ConvertTime(gap, zone).Hour);
            var overlap = AlarmTiming.NextOccurrence(1, 30, new DateTimeOffset(2026, 11, 1, 1, 45, 0, TimeSpan.FromHours(-7)), zone);
            Equal(TimeSpan.FromHours(-8), overlap.Offset);
        });
        Run("YouTube exact hosts and HTTPS validation", () =>
        {
            foreach (var url in new[] { "https://www.youtube.com/watch?v=abc", "https://youtu.be/abc", "https://m.youtube.com/shorts/abc", "https://www.youtube.com/live/abc" })
                Check(AlarmSettings.TryGetYouTubeUri(url, out _), url);
            foreach (var url in new[] { "file:///C:/x.exe", "http://youtu.be/abc", "https://youtube.com.attacker.com/watch?v=x", "https://youtube.com@evil.test/watch?v=x", "https://youtu.be:123/x", "https://youtube.com/", "https://youtube.com/watch?x=abc", "https://youtube.com/watch?v=" })
                Check(!AlarmSettings.TryGetYouTubeUri(url, out _), url);
        });
        Run("settings bounds, absent MP3 and non-MP3", () =>
        {
            Check(new AlarmSettings().Validate() is null);
            Check(new AlarmSettings { Hour = 24 }.Validate() is not null);
            Check(new AlarmSettings { ShutdownAfterMinutes = 0 }.Validate() is not null);
            Check(new AlarmSettings { LocalMusicPath = @"C:\missing\alarm.mp3" }.Validate() is not null);
            var file = Path.Combine(_root, "a.txt"); File.WriteAllText(file, "x");
            Check(new AlarmSettings { LocalMusicPath = file }.Validate() is not null);
        });
        Run("JSON roundtrip and corrupt-file preservation", () =>
        {
            var store = new AppStore(Path.Combine(_root, "json"));
            var settings = new AlarmSettings { Hour = 23, Minute = 59, AutoShutdownEnabled = true, YouTubeUrl = "https://youtu.be/abc" };
            store.Save("settings.json", settings); Equal(settings, store.Load<AlarmSettings>("settings.json"));
            Check(!File.Exists(Path.Combine(store.Root, "settings.json.tmp")));
            var preservedBefore = Directory.GetFiles(store.Root, "*.corrupt-*").Length;
            File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{broken");
            Check(store.Load<AlarmSettings>("settings.json") is null);
            Check(Directory.GetFiles(store.Root, "*.corrupt-*").Length == preservedBefore + 1);
        });
        Run("idle countdown shuts down once after 60 seconds", () =>
        {
            var (controller, power, _) = NewShutdown(); controller.Begin(Now, Now); controller.Tick(Now);
            Equal(ShutdownMode.Countdown, controller.Mode); Equal(0, power.ShutdownCalls);
            Advance(controller, Now, 59); Equal(0, power.ShutdownCalls);
            controller.Tick(Now.AddSeconds(60)); Equal(1, power.ShutdownCalls);
            controller.Tick(Now.AddSeconds(61)); Equal(1, power.ShutdownCalls);
        });
        Run("recent activity defaults to cancel", () =>
        {
            var (c, p, a) = NewShutdown(); a.Value = a.Value with { IdleTime = TimeSpan.FromSeconds(3) };
            c.Begin(Now, Now); c.Tick(Now); Equal(ShutdownMode.ActivityConfirmation, c.Mode);
            Advance(c, Now, 60); Equal(ShutdownMode.Cancelled, c.Mode); Equal(0, p.ShutdownCalls);
        });
        Run("input earlier in alarm session remains protected", () =>
        {
            var (c, p, a) = NewShutdown(); c.Begin(Now.AddSeconds(5), Now);
            a.Value = a.Value with { LastInputTick = 456, IdleTime = TimeSpan.FromHours(1) };
            Advance(c, Now, 5); Equal(ShutdownMode.ActivityConfirmation, c.Mode);
            Advance(c, Now.AddSeconds(5), 60); Equal(0, p.ShutdownCalls);
        });
        Run("input during countdown changes default to cancel", () =>
        {
            var (c, p, a) = NewShutdown(); c.Begin(Now, Now); c.Tick(Now);
            Advance(c, Now, 30); a.Value = a.Value with { LastInputTick = 456 }; c.Tick(Now.AddSeconds(31));
            Equal(ShutdownMode.ActivityConfirmation, c.Mode);
            Advance(c, Now.AddSeconds(31), 60); Equal(0, p.ShutdownCalls);
        });
        Run("last-moment fresh input check blocks shutdown", () =>
        {
            var (c, p, a) = NewShutdown();
            a.Script = call => call >= 63 ? a.Value with { LastInputTick = 456 } : a.Value;
            c.Begin(Now, Now); c.Tick(Now); Advance(c, Now, 60);
            Equal(ShutdownMode.ActivityConfirmation, c.Mode); Equal(0, p.ShutdownCalls);
        });
        Run("activity API failure or other session protects work", () =>
        {
            foreach (var failure in new[] { 0, 1, 2 })
            {
                var (c, p, a) = NewShutdown();
                if (failure == 0) a.Throw = true;
                if (failure == 1) a.Value = a.Value with { Reliable = false };
                if (failure == 2) a.Value = a.Value with { SafeSession = false };
                c.Begin(Now, Now); c.Tick(Now); Advance(c, Now, 60); Equal(0, p.ShutdownCalls); Equal(ShutdownMode.Cancelled, c.Mode);
            }
        });
        Run("resume delay and backward clock cancel shutdown", () =>
        {
            foreach (var jump in new[] { TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(-3) })
            {
                var (c, p, _) = NewShutdown(); c.Begin(Now, Now); c.Tick(Now + jump);
                Equal(ShutdownMode.Cancelled, c.Mode); Equal(0, p.ShutdownCalls);
            }
        });
        Run("cancel and explicit normal shutdown", () =>
        {
            var (c, p, _) = NewShutdown(); c.Begin(Now, Now); c.Cancel(); c.RequestNow(); Equal(0, p.ShutdownCalls);
            c.Begin(Now, Now); c.RequestNow(); c.RequestNow(); Equal(1, p.ShutdownCalls);
            p.FailShutdown = true; c.Begin(Now, Now); c.RequestNow(); Equal(ShutdownMode.Cancelled, c.Mode);
        });
        Run("scheduler failure never hibernates", () =>
        {
            using var h = NewHarness(); h.Scheduler.FailRegistration = true;
            Throws(() => h.Coordinator.Arm(new(), DateTimeOffset.Now.AddMinutes(2), true, false));
            Equal(0, h.Power.HibernateCalls); Equal(AlarmPhase.Cancelled, h.Coordinator.Schedule?.Phase);
        });
        Run("disabled hibernation rejects without arming", () =>
        {
            using var h = NewHarness(); h.Power.HibernateAllowed = false;
            Throws(() => h.Coordinator.Arm(new(), DateTimeOffset.Now.AddMinutes(2), true, false));
            Check(h.Coordinator.Schedule is null); Equal(0, h.Power.HibernateCalls);
        });
        Run("durable state and task exist before S4 call", () =>
        {
            using var h = NewHarness();
            h.Power.BeforeHibernate = () => { Check(h.Store.Load<AlarmSchedule>("schedule.json")?.Phase == AlarmPhase.Armed); Check(h.Scheduler.Exists()); };
            h.Coordinator.Arm(new(), DateTimeOffset.Now.AddMinutes(2), true, false); Equal(1, h.Power.HibernateCalls);
        });
        Run("due dispatch is idempotent; stop keeps shutdown; cancel completes", () =>
        {
            using var h = NewHarness(); h.Seed(Now); h.Coordinator.Recover(Now); h.Coordinator.Tick(Now);
            Equal(1, h.Playback.Starts); Check(h.Coordinator.Shutdown.IsPending);
            h.Coordinator.StopAlarm(); Equal(AlarmPhase.Stopped, h.Coordinator.Schedule?.Phase); Check(h.Coordinator.Shutdown.IsPending);
            h.Coordinator.CancelShutdown(); Equal(AlarmPhase.Completed, h.Coordinator.Schedule?.Phase); Equal(0, h.Power.ShutdownCalls);
        });
        Run("normal exit preserves future alarm and shutdown preference", () =>
        {
            var h = NewHarness(); h.Seed(Now.AddDays(1)); h.Coordinator.Recover(Now); h.Dispose();
            var saved = h.Store.Load<AlarmSchedule>("schedule.json"); Equal(AlarmPhase.Armed, saved?.Phase); Check(saved?.ShutdownCancelled == false); Check(h.Scheduler.Exists());
        });
        Run("wake cancellation and pre-alarm shutdown cancellation", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddMinutes(1)); h.Coordinator.Recover(Now);
            h.Coordinator.CancelShutdown(); h.Coordinator.Tick(Now.AddMinutes(1)); Check(!h.Coordinator.Shutdown.IsPending);
            h.Coordinator.StopAlarm(); h.Coordinator.Arm(new(), DateTimeOffset.Now.AddMinutes(2), false, false);
            h.Coordinator.CancelWake(); Equal(AlarmPhase.Cancelled, h.Coordinator.Schedule?.Phase); Check(!h.Scheduler.Exists());
        });
        Run("immediate and scheduled tests never schedule shutdown", () =>
        {
            using var h = NewHarness(); h.Coordinator.TestAlarm(new AlarmSettings { AutoShutdownEnabled = true }); Check(!h.Coordinator.Shutdown.IsPending);
            h.Coordinator.StopAlarm();
            var s = h.Seed(Now) with { IsTest = true }; h.Store.Save("schedule.json", s); h.Coordinator.Recover(Now);
            Check(!h.Coordinator.Shutdown.IsPending); Equal(0, h.Power.ShutdownCalls);
        });
        Run("crashed ringing alarm resumes audio but cancels shutdown", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddMinutes(-5), AlarmPhase.Ringing); h.Coordinator.Recover(Now);
            Equal(1, h.Playback.Starts); Check(h.Coordinator.Schedule?.ShutdownCancelled == true); Check(!h.Coordinator.Shutdown.IsPending);
        });
        Run("stopped recovery never resumes shutdown or audio", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddMinutes(-5), AlarmPhase.Stopped); h.Coordinator.Recover(Now);
            Equal(0, h.Playback.Starts); Check(!h.Coordinator.Shutdown.IsPending); Equal(AlarmPhase.Completed, h.Coordinator.Schedule?.Phase);
        });
        Run("missed alarm grace period and expiration", () =>
        {
            using var late = NewHarness(); late.Seed(Now.AddMinutes(-10)); late.Coordinator.Recover(Now); Equal(1, late.Playback.Starts);
            using var expired = NewHarness(); expired.Seed(Now.AddHours(-3)); expired.Coordinator.Recover(Now);
            Equal(0, expired.Playback.Starts); Equal(AlarmPhase.Missed, expired.Coordinator.Schedule?.Phase);
        });
        Run("missing settings snapshot and deleted MP3 preserve fallback", () =>
        {
            using var h = NewHarness(); var schedule = h.Seed(Now) with { Settings = null! }; h.Store.Save("schedule.json", schedule); h.Coordinator.Recover(Now);
            Check(h.Coordinator.Schedule?.Settings is not null); Equal(1, h.Playback.Starts); Check(!h.Coordinator.Shutdown.IsPending);
            schedule = h.Seed(Now) with { Settings = new AlarmSettings { LocalMusicPath = @"C:\deleted\alarm.mp3", YouTubeUrl = "invalid" } };
            h.Store.Save("schedule.json", schedule); h.Coordinator.Recover(Now);
            Equal(2, h.Playback.Starts); Equal(schedule.Settings, h.Playback.LastSettings);
        });
        Run("runtime state-write failure does not suppress audio", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddSeconds(1)); h.Coordinator.Recover(Now);
            using (var locked = new FileStream(Path.Combine(h.Store.Root, "schedule.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                h.Coordinator.Tick(Now.AddSeconds(1));
            Equal(1, h.Playback.Starts);
        });
        Run("resume notification cancels pending shutdown", () =>
        {
            using var h = NewHarness(); h.Seed(DateTimeOffset.Now); h.Coordinator.Recover();
            Check(h.Coordinator.Shutdown.IsPending); h.Coordinator.OnResumeOrSessionChange(); Check(!h.Coordinator.Shutdown.IsPending);
        });
        Run("expected wake notification preserves newly started shutdown", () =>
        {
            using var h = NewHarness(); h.Seed(DateTimeOffset.Now); h.Coordinator.Recover();
            h.Coordinator.OnResumeOrSessionChange(powerResume: true); Check(h.Coordinator.Shutdown.IsPending);
        });
        Run("YouTube launch failure still triggers delayed local fallback", () =>
        {
            var local = new FakeLocalOutput(); var delay = new TaskCompletionSource();
            using var launched = new ManualResetEventSlim();
            using var playback = new AlarmPlaybackService(new AppStore(Path.Combine(_root, "fallback")), local,
                _ => { launched.Set(); throw new InvalidOperationException("offline/browser error"); },
                (duration, token) => { Equal(TimeSpan.FromSeconds(5), duration); return delay.Task.WaitAsync(token); });
            var pending = playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/abc" });
            Check(launched.Wait(TimeSpan.FromSeconds(2))); Equal(0, local.Starts);
            delay.SetResult(); pending.GetAwaiter().GetResult(); Equal(1, local.Starts);
        });
        Run("stop during fallback delay prevents late audio", () =>
        {
            var local = new FakeLocalOutput(); var delay = new TaskCompletionSource();
            using var playback = new AlarmPlaybackService(new AppStore(Path.Combine(_root, "cancel-fallback")), local,
                _ => { }, (_, token) => delay.Task.WaitAsync(token));
            var pending = playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/abc" });
            playback.Stop(); delay.SetResult(); pending.GetAwaiter().GetResult(); Equal(0, local.Starts);
        });
        Run("generated emergency PCM WAV has valid length and samples", () =>
        {
            var wave = LocalAlarmPlayer.CreateEmergencyWave();
            Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4)); Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
            Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4)); Equal(wave.Length - 44, BitConverter.ToInt32(wave, 40));
            Check(wave.Skip(44).Any(b => b != 0));
        });
        Run("native struct layout and read-only Windows diagnostics", () =>
        {
            Equal(76, Marshal.SizeOf<WindowsPowerService.PowerCapabilities>());
            Equal(22, Marshal.OffsetOf<WindowsPowerService.PowerCapabilities>(nameof(WindowsPowerService.PowerCapabilities.HiberFileType)).ToInt32());
            Console.WriteLine("  " + new WindowsPowerService().GetDiagnostics());
            var input = new WindowsActivityMonitor().Read(); Check(input.Reliable); Check(input.IdleTime >= TimeSpan.Zero);
            Console.WriteLine($"  Input API reliable={input.Reliable}, safe console={input.SafeSession}");
        });
        Run("scheduler XML encodes user, safe flags, UTC and escaped paths", () =>
        {
            var scheduler = new TaskSchedulerWakeService(Path.Combine(_root, "공백 & space"));
            var xml = XDocument.Parse(scheduler.BuildXml(new AlarmSchedule { DueAt = Now })); XNamespace ns = xml.Root!.Name.Namespace;
            Equal("true", xml.Descendants(ns + "WakeToRun").Single().Value);
            Equal("InteractiveToken", xml.Descendants(ns + "LogonType").Single().Value);
            Equal("LeastPrivilege", xml.Descendants(ns + "RunLevel").Single().Value);
            Equal("false", xml.Descendants(ns + "AllowHardTerminate").Single().Value);
            Check(xml.Descendants(ns + "StartBoundary").All(element => element.Value.EndsWith('Z')));
            Check(xml.Descendants(ns + "EndBoundary").Any()); Check(xml.Descendants(ns + "LogonTrigger").Any());
            Check(xml.Descendants(ns + "Arguments").Single().Value.Contains("공백 & space", StringComparison.Ordinal));
            Equal("\"C:\\with space\\\\\"", TaskSchedulerWakeService.QuoteArgument("C:\\with space\\"));
        });
        var app = new Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/WindowsAlarm;component/Styles.xaml") });
        Run("countdown window close defaults to cancellation", () =>
        {
            var (controller, power, _) = NewShutdown(); controller.Begin(Now, Now); controller.Tick(Now);
            var window = new ShutdownWindow(controller); window.Refresh(Now); window.Show(); Pump(TimeSpan.FromMilliseconds(100)); window.Close();
            Equal(ShutdownMode.Cancelled, controller.Mode); Equal(0, power.ShutdownCalls);
        });
        Run("main WPF window renders, saves edited settings and closes safely", () =>
        {
            var store = new AppStore(Path.Combine(_root, "ui"));
            var window = new MainWindow(store); window.Show(); Pump(TimeSpan.FromMilliseconds(250));
            Check(window.IsVisible); Equal("Windows Alarm", window.Title); Check(window.FindName("HourBox") is ComboBox);
            ((ComboBox)window.FindName("HourBox")).SelectedIndex = 9;
            ((ComboBox)window.FindName("MinuteBox")).SelectedIndex = 40;
            ((TextBox)window.FindName("YouTubeBox")).Text = "https://youtu.be/abc";
            ((CheckBox)window.FindName("ShutdownCheck")).IsChecked = true;
            ((TextBox)window.FindName("ShutdownMinutesBox")).Text = "45";
            FindVisuals<Button>(window).Single(button => Equals(button.Content, "설정 저장")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = store.Load<AlarmSettings>("settings.json"); Equal(9, saved?.Hour); Equal(40, saved?.Minute); Check(saved?.AutoShutdownEnabled == true);
            window.UpdateLayout();
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(_root, "main-window.png"))) encoder.Save(output);
            window.Close();
        });
        if (args.Contains("--integration")) Run("muted WPF audio decoder opens and advances PCM", () =>
        {
            var file = Path.Combine(_root, "media.wav"); File.WriteAllBytes(file, LocalAlarmPlayer.CreateEmergencyWave());
            var opened = false; Exception? failure = null; var player = new MediaPlayer { Volume = 0, IsMuted = true };
            player.MediaOpened += (_, _) => { opened = true; player.Play(); };
            player.MediaFailed += (_, e) => failure = e.ErrorException;
            try
            {
                player.Open(new Uri(file)); player.Play(); Pump(TimeSpan.FromSeconds(3));
                Check(failure is null, failure?.Message); Check(opened, "MediaOpened not received"); Check(player.Position > TimeSpan.Zero, "Audio clock did not advance");
            }
            finally { player.Close(); }
        });
        if (args.Contains("--integration")) Run("real Task Scheduler register/read/remove (no S4/shutdown)", SchedulerIntegration);
        app.Shutdown();
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed. Data: {_root}");
        return _failed == 0 ? 0 : 1;
    }

    private static void SchedulerIntegration()
    {
        var scheduler = new TaskSchedulerWakeService(Path.Combine(_root, "integration"));
        try
        {
            scheduler.Register(new AlarmSchedule { DueAt = DateTimeOffset.Now.AddMinutes(10), IsTest = true }); Check(scheduler.Exists());
            var xml = scheduler.ReadRegisteredXml(); Check(xml?.Contains("<WakeToRun>true</WakeToRun>", StringComparison.Ordinal) == true);
            Console.WriteLine("  Registered and verified " + scheduler.TaskName);
        }
        catch
        {
            try { scheduler.Remove(); } catch (Exception cleanup) { Console.WriteLine("  Task cleanup: " + cleanup.Message); }
            throw;
        }
        scheduler.Remove();
        Check(!scheduler.Exists());
    }
    private static Harness NewHarness() => new(_root);
    private static (ShutdownController, FakePower, FakeActivity) NewShutdown()
    {
        var power = new FakePower(); var activity = new FakeActivity(); return (new ShutdownController(power, activity, _ => { }), power, activity);
    }
    private static void Advance(ShutdownController controller, DateTimeOffset start, int seconds) { for (var i = 1; i <= seconds; i++) controller.Tick(start.AddSeconds(i)); }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindVisuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }
    private static void Check(bool value, string? message = null) { if (!value) throw new InvalidOperationException(message ?? "Assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}"); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Expected rejection"); }
}
