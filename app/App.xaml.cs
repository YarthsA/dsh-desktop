using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DshDesktop;

public partial class App : Application
{
    private const string MutexName = "DshDesktop_SingleInstance";
    private const string ShowEventName = "DshDesktop_ShowWindowEvent";
    private static readonly TimeSpan TopmostHoldDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(15);

    private Mutex? _mutex;
    private ServiceHost? _serviceHost;
    private MainWindow? _mainWindow;
    private TrayIcon? _tray;
    private SplashWindow? _splash;
    private DispatcherTimer? _healthTimer;
    private volatile bool _shuttingDown;
    private bool _probing;
    private int _downStreak;   // 端口拒绝连接的连续次数
    private int _hangStreak;   // 超时/其他探测错误的连续次数

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 兜底：未处理异常记日志 + 友好提示，不再弹 .NET 默认异常对话框
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("未处理异常: " + args.Exception);
            MessageBox.Show(
                "发生未处理异常：\n" + args.Exception.Message +
                "\n\n详情见日志：%LOCALAPPDATA%\\DshDesktop\\app.log",
                "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // 上次进程若异常退出（崩溃/被强杀），命名互斥量会处于 abandoned 状态，
        // 此时 ctor 抛 AbandonedMutexException 且所有权已转移给本线程——按首实例继续。
        bool isFirstInstance;
        try
        {
            _mutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch (AbandonedMutexException)
        {
            isFirstInstance = true;
            _mutex = null;
        }

        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _serviceHost = new ServiceHost();
        Log.Info($"应用启动：dshDir={_serviceHost.DshDir}，url={_serviceHost.Url}");
        _ = BootAsync();
    }

    private async Task BootAsync()
    {
        try
        {
            await BootCoreAsync();
        }
        catch (Exception ex)
        {
            // 服务循环之外的首启兜底：窗口/托盘等意外失败时给出提示并退出
            Log.Error("启动过程异常: " + ex);
            MessageBox.Show("启动失败：" + ex.Message,
                "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task BootCoreAsync()
    {
        _splash = new SplashWindow();
        _splash.Show();

        // 服务起不来时给"重试/退出"：重试会重建 ServiceHost 重新读 config（用户可能已改 dshDir）
        while (true)
        {
            try
            {
                await _serviceHost!.EnsureRunningAsync();
                break;
            }
            catch (Exception ex)
            {
                Log.Error("启动 dsh 服务失败: " + ex.Message);
                var retry = MessageBox.Show("无法启动 dsh 服务：\n" + ex.Message + "\n\n是否重试？",
                    "DeepSeek Harness", MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (retry != MessageBoxResult.Yes)
                {
                    Shutdown();
                    return;
                }
                _serviceHost = new ServiceHost();
            }
        }

        _mainWindow = new MainWindow(_serviceHost!.Url, _serviceHost.StartMaximized);
        _tray = new TrayIcon(_mainWindow.ShowFromTray, ExitFromTray);
        // 先置顶、声明不激活，再显示：若启动时用户停在别的最大化窗口，
        // 直接以置顶姿态出现在最上方，避免 Activate() 因前台锁被拒而
        // 触发任务栏按钮红色闪烁（splash 消失与窗口出现同帧完成，无空档）
        _mainWindow.Topmost = true;
        _mainWindow.ShowActivated = false;
        _mainWindow.Show();
        _mainWindow.ApplyStartupState(); // 启动最大化：Show 之后切状态，避免 ShowActivated=false+Maximized 冲突
        _splash.Close();
        _splash = null;
        var win = _mainWindow;
        var timer = new DispatcherTimer { Interval = TopmostHoldDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            win.Topmost = false;
        };
        timer.Start();

        _healthTimer = new DispatcherTimer { Interval = HealthCheckInterval };
        _healthTimer.Tick += OnHealthTick;
        _healthTimer.Start();

        Log.Info("应用就绪");
        await Task.Run(ListenForShowEvent);
    }

    // 运行期健康检查：服务意外停止时自动拉起并刷新页面。
    // 只有"端口拒绝连接"（服务确认停止）连续两次才重启；超时/异常可能是
    // 服务繁忙，需连续约 90 秒（6 次）才视为异常，避免误杀活服务。
    private async void OnHealthTick(object? sender, EventArgs e)
    {
        if (_probing || _shuttingDown || _mainWindow is null || _serviceHost is null) return;
        _probing = true;
        try
        {
            var probe = await _serviceHost.ProbeAsync();
            if (probe == ServiceHost.ProbeResult.Up)
            {
                _downStreak = 0;
                _hangStreak = 0;
                return;
            }

            if (probe == ServiceHost.ProbeResult.Refused)
            {
                _hangStreak = 0;
                if (++_downStreak < 2) return;
            }
            else
            {
                _downStreak = 0;
                if (++_hangStreak < 6) return;
            }

            _downStreak = 0;
            _hangStreak = 0;
            Log.Info("检测到 dsh 服务停止，尝试自动重启");
            try
            {
                await _serviceHost.EnsureRunningAsync();
                _mainWindow.Reload();
                Log.Info("dsh 服务已自动重启");
            }
            catch (Exception ex)
            {
                Log.Error("自动重启失败: " + ex.Message);
                if (_shuttingDown) return;
                var retry = MessageBox.Show("dsh 服务已停止且自动重启失败：\n" + ex.Message + "\n\n是否重试？",
                    "DeepSeek Harness", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (retry != MessageBoxResult.Yes)
                    _healthTimer?.Stop();
            }
        }
        catch (Exception ex)
        {
            Log.Error("健康检查异常: " + ex.Message);
        }
        finally
        {
            _probing = false;
        }
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

    // 后台线程：等待第二实例的唤醒信号。带 1s 超时轮询，使退出时
    // （_shuttingDown=true）线程能及时结束；dispatcher 关闭期间的
    // 异常也在此吞掉，避免退出竞态导致未捕获异常。
    // ⚠️ WaitOne 的返回值必须检查：超时只是用于退出检查，
    // 若不检查会每 1 秒误触发一次 ShowFromTray（抢前台）。
    private void ListenForShowEvent()
    {
        var evt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        while (!_shuttingDown)
        {
            bool signaled;
            try { signaled = evt.WaitOne(1000); }
            catch { break; }
            if (!signaled) continue; // 超时：仅用于退出检查，不触发唤醒
            if (_shuttingDown) break;
            try
            {
                Dispatcher.Invoke(() => _mainWindow?.ShowFromTray());
            }
            catch (Exception)
            {
                break; // dispatcher 正在关闭
            }
        }
    }

    private void ExitFromTray()
    {
        _shuttingDown = true;
        _mainWindow?.PrepareExit();
        _tray?.Dispose();
        _serviceHost?.Stop();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        _healthTimer?.Stop();
        _tray?.Dispose();
        _serviceHost?.Stop();
        Log.Info("应用退出");
        base.OnExit(e);
    }
}
