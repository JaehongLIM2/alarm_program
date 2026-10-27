using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace WindowsAlarm;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        DataContext = new { AppInfo.Author, AppInfo.RepositoryUrl, AppInfo.RepositoryUri, AppInfo.ContactEmail, AppInfo.ContactUri };
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        // Only the two fixed public contact links can be passed to Windows.
        if (e.Uri != AppInfo.RepositoryUri && e.Uri != AppInfo.ContactUri) return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception)
        {
            MessageBox.Show(this, "연결된 앱을 열지 못했습니다. 아래 주소를 브라우저나 메일 앱에 직접 입력해 주세요.\n\n" + e.Uri.AbsoluteUri,
                "링크 열기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
