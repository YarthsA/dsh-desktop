using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DshDesktop;

public partial class App : Application
{
    private const string MutexName = "DshDesktop_SingleInstance";
    private const string ShowEventName = "DshDesktop_ShowWindowEvent";

    private Mutex? _mutex;
    private ServiceHost? _serviceHost;
    private MainWindow? _mainWindow;
    private TrayIcon? _tray;
    private SplashWindow? _splash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _serviceHost = new ServiceHost();
        _ = BootAsync();
    }

    private async Task BootAsync()
    {
        Dispatcher.Invoke(() =>
        {
            _splash = new SplashWindow();
            _splash.Show();
        });

        try
        {
            await _serviceHost!.EnsureRunningAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法启动 dsh 服务：" + ex.Message,
                "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Dispatcher.Invoke(() =>
        {
            _mainWindow = new MainWindow();
            _tray = new TrayIcon(_mainWindow.ShowFromTray, ExitFromTray);
            // 先置顶、声明不激活，再显示：若启动时用户停在别的最大化窗口，
            // 直接以置顶姿态出现在最上方，避免 Activate() 因前台锁被拒而
            // 触发任务栏按钮红色闪烁（splash 消失与窗口出现同帧完成，无空档）
            _mainWindow.Topmost = true;
            _mainWindow.ShowActivated = false;
            _mainWindow.Show();
            if (_splash != null)
            {
                _splash.Close();
                _splash = null;
            }
            var win = _mainWindow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                win.Topmost = false;
            };
            timer.Start();
        });

        await Task.Run(ListenForShowEvent);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var evt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            evt.Set();
        }
        catch { }
    }

    private void ListenForShowEvent()
    {
        var evt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        while (true)
        {
            evt.WaitOne();
            Dispatcher.Invoke(() => _mainWindow?.ShowFromTray());
        }
    }

    private void ExitFromTray()
    {
        _mainWindow?.PrepareExit();
        _tray?.Dispose();
        _serviceHost?.Stop();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _serviceHost?.Stop();
        base.OnExit(e);
    }
}
