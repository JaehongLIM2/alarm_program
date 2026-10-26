using System.ComponentModel;
using System.Windows;
using WindowsAlarm.Services;
using ShutdownMode = WindowsAlarm.Services.ShutdownMode;

namespace WindowsAlarm;

public partial class ShutdownWindow : Window
{
    private readonly ShutdownController _controller;
    private bool _programmaticClose;
    private bool _isClosing;
    public ShutdownWindow(ShutdownController controller)
    {
        InitializeComponent(); _controller = controller;
        Closing += OnClosing;
    }
    public void Refresh(DateTimeOffset now)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(((_controller.Deadline ?? now) - now).TotalSeconds));
        var active = _controller.Mode == ShutdownMode.ActivityConfirmation;
        MessageText.Text = active ? "현재 PC 사용이 감지되었습니다.\n자동 종료를 진행하시겠습니까?" : $"PC가 {seconds}초 후 자동 종료됩니다.";
        DetailText.Text = active ? $"{seconds}초 안에 응답하지 않으면 자동 종료를 취소합니다." : "키보드/마우스 입력이 감지되면 확인 모드로 전환합니다. 작업을 계속하려면 종료 취소를 누르세요.";
    }
    public void Dismiss() { _programmaticClose = true; if (!_isClosing) Close(); }
    private void OnClosing(object? sender, CancelEventArgs e) { _isClosing = true; if (!_programmaticClose) _controller.Cancel("확인 창을 닫음"); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _controller.Cancel();
    private void Shutdown_Click(object sender, RoutedEventArgs e) => _controller.RequestNow();
}
