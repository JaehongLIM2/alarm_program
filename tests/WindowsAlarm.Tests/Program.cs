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
using Microsoft.Web.WebView2.Core;
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
            foreach (var url in new[] { "https://www.youtube.com/watch?v=M7lc1UVf-VE", "https://youtu.be/M7lc1UVf-VE", "https://m.youtube.com/shorts/M7lc1UVf-VE", "https://www.youtube.com/live/M7lc1UVf-VE" })
                Check(AlarmSettings.TryGetYouTubeUri(url, out _), url);
            foreach (var url in new[] { "file:///C:/x.exe", "http://youtu.be/M7lc1UVf-VE", "https://youtube.com.attacker.com/watch?v=x", "https://youtube.com@evil.test/watch?v=x", "https://youtu.be:123/x", "https://youtube.com/", "https://youtube.com/watch?x=abc", "https://youtube.com/watch?v=" })
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
            var settings = new AlarmSettings { Hour = 23, Minute = 59, AutoShutdownEnabled = true, YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" };
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
        Run("input during playback startup remains protected after five minutes idle", () =>
        {
            using var h = NewHarness();
            var schedule = h.Seed(Now) with { Settings = new AlarmSettings { AutoShutdownEnabled = true, ShutdownAfterMinutes = 6 } };
            h.Store.Save("schedule.json", schedule);
            h.Playback.OnStart = () => h.Activity.Value = h.Activity.Value with { LastInputTick = 456, IdleTime = TimeSpan.Zero };
            h.Coordinator.Recover(Now);
            for (var second = 1; second <= 360; second++) { h.Activity.Value = h.Activity.Value with { IdleTime = TimeSpan.FromSeconds(second) }; h.Coordinator.Tick(Now.AddSeconds(second)); }
            Equal(ShutdownMode.ActivityConfirmation, h.Coordinator.Shutdown.Mode); Equal(0, h.Power.ShutdownCalls);
        });
        Run("shutdown cancellation during playback startup is never reenabled", () =>
        {
            using var h = NewHarness(); h.Seed(Now);
            h.Playback.OnStart = () => h.Coordinator.CancelShutdown();
            h.Coordinator.Recover(Now);
            Check(!h.Coordinator.Shutdown.IsPending); Check(h.Coordinator.Schedule?.ShutdownCancelled == true);
        });
        Run("stopping audio during startup retains consistent pending-shutdown state", () =>
        {
            using var h = NewHarness(); h.Seed(Now); h.Playback.OnStart = h.Coordinator.StopAlarm;
            h.Coordinator.Recover(Now);
            Equal(AlarmPhase.Stopped, h.Coordinator.Schedule?.Phase); Check(h.Coordinator.Shutdown.IsPending); Check(!h.Coordinator.IsRinging);
        });
        Run("stop during shutdown initialization cannot start delayed audio", () =>
        {
            using var h = NewHarness(); h.Seed(Now);
            h.Coordinator.Changed += () =>
            {
                if (h.Coordinator.Shutdown.IsPending && h.Coordinator.IsRinging) h.Coordinator.StopAlarm();
            };
            h.Coordinator.Recover(Now);
            Equal(0, h.Playback.Starts); Equal(AlarmPhase.Stopped, h.Coordinator.Schedule?.Phase); Check(h.Coordinator.Shutdown.IsPending);
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
        Run("wake cancellation reports locked state and retry persists cancellation", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddMinutes(1)); h.Coordinator.Recover(Now);
            using (var locked = new FileStream(Path.Combine(h.Store.Root, "schedule.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                h.Coordinator.CancelWake();
                Equal(AlarmPhase.Cancelled, h.Coordinator.Schedule?.Phase); Check(!h.Scheduler.Exists());
                Check(h.Coordinator.Status.Contains("저장하지 못", StringComparison.Ordinal), "Persistent cancellation failure was hidden.");
            }
            h.Coordinator.CancelWake();
            Equal(AlarmPhase.Cancelled, h.Store.Load<AlarmSchedule>("schedule.json")?.Phase);
            using var recovered = new AlarmCoordinator(h.Store, h.Scheduler, h.Power, new FakePlayback(), h.Activity);
            recovered.Recover(Now); Check(!h.Scheduler.Exists());
        });
        Run("wake cancellation reports scheduler deletion failure and permits retry", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddMinutes(1)); h.Coordinator.Recover(Now); h.Scheduler.FailRemoval = true;
            h.Coordinator.CancelWake(); Check(h.Scheduler.Exists());
            Check(h.Coordinator.Status.Contains("삭제하지 못", StringComparison.Ordinal), "Task deletion failure was hidden.");
            h.Scheduler.FailRemoval = false; h.Coordinator.CancelWake(); Check(!h.Scheduler.Exists());
        });
        Run("pre-alarm shutdown cancellation reports persistence failure and retry survives recovery", () =>
        {
            using var h = NewHarness(); h.Seed(Now.AddMinutes(1)); h.Coordinator.Recover(Now);
            using (var locked = new FileStream(Path.Combine(h.Store.Root, "schedule.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                h.Coordinator.CancelShutdown(); Check(h.Coordinator.Schedule?.ShutdownCancelled == true);
                Check(h.Coordinator.Status.Contains("저장하지 못", StringComparison.Ordinal), "Shutdown cancellation persistence failure was hidden.");
            }
            h.Coordinator.CancelShutdown();
            using var recovered = new AlarmCoordinator(h.Store, h.Scheduler, h.Power, new FakePlayback(), h.Activity);
            recovered.Recover(Now.AddMinutes(1)); Check(!recovered.Shutdown.IsPending);
        });
        Run("immediate and scheduled tests never schedule shutdown", () =>
        {
            using var h = NewHarness(); h.Coordinator.TestAlarm(new AlarmSettings { AutoShutdownEnabled = true }); Check(!h.Coordinator.Shutdown.IsPending);
            h.Coordinator.StopAlarm();
            var s = h.Seed(Now) with { IsTest = true }; h.Store.Save("schedule.json", s); h.Coordinator.Recover(Now);
            Check(!h.Coordinator.Shutdown.IsPending); Equal(0, h.Power.ShutdownCalls);
        });
        Run("display wake failure does not suppress alarm playback", () =>
        {
            using var h = NewHarness();
            h.Power.OnWakeDisplay = () => { Check(h.Power.Awake); throw new InvalidOperationException("display unavailable"); };
            h.Seed(Now); h.Coordinator.Recover(Now);
            Equal(1, h.Playback.Starts); Check(h.Coordinator.IsRinging);
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
        PlaybackTests();
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
            ((TextBox)window.FindName("YouTubeBox")).Text = "https://youtu.be/M7lc1UVf-VE";
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
        if (args.Contains("--integration")) Run("native Alerts audio advances, loops, stops and ignores stale callbacks", () =>
        {
            var store = new AppStore(Path.Combine(_root, "alerts-audio"));
            var file = Path.Combine(store.Root, "test.wav"); File.WriteAllBytes(file, LocalAlarmPlayer.CreateEmergencyWave());
            using var player = new LocalAlarmPlayer(store) { SuppressSoundForTesting = true };
            player.Start(file);
            PumpUntil(() => player.NativePlayerForTesting?.PlaybackSession.Position > TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(8));
            Equal(Windows.Media.Playback.MediaPlayerAudioCategory.Alerts, player.NativePlayerForTesting!.AudioCategory);
            Check(player.NativePlayerForTesting.IsLoopingEnabled);
            player.Stop(); Pump(TimeSpan.FromMilliseconds(200));
            Check(!player.IsActive); Check(player.NativePlayerForTesting is null);
            player.Start(null);
            PumpUntil(() => player.NativePlayerForTesting?.PlaybackSession.Position > TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(8));
            Equal(Windows.Media.Playback.MediaPlayerAudioCategory.Alerts, player.NativePlayerForTesting!.AudioCategory);
            player.Stop(); Pump(TimeSpan.FromMilliseconds(200)); Check(!player.IsActive);
        });
        if (args.Contains("--integration")) Run("real Task Scheduler register/read/remove (no S4/shutdown)", SchedulerIntegration);
        if (args.Contains("--webview-integration")) Run("real WebView2 bridge and exclusive fallback with deterministic player API", () => WithDispatcherContext(WebViewIntegration));
        if (args.Contains("--scheduler-process-integration")) Run("scheduler launcher exits while GUI lives and reopens persisted future alarm", SchedulerProcessIntegration);
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
    private static void SchedulerProcessIntegration()
    {
        var store = new AppStore(Path.Combine(_root, "scheduler-process", Guid.NewGuid().ToString("N")));
        var schedule = new AlarmSchedule { DueAt = DateTimeOffset.Now.AddMinutes(15), IsTest = true, ShutdownCancelled = true };
        store.Save("schedule.json", schedule);
        var scheduler = new TaskSchedulerWakeService(store.Root);
        var expectedExe = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
        dynamic? service = null; dynamic? folder = null; dynamic? task = null;
        var childIds = new HashSet<int>();
        try
        {
            scheduler.Register(schedule);
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!; service.Connect(); folder = service.GetFolder("\\"); task = folder.GetTask(scheduler.TaskName);
            for (var launch = 0; launch < 2; launch++)
            {
                object running = task.Run(null); Marshal.FinalReleaseComObject(running);
                var childId = 0; IntPtr window = IntPtr.Zero;
                PumpUntil(() =>
                {
                    var logs = Path.Combine(store.Root, "logs");
                    if (!Directory.Exists(logs)) return false;
                    foreach (var log in Directory.GetFiles(logs, "*.log"))
                    {
                        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(reader.ReadToEnd(), @"Application started; process=(\d+)"))
                        {
                            var candidate = int.Parse(match.Groups[1].Value);
                            if (childIds.Contains(candidate)) continue;
                            var found = ProcessWindow.Find(candidate);
                            if (found != IntPtr.Zero) { childId = candidate; window = found; return true; }
                        }
                    }
                    return false;
                }, TimeSpan.FromSeconds(20));
                childIds.Add(childId);
                using var child = System.Diagnostics.Process.GetProcessById(childId);
                Check(string.Equals(child.MainModule?.FileName, expectedExe, StringComparison.OrdinalIgnoreCase), "Unexpected scheduled executable.");
                PumpUntil(() => (int)task.State == 3, TimeSpan.FromSeconds(10));
                Check(!child.HasExited, "GUI exited with the launcher.");
                var recovered = store.Load<AlarmSchedule>("schedule.json");
                Equal(schedule.Id, recovered?.Id); Equal(AlarmPhase.Armed, recovered?.Phase); Check(recovered?.IsTest == true);
                Check(ProcessWindow.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero));
                Check(child.WaitForExit(6000), "Scheduled GUI did not close after WM_CLOSE.");
                Check(scheduler.Exists(), "Closing the GUI removed the future alarm.");
            }
            Console.WriteLine("  Two scheduled GUI launches, persistent state and completed launcher task verified without audio/S4/shutdown.");
        }
        finally
        {
            // Stop only this unique scratch task; never terminate a task engine or another app profile.
            try { if (task is not null) task.Stop(0); } catch (Exception) { }
            var logs = Path.Combine(store.Root, "logs");
            if (Directory.Exists(logs))
                foreach (var log in Directory.GetFiles(logs, "*.log"))
                {
                    try
                    {
                        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(log), @"Application started; process=(\d+)"))
                            childIds.Add(int.Parse(match.Groups[1].Value));
                    }
                    catch (IOException) { }
                }
            foreach (var childId in childIds)
            {
                try
                {
                    using var child = System.Diagnostics.Process.GetProcessById(childId);
                    if (string.Equals(child.MainModule?.FileName, expectedExe, StringComparison.OrdinalIgnoreCase))
                    {
                        var window = ProcessWindow.Find(childId);
                        if (window != IntPtr.Zero) ProcessWindow.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                        if (!child.WaitForExit(3000)) child.Kill();
                    }
                }
                catch (ArgumentException) { }
            }
            if (task is not null) Marshal.FinalReleaseComObject(task);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            if (service is not null) Marshal.FinalReleaseComObject(service);
            scheduler.Remove();
        }
        Check(!scheduler.Exists(), "Scratch task was not removed.");
    }

    private static class ProcessWindow
    {
        private delegate bool Callback(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int length);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        public static IntPtr Find(int process)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner); if (owner != process) return true;
                var title = new StringBuilder(256); GetWindowText(window, title, 256);
                if (title.ToString() != "Windows Alarm") return true;
                result = window; return false;
            }, IntPtr.Zero);
            return result;
        }
    }
    private static void WebViewIntegration()
    {
        var store = new AppStore(Path.Combine(_root, "webview"));
        var local = new FakeLocalOutput(); var samples = new List<YouTubeSample>(); var failures = new List<string>();
        var referrerSeen = false;
        local.OnStart = () => Check(!Application.Current.Windows.OfType<Window>().Any(w => w.Title == "YouTube 알람" && w.IsVisible), "YouTube window remained open when local playback started.");
        using var playback = new AlarmPlaybackService(store, local, () =>
        {
            var player = new WebViewYouTubePlayer(store)
            {
                ConfigureForTesting = core =>
                {
                    core.AddWebResourceRequestedFilter("https://www.youtube.com/iframe_api", CoreWebView2WebResourceContext.Script);
                    core.WebResourceRequested += (_, e) =>
                    {
                        referrerSeen = e.Request.Headers.GetHeader("Referer").Contains(WebViewYouTubePlayer.HostName, StringComparison.Ordinal);
                        const string script = "window.YT={Player:function(id,o){var t=performance.now();this.getPlayerState=()=>1;this.getCurrentTime=()=>(performance.now()-t)/1000;this.isMuted=()=>false;this.getVolume=()=>100;this.unMute=this.setVolume=this.playVideo=this.mute=this.stopVideo=this.destroy=()=>{};setTimeout(()=>o.events.onReady({target:this}),0);setTimeout(()=>o.events.onError({data:150}),2500);}};window.onYouTubeIframeAPIReady();";
                        e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(script)), 200, "OK", "Content-Type: application/javascript; charset=utf-8");
                    };
                }
            };
            player.SampleReceived += samples.Add; player.Failed += failures.Add;
            return player;
        });
        var start = playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" });
        PumpUntil(() => local.Starts > 0, TimeSpan.FromSeconds(30));
        Check(start.IsCompleted, "Initialization did not settle."); start.GetAwaiter().GetResult();
        Equal(1, local.Starts); Check(referrerSeen, "The local HTTPS origin did not produce a Referer header.");
        Check(samples.Count >= 2, "The real WebView message bridge did not deliver progress samples.");
        Check(samples.All(sample => !sample.AudioPlaying), "A JavaScript PLAYING claim was incorrectly treated as native audio.");
        Check(failures.Any(reason => reason.Contains("150", StringComparison.Ordinal)), "Expected embed failure did not reach the host.");
        playback.Stop(); Check(!playback.IsActive); Check(!local.Active);
        using (var cancelled = new WebViewYouTubePlayer(store))
        {
            var queuedStart = cancelled.StartAsync("M7lc1UVf-VE", CancellationToken.None);
            cancelled.Stop();
            PumpUntil(() => queuedStart.IsCompleted, TimeSpan.FromSeconds(3));
            Check(queuedStart.IsCanceled, "A queued start reopened a stopped player.");
            Check(!Application.Current.Windows.OfType<Window>().Any(w => w.Title == "YouTube 알람" && w.IsVisible));
        }
        Console.WriteLine("  Real Runtime, HTTPS origin/Referer, session bridge and mute/dispose-before-local verified; YouTube API response was simulated.");
    }
    private static void PlaybackTests()
    {
        Run("YouTube video ID extraction rejects ambiguous and injected inputs", () =>
        {
            foreach (var url in new[] { "https://youtu.be/M7lc1UVf-VE", "https://www.youtube.com/watch?v=M7lc1UVf-VE&t=10", "https://m.youtube.com/shorts/M7lc1UVf-VE", "https://www.youtube.com/live/M7lc1UVf-VE", "https://www.youtube.com/embed/M7lc1UVf-VE" })
            { Check(AlarmSettings.TryGetYouTubeVideoId(url, out var id)); Equal("M7lc1UVf-VE", id); }
            foreach (var url in new[] { "https://youtu.be/short", "https://youtu.be/M7lc1UVf-VE/extra", "https://www.youtube.com/watch?v=M7lc1UVf-VE&v=M7lc1UVf-VE", "https://www.youtube.com/embed/%3Cscript%3E", "https://www.youtube.com/shorts/M7lc1UVf-VE/extra" })
                Check(!AlarmSettings.TryGetYouTubeVideoId(url, out _), url);
        });
        Run("PLAYING alone, static clock, muted video or absent audio never confirms playback", () =>
        {
            foreach (var scenario in new[] { "static", "muted", "silent", "zero-volume", "buffering" })
            {
                using var h = new PlaybackHarness(_root); h.Start();
                h.YouTube.Emit(0, audible: scenario != "silent", muted: scenario == "muted", state: scenario == "buffering" ? 3 : 1, volume: scenario == "zero-volume" ? 0 : 100);
                h.Clock.Advance(2);
                h.YouTube.Emit(scenario == "static" ? 0 : 2, audible: scenario != "silent", muted: scenario == "muted", state: scenario == "buffering" ? 3 : 1, volume: scenario == "zero-volume" ? 0 : 100);
                Equal(0, h.Local.Starts); h.Tick(18); Equal(1, h.Local.Starts);
                Check(h.Events.IndexOf("youtube-stop") < h.Events.IndexOf("local-start"));
            }
        });
        Run("confirmed YouTube stays exclusive beyond startup timeout", () =>
        {
            using var h = new PlaybackHarness(_root); h.Start(); h.YouTube.Emit(0);
            for (var second = 1; second <= 30; second++) { h.Clock.Advance(1); h.YouTube.Emit(second); h.Tick(0); }
            Equal(0, h.Local.Starts); Equal(0, h.YouTube.Stops);
        });
        Run("YouTube startup timeout closes player before starting local once", () =>
        {
            using var h = new PlaybackHarness(_root); h.Start(); h.Tick(19); Equal(0, h.Local.Starts);
            h.Tick(1); Equal(1, h.Local.Starts); Check(h.YouTube.Stops > 0);
            h.YouTube.Emit(1); h.YouTube.Emit(2); h.YouTube.Error(); Equal(1, h.Local.Starts);
            Check(h.Events.IndexOf("youtube-stop") < h.Events.IndexOf("local-start"));
        });
        Run("confirmed playback stalling or losing audio switches to local", () =>
        {
            foreach (var lostAudio in new[] { false, true })
            {
                using var h = new PlaybackHarness(_root); h.Start(); h.YouTube.Emit(0); h.Clock.Advance(1); h.YouTube.Emit(1);
                if (lostAudio) { h.Clock.Advance(1); h.YouTube.Emit(2, audible: false); h.Tick(9); }
                else h.Tick(10);
                Equal(1, h.Local.Starts); Check(h.Events.IndexOf("youtube-stop") < h.Events.IndexOf("local-start"));
            }
        });
        Run("YouTube initialization error and autoplay error fall back immediately", () =>
        {
            using var failed = new PlaybackHarness(_root); failed.YouTube.FailStart = true; failed.Start(); Equal(1, failed.Local.Starts);
            using var blocked = new PlaybackHarness(_root); blocked.Start(); blocked.YouTube.Error(); Equal(1, blocked.Local.Starts);
        });
        Run("late initialization and playback cannot overlap local fallback", () =>
        {
            using var h = new PlaybackHarness(_root); var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.YouTube.Initialization = initialization.Task;
            var start = h.Playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" });
            h.Tick(20); Equal(1, h.Local.Starts); initialization.SetResult(); start.GetAwaiter().GetResult();
            h.YouTube.Emit(0); h.YouTube.Emit(1); h.YouTube.Error(); Equal(1, h.Local.Starts); Check(h.YouTube.Stops > 0);
        });
        Run("stop during initialization prevents all late audio", () =>
        {
            using var h = new PlaybackHarness(_root); var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.YouTube.Initialization = initialization.Task;
            var start = h.Playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" });
            h.Playback.Stop(); initialization.SetResult(); start.GetAwaiter().GetResult();
            h.YouTube.Emit(1); h.YouTube.Error(); h.Tick(30); Equal(0, h.Local.Starts); Check(!h.Playback.IsActive);
        });
        Run("old session events cannot affect a replacement alarm", () =>
        {
            var old = new FakeYouTube(); var next = new FakeYouTube(); var players = new Queue<IYouTubeAlarmPlayer>([old, next]); var local = new FakeLocalOutput();
            using var playback = new AlarmPlaybackService(new AppStore(Path.Combine(_root, "replace")), local, () => players.Dequeue());
            playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" }).GetAwaiter().GetResult();
            playback.StartAsync(new AlarmSettings { YouTubeUrl = "https://youtu.be/M7lc1UVf-VE" }).GetAwaiter().GetResult();
            old.Error(); old.Emit(1); Equal(0, local.Starts); Check(old.Stops > 0); Equal(0, next.Stops);
        });
        Run("player stop button stops both sources without activating fallback", () =>
        {
            using var h = new PlaybackHarness(_root); h.Playback.StopRequested += h.Playback.Stop; h.Start(); h.YouTube.RequestStop();
            Check(!h.Playback.IsActive); Check(h.YouTube.Stops > 0); Equal(0, h.Local.Starts);
        });
        Run("stop reentering during native player closure cannot start local audio", () =>
        {
            using var h = new PlaybackHarness(_root); h.Start();
            h.YouTube.OnStop = h.Playback.Stop;
            h.YouTube.Error();
            Equal(0, h.Local.Starts); Check(!h.Playback.IsActive);
        });
        Run("stop reentering from fallback status cannot start local audio", () =>
        {
            using var h = new PlaybackHarness(_root); h.Start();
            h.Playback.StatusChanged += status => { if (status.Contains("로컬 백업", StringComparison.Ordinal)) h.Playback.Stop(); };
            h.YouTube.Error();
            Equal(0, h.Local.Starts); Check(!h.Playback.IsActive);
        });
        Run("missing or invalid YouTube starts local without creating a WebView", () =>
        {
            var local = new FakeLocalOutput();
            using var playback = new AlarmPlaybackService(new AppStore(Path.Combine(_root, "local-only")), local, () => throw new InvalidOperationException("must not create WebView"));
            playback.StartAsync(new AlarmSettings()).GetAwaiter().GetResult(); Equal(1, local.Starts);
            playback.StartAsync(new AlarmSettings { YouTubeUrl = "invalid recovered URL" }).GetAwaiter().GetResult(); Equal(2, local.Starts);
        });
        Run("WebView message bridge authenticates session and validates payload", () =>
        {
            var json = "{\"session\":\"current\",\"type\":\"progress\",\"state\":1,\"position\":1.5,\"muted\":false,\"volume\":100}";
            Check(YouTubeMessage.TryRead(json, "current", true, out var sample)); Check(sample?.Sample?.AudioPlaying == true);
            Check(!YouTubeMessage.TryRead(json, "stale", true, out _));
            Check(!YouTubeMessage.TryRead(json.Replace("1.5", "-1"), "current", true, out _));
            Check(!YouTubeMessage.TryRead(json.Replace("100", "101"), "current", true, out _));
            Check(!YouTubeMessage.TryRead(json.Replace("false", "\"false\""), "current", true, out _));
            Check(!YouTubeMessage.TryRead("null", "current", true, out _)); Check(!YouTubeMessage.TryRead(new string('x', 5000), "current", true, out _));
            Check(YouTubeMessage.TryRead("{\"session\":\"current\",\"type\":\"blocked\"}", "current", true, out var blocked)); Check(blocked?.Failure is not null);
            Check(YouTubeMessage.TryRead("{\"session\":\"current\",\"type\":\"error\",\"code\":153}", "current", true, out var error)); Check(error?.Failure?.Contains("153", StringComparison.Ordinal) == true);
        });
    }
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
    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < timeout) Pump(TimeSpan.FromMilliseconds(50));
        Check(condition(), "Timed out waiting for an integration event.");
    }
    private static void WithDispatcherContext(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
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
