using System.Diagnostics;
using System.IO;
using System.Windows;
using WindowsAlarm.Services;

namespace WindowsAlarm;

public partial class PowerStatusWindow : Window
{
    private readonly Func<PowerStatusSnapshot> _readStatus;
    public PowerStatusWindow(Func<PowerStatusSnapshot>? readStatus = null)
    {
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        _readStatus = readStatus ?? new WindowsPowerService().ReadStatus;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        PowerStatusSnapshot snapshot;
        try { snapshot = _readStatus(); }
        catch (Exception ex) { snapshot = new(null, null, null, null, [ex.Message]); }
        DataContext = PowerReadinessReport.Create(snapshot);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenPowerOptions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "control.exe")) { UseShellExecute = true };
            start.ArgumentList.Add("powercfg.cpl");
            Process.Start(start);
        }
        catch (Exception ex) { MessageBox.Show(this, "전원 옵션을 열지 못했습니다. 제어판의 전원 옵션을 직접 열어 주세요.\n\n" + ex.Message, "전원 옵션"); }
    }
}
