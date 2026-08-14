using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using DrawingColor = System.Drawing.Color;
using Microsoft.Web.WebView2.Core;
using MessageBox = System.Windows.MessageBox;

namespace DshDesktop;

public partial class MainWindow : Window
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_MENU = 0x12;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 2;

    private readonly string _startUrl;
    private readonly bool _startMaximized;
    private bool _isExiting;
    private int _rendererCrashStreak;
    private DateTime _lastRendererCrash = DateTime.MinValue;

    public MainWindow(string startUrl, bool startMaximized)
    {
        InitializeComponent();
        _startUrl = startUrl;
        _startMaximized = startMaximized;
        StateChanged += OnWindowStateChanged;
        Loaded += OnLoaded;
    }

    // Show() 之后调用：WPF 不允许 Show() 时同时 ShowActivated=false 与 Maximized，
    // 必须先以普通状态 Show（避免启动抢占前台），再切换最大化。
    public void ApplyStartupState()
    {
        if (_startMaximized)
            WindowState = WindowState.Maximized;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetRoundedCorners();
        HookMaximizeToWorkArea();
        try
        {
            // 与窗口背景 #1B1B1F 一致，避免 WebView2 初始化/加载期间的白屏闪烁
            Browser.DefaultBackgroundColor = DrawingColor.FromArgb(0x1B, 0x1B, 0x1F);
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.ProcessFailed += OnBrowserProcessFailed;
            Browser.Source = new Uri(_startUrl);
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 初始化失败: " + ex);
            MessageBox.Show("WebView2 初始化失败：" + ex.Message,
                "DeepSeek Harness", MessageBoxButton.OK, MessageBoxImage.Error);
            // 没有 WebView2 的窗口没有用途：停服务并退出，修复环境后重开
            _isExiting = true;
            System.Windows.Application.Current.Shutdown();
        }
    }

    // WebView2 进程异常退出时自愈：子框架/渲染进程崩溃 WebView2 通常会自行恢复，
    // 这里兜底主动重载；30 秒内崩溃超过 3 次则停止，避免无限重载循环。
    private void OnBrowserProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Error($"WebView2 进程异常退出: kind={e.ProcessFailedKind}, exitCode={e.ExitCode}, reason={e.Reason}");
        // 子框架渲染进程崩溃（FrameRenderProcessExited）由 WebView2 自行恢复，无需干预
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited) return;
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isExiting) return;
                var now = DateTime.Now;
                _rendererCrashStreak =
                    now - _lastRendererCrash < TimeSpan.FromSeconds(30) ? _rendererCrashStreak + 1 : 1;
                _lastRendererCrash = now;
                if (_rendererCrashStreak > 3)
                {
                    Log.Error("WebView2 反复崩溃（30 秒内超过 3 次），停止自动恢复");
                    return;
                }
                try { Browser.CoreWebView2?.Reload(); }
                catch (Exception reloadEx) { Log.Error("WebView2 崩溃恢复失败: " + reloadEx.Message); }
            });
        }
        catch (Exception ex)
        {
            Log.Error("调度 WebView2 崩溃恢复失败: " + ex.Message);
        }
    }

    /// <summary>重新加载当前页面（供刷新按钮与健康检查自动重启后使用）。</summary>
    public void Reload()
    {
        if (Browser.CoreWebView2 != null)
            Browser.CoreWebView2.Reload();
    }

    private void SetRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // WPF 无边框窗口最大化时默认铺满整个屏幕（含任务栏区域），
    // 底部内容会被任务栏挡住。挂 WM_GETMINMAXINFO 把最大尺寸钳制到
    // 当前监视器的工作区（不含任务栏），最大化即不遮任务栏。
    private void HookMaximizeToWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(hwnd) is { } src)
            src.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                var w = mi.rcWork;
                mmi.ptMaxPosition.X = w.Left;
                mmi.ptMaxPosition.Y = w.Top;
                mmi.ptMaxSize.X = w.Right - w.Left;
                mmi.ptMaxSize.Y = w.Bottom - w.Top;
                mmi.ptMaxTrackSize.X = w.Right - w.Left;
                mmi.ptMaxTrackSize.Y = w.Bottom - w.Top;
                Marshal.StructureToPtr(mmi, lParam, false);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECTL { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECTL rcMonitor;
        public RECTL rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e) => Reload();

    private async void DevToolsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 == null) return;
        Browser.CoreWebView2.OpenDevToolsWindow();
        // DevTools 属于浏览器进程的独立窗口，主窗口容易把前台抢回来；
        // 打开后把它置顶并激活，避免被盖住。窗口创建是异步的，稍等重试。
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(100);
            var hwnd = FindDevToolsWindow();
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(hwnd);
                return;
            }
        }
    }

    private void MinButton_OnClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaxButton_OnClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // 切换最大化/还原时同步按钮图标（E922 方块 / E923 叠块）
    private void OnWindowStateChanged(object? sender, EventArgs e)
        => MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        => Hide();

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting) return;
        e.Cancel = true;
        Hide();
    }

    public void PrepareExit() => _isExiting = true;

    public void ShowFromTray()
    {
        // WPF 硬限制：ShowActivated=false 时 Show() 不允许 Maximized 状态。
        // 用户先最大化再折叠到托盘后恢复，就会命中该异常——恢复前临时放开。
        // （ShowActivated 只在 Show() 时起作用，放开后无副作用。）
        if (WindowState == WindowState.Maximized)
            ShowActivated = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ActivateWindowSafely(this);
    }

    // 前台锁规避：直接 Activate() 在别的窗口拥有前台时会被拒绝，
    // 表现为任务栏按钮红色闪烁。先模拟一次 Alt 键按下，让本进程
    // 取得设置前台的资格，再 SetForegroundWindow 就能正常置前。
    private static void ActivateWindowSafely(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    // 只匹配 WebView2 进程（msedgewebview2.exe）的 DevTools 窗口，
    // 避免误置顶 Chrome 等其他 Chromium 应用的同名窗口。
    private static IntPtr FindDevToolsWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().StartsWith("DevTools - ", StringComparison.OrdinalIgnoreCase)
                && IsOwnedByWebView2Process(hWnd))
            {
                found = hWnd;
                return false; // 停止枚举
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsOwnedByWebView2Process(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0) return false;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return string.Equals(proc.ProcessName, "msedgewebview2", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // 进程已退出
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
