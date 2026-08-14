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
            _mainWindow.Show();
            _mainWindow.Activate();
            // 启动完成时若用户正停在别的最大化窗口，主窗口可能落在后面；
            // 临时置顶几秒确保 UI 显示到最上方，随后恢复正常 z-order。
            _mainWindow.Topmost = true;
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
