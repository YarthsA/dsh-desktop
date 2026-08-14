# 快速开始（按你的情况选路径）

dsh-desktop 只是一个**桌面壳**：它把 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 的 Web UI 变成 Windows 桌面应用，并托管服务生命周期。它本身不包含 dsh 的能力——要真正跑任务，你还需要：

1. **dsh Web UI** 在运行（`http://127.0.0.1:3080`，默认端口）
2. **一个模型 Provider**（Web UI 的 **Settings → Models** 填入 DeepSeek API key，或按[官方 providers 指南](https://github.com/deepseek-ai/deepseek-harness/blob/main/docs/user/guide/providers.md)配置其他 provider）

> Claude Code / Codex CLI **不是** dsh-desktop 的依赖——它们是 dsh 的**可选** subagent 后端（用于委派子任务）。只有你要用子 agent 委派时才需要安装。详见 [dsh 与 agent 的关系](#dsh-与-agent-的关系)。

先对照下表选你的场景：

| 场景 | 你的情况 | 推荐路径 | 服务模式 |
|---|---|---|---|
| 0 | Web UI **已经在跑**（浏览器能打开 3080，无论源码还是 npx 方式启动） | [场景 0](#场景-0web-ui-已经在跑) | attach（挂接，不重复拉起） |
| 1 | 有 dsh **源码目录**（已 `pnpm install`），但 Web UI 没跑起来过 | [场景 1](#场景-1有-dsh-源码但-web-ui-没跑起来过) | managed（app 托管拉起） |
| 2 | **什么都没有**，从零开始 | [场景 2](#场景-2从零开始) | 先装环境，再回到场景 0 或 1 |
| 3 | 想用 Claude Code / Codex 帮你安装或排查 | [docs/AGENT_PROMPTS.md](AGENT_PROMPTS.md) | — |

---

## 场景 0：Web UI 已经在跑

你能用浏览器打开 `http://127.0.0.1:3080`（dsh 可以是源码方式 `pnpm dsh web`，也可以是 `npx @deepseek-ai/dsh web`）。

1. 下载 [Release](https://github.com/YarthsA/dsh-desktop/releases) 里的 `dsh-desktop-*-win-x64.zip`，解压到任意目录
2. 双击 `DshDesktop.exe`
3. app 探测到服务已在运行 → **直接挂接（attach）**，不会重复拉起服务，稍候加载 Web UI

无需任何配置。可选：在 exe 旁创建 `config.json` 指定 `url`（见下文）。

> 注意：托盘「退出」**不会**停掉你自己启动的服务——app 只停它自己拉起的进程（设计行为）。想停服务请自行停止（如关闭启动它的终端，或 `taskkill /T /F` 那个进程树）。

## 场景 1：有 dsh 源码，但 Web UI 没跑起来过

你已 clone 了 `deepseek-harness` 并执行过 `pnpm install`（缺 `node_modules` 的话 app 会拒绝启动并提示）。

1. 下载 Release zip，解压
2. 在 exe 旁创建 `config.json`：
   ```json
   {
     "dshDir": "D:\\path\\to\\deepseek-harness"
   }
   ```
   > 不建也行：app 会按 `DSH_DIR` 环境变量 → 相对 exe 向上查找 `deepseek-harness`（同名目录 + package.json）的顺序自动定位。
3. 双击 `DshDesktop.exe`：app 以隐藏窗口调用 `pnpm dsh web` 拉起服务，就绪后加载 Web UI
4. 托盘「退出」= 停掉服务并关闭 app
5. 验证：`powershell -ExecutionPolicy Bypass -File scripts\verify-install.ps1 -AppDir <app 目录>`（一条命令检查前置、补丁、服务与归属）

> 托管（managed）模式下，app 启动服务前会自动给 dsh 的目录选择器打「PowerShell 兜底」补丁（幂等，dsh 重新 build 后自动重打）。想手动打：`powershell -ExecutionPolicy Bypass -File scripts\fix-directory-picker.ps1 -DshDir <dshDir>`。

## 场景 2：从零开始

1. 安装 [Node.js](https://nodejs.org/)（≥ 22.19）与 [pnpm](https://pnpm.io/installation)（确保 `pnpm` 在 PATH）
2. 按[官方快速开始](https://github.com/deepseek-ai/deepseek-harness#run)准备 dsh：
   ```sh
   git clone https://github.com/deepseek-ai/deepseek-harness.git
   cd deepseek-harness
   pnpm install
   pnpm dsh web          # 源码方式；或 npx @deepseek-ai/dsh web（无需源码）
   ```
3. 打开 `http://127.0.0.1:3080`，在 **Settings → Models** 配置 API key，验证 Web UI 可用
4. 回到[场景 0](#场景-0web-ui-已经在跑)（npx 方式）或[场景 1](#场景-1有-dsh-源码但-web-ui-没跑起来过)（源码方式）
5. 安装 dsh-desktop 前还需要：Windows 11（或 Win10 + 自装 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)）、[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（用自包含构建则无需）

### 安装 .NET 10 Desktop Runtime

Release 包是**框架依赖**构建，运行需要 .NET 10 Desktop Runtime **机器级**安装（需要管理员）：

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
```

> ⚠️ **不要用 dotnet-install 脚本装用户目录版**：apphost（DshDesktop.exe）只认 `C:\Program Files\dotnet`（机器级），用户目录版装了照样报 "You must install or update .NET to run this application"。必须走机器级安装。
>
> 想完全免装运行时：用 `scripts\build-release.ps1 -SelfContained` 构建自包含 zip（体积更大但零前置）。

装好后一键验证：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-install.ps1 -AppDir <解压后的 app 目录>
```

## 场景 3：让 agent 帮你装或排查

把 [docs/AGENT_PROMPTS.md](AGENT_PROMPTS.md) 里对应场景的 prompt 粘给你的 Claude Code / Codex 会话即可。

---

## dsh 与 agent 的关系

dsh（DeepSeek Harness）是 **agent 编排框架**：它自带 agent 循环，Web UI 只是交互界面之一。对你（dsh-desktop 用户）来说：

- **必需要**：dsh Web UI 在跑 + 至少一个模型 Provider（DeepSeek API key 即可）
- **可选**：Claude Code / Codex CLI —— 作为 dsh 的 **subagent 后端**（主 agent 委派子任务时调用）。安装后 dsh 会自动发现它们；不装也能正常用主循环
- **dsh-desktop 与两者都无关**：它只渲染 Web UI 并托管其进程。你不需要为了用 dsh-desktop 而安装任何 agent

## 兼容性矩阵（两种启动方式 × 两种服务模式）

| dsh 的启动方式 | dsh-desktop 模式 | 说明 |
|---|---|---|
| 源码（`pnpm dsh web`） | managed（推荐）或 attach | `dshDir` 指向源码目录，app 托管服务生命周期，托盘退出即停服务 |
| npx（`npx @deepseek-ai/dsh web`） | 仅 attach | 无需源码目录；自己启动服务后 app 挂接。`dshDir` 可留空，`url` 填实际地址 |
| 未启动任何服务 | managed | app 自动拉起（需要源码 + pnpm） |

## config.json 全部字段

```json
{
  "dshDir": "D:\\path\\to\\deepseek-harness",
  "url": "http://127.0.0.1:3080/",
  "pollTimeoutSec": 90,
  "startMaximized": false
}
```

- `dshDir`：dsh 源码目录（attach 模式下可省略）
- `url`：Web UI 地址（attach 模式必填，默认 `http://127.0.0.1:3080/`）
- `pollTimeoutSec`：服务就绪轮询超时（秒，默认 90）
- `startMaximized`：启动即最大化（默认 `false`）

配置解析顺序：`config.json`（exe 旁，首次运行自动生成）→ 环境变量 `DSH_DIR` → 相对 exe 向上查找。

## 下一步

- 遇到问题？看 [docs/TROUBLESHOOTING.md](TROUBLESHOOTING.md)
- 报 bug？用 [Issue 模板](../.github/ISSUE_TEMPLATE/bug_report.yml)（会自动收集环境信息）
