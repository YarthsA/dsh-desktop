using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DshDesktop;

public sealed class ServiceHost
{
    /// <summary>探测结果：Up=HTTP 正常；Refused=端口无人监听（服务确认停止）；Other=超时/其他错误（可能只是慢）。</summary>
    public enum ProbeResult { Up, Refused, Other }

    public sealed class Config
    {
        public string DshDir { get; set; } = "";
        public string Url { get; set; } = "http://127.0.0.1:3080/";
        public int PollTimeoutSec { get; set; } = 90;
        public bool StartMaximized { get; set; } = false;
    }

    // 本地服务冷启动/繁忙时也可能偶发慢响应，超时给足 3s 再判定
    private const int ProbeTimeoutMs = 3000;

    private readonly HttpClient _client;
    private readonly Config _cfg;

    // 所有读写都在 UI 线程（启动流程与健康检查均在 Dispatcher 上）执行，无需加锁；
    // 若未来从后台线程调用 Stop()，需要在此处同步。
    private Process? _serviceProc;

    /// <summary>解析后的服务地址（config.json 的 url，供 WebView2 导航使用）。</summary>
    public string Url => _cfg.Url;

    /// <summary>解析后的 dsh 源码目录（供日志/展示使用）。</summary>
    public string DshDir => _cfg.DshDir;

    /// <summary>启动时是否最大化主窗口。</summary>
    public bool StartMaximized => _cfg.StartMaximized;

    public ServiceHost()
    {
        _cfg = LoadConfig();
        _client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs) };
    }

    /// <summary>探测服务是否已就绪。启动轮询与运行期健康检查共用。</summary>
    public async Task<bool> IsRunningAsync() => await ProbeAsync() == ProbeResult.Up;

    /// <summary>探测服务状态，区分"端口无人监听"与"只是响应慢/异常"，
    /// 供健康检查决定是否值得重启（拒绝连接才确认服务停止）。</summary>
    public async Task<ProbeResult> ProbeAsync()
    {
        try
        {
            // ResponseHeadersRead：只等响应头，不下载整页 HTML
            using var resp = await _client.GetAsync(_cfg.Url, HttpCompletionOption.ResponseHeadersRead);
            return resp.IsSuccessStatusCode ? ProbeResult.Up : ProbeResult.Other;
        }
        catch (Exception ex) when (IsRefused(ex))
        {
            return ProbeResult.Refused;
        }
        catch
        {
            // 超时（TaskCanceledException）等：无法断定服务停止，可能是负载高
            return ProbeResult.Other;
        }
    }

    private static bool IsRefused(Exception ex) =>
        ex is HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused } };

    public async Task EnsureRunningAsync()
    {
        if (await IsRunningAsync())
            return;

        StartService();
        for (int i = 0; i < _cfg.PollTimeoutSec; i++)
        {
            if (await IsRunningAsync())
                return;
            // 启动的进程已提前退出（pnpm/dsh 不存在或立即报错）→ 快速失败，别空等超时
            if (_serviceProc is { HasExited: true })
            {
                var exitCode = _serviceProc.ExitCode;
                _serviceProc.Dispose();
                _serviceProc = null;
                throw new Exception(
                    $"dsh 服务进程提前退出（exit code {exitCode}）。\n" +
                    "请确认 dshDir 正确、已执行 pnpm install、且 pnpm 在 PATH 中。");
            }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"dsh 服务在 {_cfg.PollTimeoutSec} 秒内未就绪");
    }

    public void Stop()
    {
        try
        {
            if (_serviceProc is not { HasExited: false })
            {
                _serviceProc = null;
                return;
            }
            var pid = _serviceProc.Id;
            Log.Info($"停止 dsh 服务进程树 (PID {pid})");
            using var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {pid} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            // 等 taskkill 完成并确认进程树真正退出：否则退出后立即重开 App，
            // 会连到旧服务（端口仍被占），且该服务从此不受任何进程管理。
            if (killer != null && killer.WaitForExit(5000))
                _serviceProc.WaitForExit(3000);
            else if (killer != null)
                Log.Error("taskkill 5 秒内未完成，服务进程可能仍在运行");
        }
        catch (Exception ex)
        {
            Log.Error($"停止服务失败: {ex.Message}");
        }
        finally
        {
            _serviceProc?.Dispose();
            _serviceProc = null;
        }
    }

    // 启动命令的唯一实现是 scripts/run-dsh-web.cmd（内含清 PWD/INIT_CWD 等 git-bash 处理），
    // 这里只负责以隐藏窗口调用它，避免 C# 与 .cmd 两处维护同一命令、以及 cmd 引号拼接的脆弱性。
    private void StartService()
    {
        if (!Directory.Exists(_cfg.DshDir))
            throw new DirectoryNotFoundException(
                $"未找到 dsh 源码目录：{_cfg.DshDir}\n请编辑 exe 旁的 config.json 设置 dshDir，或设置环境变量 DSH_DIR");

        if (!Directory.Exists(Path.Combine(_cfg.DshDir, "node_modules")))
            throw new DirectoryNotFoundException(
                $"dsh 源码目录缺少 node_modules：{_cfg.DshDir}\n请先在该目录执行 pnpm install");

        var script = Path.Combine(AppContext.BaseDirectory, "scripts", "run-dsh-web.cmd");
        if (!File.Exists(script))
            throw new FileNotFoundException($"未找到服务启动脚本：{script}");

        Log.Info($"启动 dsh 服务：{script} \"{_cfg.DshDir}\"");
        var psi = new ProcessStartInfo
        {
            // cmd /c ""script" "dir""：外层引号内再包一对引号，是给 cmd 传带空格参数的标准写法
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{script}\" \"{_cfg.DshDir}\"\"",
            WorkingDirectory = _cfg.DshDir,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        _serviceProc?.Dispose();
        _serviceProc = Process.Start(psi);
    }

    private static Config LoadConfig()
    {
        var cfg = new Config();

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        try
        {
            if (File.Exists(configPath))
            {
                var root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
                if (root != null)
                {
                    if (root["dshDir"] is JsonValue v && v.TryGetValue<string>(out var dir) && !string.IsNullOrEmpty(dir))
                        cfg.DshDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dir));
                    if (root["url"] is JsonValue u && u.TryGetValue<string>(out var url) && !string.IsNullOrEmpty(url))
                        cfg.Url = url;
                    if (root["pollTimeoutSec"] is JsonValue t && t.TryGetValue<int>(out var sec) && sec > 0)
                        cfg.PollTimeoutSec = sec;
                    if (root["startMaximized"] is JsonValue m && m.TryGetValue<bool>(out var max))
                        cfg.StartMaximized = max;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"读取 config.json 失败: {ex.Message}");
        }

        if (string.IsNullOrEmpty(cfg.DshDir))
            cfg.DshDir = Environment.GetEnvironmentVariable("DSH_DIR") ?? "";

        if (string.IsNullOrEmpty(cfg.DshDir))
            cfg.DshDir = FindDshDirByWalkUp() ?? "";

        TryWriteConfig(configPath, cfg);
        return cfg;
    }

    // 相对 exe 向上查找标准仓库布局里的 deepseek-harness 目录（以同名目录 + package.json 为标志），
    // 同时覆盖开发布局（app\bin\Debug\net10.0-windows）与 publish 布局（...\win-x64\publish），
    // 不再依赖写死的目录层级。
    private static string? FindDshDirByWalkUp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "deepseek-harness");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "package.json")))
                return candidate;
        }
        return null;
    }

    // 首次运行把解析结果写一份 config.json 到 exe 旁，方便用户发现并编辑
    private static void TryWriteConfig(string configPath, Config cfg)
    {
        try
        {
            if (File.Exists(configPath)) return;
            File.WriteAllText(configPath, JsonSerializer.Serialize(new
            {
                dshDir = cfg.DshDir,
                url = cfg.Url,
                pollTimeoutSec = cfg.PollTimeoutSec,
                startMaximized = cfg.StartMaximized,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
