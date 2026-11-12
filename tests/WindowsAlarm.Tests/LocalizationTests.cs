using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsAlarm.Models;
using WindowsAlarm.Services;

namespace WindowsAlarm.Tests;

internal static partial class Program
{
    private static void RunLocalizationTests()
    {
        Run("language button updates open views and preserves unsaved settings and reservation", () =>
        {
            var store = new AppStore(Path.Combine(_root, "language-ui"));
            var window = new MainWindow(store);
            window.Show(); Pump(TimeSpan.FromMilliseconds(100));
            var hour = (ComboBox)window.FindName("HourBox"); hour.SelectedIndex = 13;
            var url = (TextBox)window.FindName("YouTubeBox"); url.Text = "https://youtu.be/M7lc1UVf-VE";
            var music = (TextBox)window.FindName("MusicBox"); music.Text = @"C:\음악\朝.mp3";
            var volume = (ComboBox)window.FindName("VolumeAfterStopBox"); volume.SelectedIndex = 1;
            var button = (Button)window.FindName("LanguageButton");
            var about = new AboutWindow(); about.Show();
            var power = new PowerStatusWindow(() => new(null, true, 1, 1, [])); power.Show();
            var (controller, fakePower, _) = NewShutdown(); controller.Begin(Now, Now); controller.Tick(Now);
            var countdown = new ShutdownWindow(controller); countdown.Refresh(Now); countdown.Show();
            try
            {
                var reserved = new AlarmSchedule { DueAt = Now.AddDays(1), Settings = new() { AutoShutdownEnabled = true, ShutdownAfterMinutes = 45 } };
                window.ApplyReservationStatus(ReservationStatus.Create(reserved, Services.ShutdownMode.None, null));
                Localization.SetText((TextBlock)window.FindName("StatusText"), "설정을 저장했습니다.");
                ((StackPanel)window.FindName("SettingsPanel")).IsEnabled = false;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Flush bindings without waiting for the coordinator timer to replace the view fixture.
                window.UpdateLayout(); about.UpdateLayout(); power.UpdateLayout(); countdown.UpdateLayout();
                Equal("JP", Localization.Current.Language); Equal("JP → KR", button.Content);
                Equal("JP", store.Load<LanguagePreference>("language.json")?.Language);
                Equal(13, hour.SelectedIndex); Equal("https://youtu.be/M7lc1UVf-VE", url.Text);
                Equal(@"C:\음악\朝.mp3", music.Text); Equal(1, volume.SelectedIndex);
                Check(!File.Exists(Path.Combine(store.Root, "settings.json")));
                Equal("設定を保存しました。", ((TextBlock)window.FindName("StatusText")).Text);
                Equal("アラーム開始から45分後にシャットダウンを確認", ((TextBlock)window.FindName("ShutdownText")).Text);
                Equal("このアプリについて", about.Title);
                Check(FindVisuals<TextBlock>(power).Any(t => t.Text == "電源接続（AC）"));
                Equal("PCを60秒後に自動シャットダウンします。", ((TextBlock)countdown.FindName("MessageText")).Text);
                Equal(0, fakePower.ShutdownCalls); Equal(Services.ShutdownMode.Countdown, controller.Mode);
                Check(((Button)window.FindName("MoreButton")).ContextMenu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "このアプリについて")));
                CaptureLocalized(window, "main-window-jp.png");
                CaptureLocalized(power, "power-status-window-jp.png");
                CaptureLocalized(countdown, "shutdown-window-jp.png");
                window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
                CaptureLocalized(window, "main-window-jp-minimum.png");
                Check(button.IsEnabled && button.ActualWidth > 50);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Equal("KR", Localization.Current.Language); Equal("KR → JP", button.Content);
                Equal("설정을 저장했습니다.", ((TextBlock)window.FindName("StatusText")).Text);
                Equal(13, hour.SelectedIndex); Equal(1, volume.SelectedIndex);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally { countdown.Dismiss(); power.Close(); about.Close(); window.Close(); }
            var reopened = new MainWindow(store);
            try { Equal("JP", Localization.Current.Language); Equal("JP → KR", ((Button)reopened.FindName("LanguageButton")).Content); }
            finally { reopened.Close(); Localization.Current.SetLanguage("KR"); }
        });
        Run("language preference defaults, corrupt file recovery and failed save", () =>
        {
            var store = new AppStore(Path.Combine(_root, "language-preference"));
            Localization.Current.Load(store); Equal("KR", Localization.Current.Language);
            store.Save("language.json", new LanguagePreference("invalid"));
            Localization.Current.Load(store); Equal("KR", Localization.Current.Language);
            File.WriteAllText(Path.Combine(store.Root, "language.json"), "{broken");
            Localization.Current.Load(store); Equal("KR", Localization.Current.Language);
            Check(Directory.GetFiles(store.Root, "*.corrupt-*").Length == 1);
            File.Delete(Path.Combine(store.Root, "language.json"));
            Directory.CreateDirectory(Path.Combine(store.Root, "language.json"));
            var failed = false;
            try { Localization.Current.Toggle(store); }
            catch (UnauthorizedAccessException) { failed = true; }
            Check(failed); Equal("KR", Localization.Current.Language);
        });
        Run("Japanese dynamic messages translate nested status and leave external paths intact", () =>
        {
            Localization.Current.SetLanguage("JP");
            try
            {
                Equal("次のアラーム 2026-11-12 07:30:00 · テスト: 自動シャットダウンなし", Localization.Current.Translate("다음 알람 2026-11-12 07:30:00 · 테스트: 자동 종료 없음"));
                Equal("YouTube未指定 · YouTubeを終了して予備のローカル音楽を再生します。", Localization.Current.Translate("YouTube 미지정 · YouTube를 종료하고 로컬 백업을 시작합니다."));
                Equal(@"C:\알람 예약\朝.mp3", Localization.Current.Translate(@"C:\알람 예약\朝.mp3"));
                Equal("settings.jsonを読み込めません。ログを確認してください。", Localization.Current.Translate("settings.json을 읽을 수 없습니다. 로그를 확인하세요."));
                var report = PowerReadinessReport.Create(new(null, true, 1, 2, []));
                var explanation = Localization.Current.Translate(report.Checks[2].Explanation)!;
                Check(explanation.StartsWith("電源接続: 有効 · バッテリ: 重要なスリープ解除タイマーのみ\n"));
                Check(!System.Text.RegularExpressions.Regex.IsMatch(explanation, "[가-힣]"));
            }
            finally { Localization.Current.SetLanguage("KR"); }
        });
    }

    private static void CaptureLocalized(Window window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(_root, name)); encoder.Save(output);
    }
}
