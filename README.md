# dsh-desktop

> 把 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 的 WebUI 封装成 Windows 桌面应用的**壳**（shell）。
> A Windows desktop shell that wraps the dsh WebUI into a native app — no browser tab needed.
>
> ⚠️ **非官方项目**：与 DeepSeek 官方无任何关联，亦非由其维护。
> ⚠️ **Unofficial**: not affiliated with or endorsed by DeepSeek.

`DshDesktop.exe` 用 **WebView2 + C# WPF** 渲染 `dsh web` 的 WebUI，并替你托管服务生命周期：双击启动、关窗口折叠到托盘、托盘退出才真正退出（同时停掉服务）。开箱即用，指到你的 dsh 源码目录即可。

![platform](https://img.shields.io/badge/platform-Windows%2011%2B-blue) ![dotnet](https://img.shields.io/badge/dotnet-.NET%2010%20Desktop-purple)

## 特性

- 🖥️ 原生桌面体验：无边框 + DWM 圆角窗口，WebView2 渲染，不打开浏览器
- 🐋 启动 Splash：透明鲸鱼图标 + 荧光光晕，加载期闪烁，不阻塞操作
- 🖱️ 系统托盘：关闭窗口 → 折叠到托盘（进程与服务常驻）；双击/右键恢复；托盘「退出」才停服务
- ⚙️ 服务托管：自动拉起/检测 `pnpm dsh web`（**已运行时直接挂接**，不重复拉起），单实例互斥，进程树整棵回收；启动前预检 dshDir / node_modules，进程提前退出快速报错而非空等超时
- ♻️ 掉线自愈：运行中服务意外停止 → 自动重启并刷新页面（自动重启失败才弹框询问）；仅"端口拒绝连接"确认停止才快速重启，响应慢的活服务不会被误杀
- 🛡️ WebView2 崩溃自愈：渲染/浏览器进程异常退出自动重载兜底（30 秒内崩溃超 3 次才停止，避免死循环）
- 📝 本地日志：`%LOCALAPPDATA%\DshDesktop\app.log`，服务启动/退出/重启与错误均可追溯，超过 1MB 自动轮转
- 📦 可移植：服务路径由 `config.json` / `DSH_DIR` 配置，无硬编码绝对路径

## 前置要求

dsh-desktop 只是**壳**：要真正跑任务，你还需要 dsh Web UI 在运行，并在 Web UI 的 **Settings → Models** 配置模型 Provider（至少一个 DeepSeek API key）。Claude Code / Codex CLI 是 dsh 的可选 subagent 后端，**不是** dsh-desktop 的依赖。

| 前置项 | 说明 |
|---|---|
| Windows 11 | 含系统 WebView2 Runtime；Win10 需自行安装 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) |
| [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | 使用自包含构建则无需 |
| dsh Web UI 或源码 | **二选一**：已在运行（attach 模式，无需配置）或源码目录（managed 模式，需 `pnpm install`） |

> 按你的情况选路径：**[docs/QUICKSTART.md](docs/QUICKSTART.md)**（已装 / 未装 Web UI、有无 agent 四种场景），想让 Claude Code / Codex 帮你装或排查：**[docs/AGENT_PROMPTS.md](docs/AGENT_PROMPTS.md)**。

## 文档

- [快速开始（场景分层）](docs/QUICKSTART.md) — 已装 / 未装 Web UI、源码 / npx、attach / managed
- [给 agent 的 prompt](docs/AGENT_PROMPTS.md) — 从零搭建 / 对接排查 两份可复制的 prompt
- [常见问题排查](docs/TROUBLESHOOTING.md) — 黑屏、端口占用、退出后服务残留等

## 构建

```powershell
# 手动
cd app
dotnet publish -c Release -r win-x64 --self-contained false
# 产物: app/bin/Release/net10.0-windows/win-x64/publish/

# 或使用脚本（也供 CI 复用）
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

> 想做成免安装的单文件分发，改用 `--self-contained true`（体积更大，但无需本机装 .NET）。

## 配置

App 会按以下顺序解析 dsh 源码目录（**attach 模式下可不配置**——服务已由你自己启动时，app 只挂接不重复拉起）：

1. **`config.json`**（exe 旁的配置文件，首次运行若不存在会自动生成）
2. 环境变量 **`DSH_DIR`**
3. 相对 exe 的回退路径（标准仓库布局下的 `../deepseek-harness`）

`config.example.json` 为模板：

```json
{
  "dshDir": "C:\\path\\to\\deepseek-harness",
  "url": "http://127.0.0.1:3080/",
  "pollTimeoutSec": 90,
  "startMaximized": true
}
```

- `startMaximized: true` 时启动即最大化；默认 `false` 保持普通窗口（标题栏有最大化/还原按钮）

手动启动服务（调试/不经过 app 时）：

```bat
scripts\run-dsh-web.cmd [dshDir]
```

## 使用

1. 双击 `DshDesktop.exe`（或桌面快捷方式）
2. 弹出 Splash，后台自动拉起 dsh 服务，随后加载 WebUI（普通窗口启动；`startMaximized: true` 可改为启动最大化）
3. 标题栏右侧有「刷新」「开发者工具 (DevTools)」「最小化」「最大化/还原」「关闭」按钮（WebView2 内 F5/Ctrl+R 本身也可刷新）
4. 点窗口 **✕** → 不退出，折叠到系统托盘（右下角鲸鱼图标）
5. 托盘 **双击** 恢复窗口；右键 → **显示 / 退出**
6. **退出** = 停掉服务并关闭 App

## 项目结构

```
dsh-desktop/
├── app/                      # WPF 桌面应用（net10.0-windows）
│   ├── App.xaml(.cs)         # 单实例 + 启动引导 + Splash
│   ├── MainWindow.xaml(.cs)  # WindowChrome 无边框窗口 + WebView2 + DWM 圆角
│   ├── SplashWindow.xaml(.cs)# 透明启动闪烁屏
│   ├── TrayIcon.cs           # 托盘图标 + 深色圆角菜单
│   ├── ServiceHost.cs        # dsh 服务托管（拉起/探测/进程树回收）
│   └── DshDesktop.csproj
├── docs/                     # 场景化快速开始 / agent prompts / 排查
├── scripts/
│   ├── run-dsh-web.cmd       # 独立服务宿主脚本
│   └── build-release.ps1     # 一键发布
├── .github/                  # CI / Release 构建 + Issue 模板
├── config.example.json       # 配置模板
├── dsh-icon.ico / .png       # 鲸鱼图标
└── dsh-icon-source.svg       # 图标源文件
```

## 工作原理

- **服务托管**：启动时探测 `127.0.0.1:3080`，**已在运行则挂接（attach）**——适合 `npx @deepseek-ai/dsh web` 等自行启动的场景，app 不重复拉起、退出时也不停这个服务；未运行则以隐藏窗口调用 `scripts/run-dsh-web.cmd`（启动命令的唯一实现，清空 `PWD`/`INIT_CWD` 防止 git-bash 注入陈旧工作目录导致 pnpm 解析错误），轮询就绪后加载。启动前预检 dshDir 与 node_modules；启动的进程提前退出时立即报错，不空等超时。
- **掉线自愈**：运行期每 15 秒探测一次；**端口拒绝连接**连续两次（或超时/异常持续约 90 秒）才视为服务停止，自动重新拉起并刷新页面——响应慢的活服务不会被误杀；自动重启失败才弹框询问（选「否」则停止监控）。
- **托盘常驻**：窗口 `Closing` 事件 `e.Cancel + Hide()`，App 以 `OnExplicitShutdown` 模式运行，只有托盘「退出」路径才 `Shutdown()`。
- **进程树回收**：`taskkill /PID <cmd> /T /F` 整棵杀掉 cmd → pnpm → node，避免孤儿进程。
- **窗口圆角**：`DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)` 在 DWM 层圆角，连 WebView2 一起裁切。

> ⚠️ 已知限制：`AllowsTransparency=True` 会让 WebView2 黑屏/失配，因此 Splash 用透明窗口，主窗口圆角走 DWM 而非 XAML 透明。

## 贡献

欢迎提交 Issue / PR。改动请保持最小化，涉及服务启动逻辑的改动请同时验证进程树无孤儿。

## License

[MIT](LICENSE)
