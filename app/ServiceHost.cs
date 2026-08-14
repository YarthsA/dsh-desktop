using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace DshDesktop;

public sealed class ServiceHost
{
    public sealed class Config
    {
        public string DshDir { get; set; } = "";
        public string Url { get; set; } = "http://127.0.0.1:3080/";
        public int PollTimeoutSec { get; set; } = 90;
    }

    private readonly HttpClient _client;
    private readonly Config _cfg;
    private int _servicePid;

    public ServiceHost()
    {
        _cfg = LoadConfig();
        _client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
    }

    public async Task EnsureRunningAsync()
    {
        if (await IsRunningAsync())
            return;

        StartService();
        for (int i = 0; i < _cfg.PollTimeoutSec; i++)
        {
            if (await IsRunningAsync())
                return;
            await Task.Delay(1000);
        }
        throw new TimeoutException($"dsh 服务在 {_cfg.PollTimeoutSec} 秒内未就绪");
    }

    public void Stop()
    {
        if (_servicePid == 0) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {_servicePid} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch { }
        _servicePid = 0;
    }

    private async Task<bool> IsRunningAsync()
    {
        try
        {
            using var resp = await _client.GetAsync(_cfg.Url);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void StartService()
    {
        if (!Directory.Exists(_cfg.DshDir))
            throw new DirectoryNotFoundException(
                $"未找到 dsh 源码目录：{_cfg.DshDir}\n请编辑 exe 旁的 config.json 设置 dshDir，或设置环境变量 DSH_DIR");

        // git-bash 会把会话 CWD 注入成 PWD/INIT_CWD，pnpm 读到陈旧 PWD 会解析错 importer，
        // 这里清掉让 pnpm 以当前 cd 的目录为准。
        var cmdLine = $"cd /d \"{_cfg.DshDir}\" && set PWD= && set INIT_CWD= && pnpm dsh web";
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{cmdLine}\"",
            WorkingDirectory = _cfg.DshDir,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        _servicePid = proc!.Id;
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
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(cfg.DshDir))
            cfg.DshDir = Environment.GetEnvironmentVariable("DSH_DIR") ?? "";

        if (string.IsNullOrEmpty(cfg.DshDir))
        {
            // 相对 exe 回退：publish 布局比开发布局深一层，两个都试
            foreach (var rel in new[]
                     {
                         @"..\..\..\..\..\..\..\deepseek-harness",
                         @"..\..\..\..\..\..\deepseek-harness",
                     })
            {
                var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
                if (Directory.Exists(candidate))
                {
                    cfg.DshDir = candidate;
                    break;
                }
            }
        }

        TryWriteConfig(configPath, cfg);
        return cfg;
    }

    // 首次运行把解析结果写一份 config.json 到 exe 旁，方便用户发现并编辑
    private static void TryWriteConfig(string configPath, Config cfg)
    {
        try
        {
            if (File.Exists(configPath)) return;
            File.WriteAllText(configPath,
                "{\n" +
                $"  \"dshDir\": \"{cfg.DshDir.Replace(@"\", @"\\")}\",\n" +
                $"  \"url\": \"{cfg.Url}\",\n" +
                $"  \"pollTimeoutSec\": {cfg.PollTimeoutSec}\n" +
                "}\n");
        }
        catch { }
    }
}
