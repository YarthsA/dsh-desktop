# 给 agent 的 prompt（Claude Code / Codex）

想让 Claude Code / Codex 帮你完成 dsh-desktop 的安装、配置或排查？把下面对应场景的 prompt 整段粘进 agent 会话即可。

**使用说明**
- 把 `<<...>>` 里的占位符换成你的实际值（目录、用户名等）
- 建议先让 agent 阅读本仓库的 `README.md`、`docs/QUICKSTART.md`，避免它臆测
- 两份 prompt 都假设 agent 运行在 Windows 11 上；agent 需要能访问终端与文件系统

---

## 已知安装坑（给 agent 的教训，先读再动手）

1. **.NET 10 Desktop Runtime 必须机器级安装**：apphost 只认 `C:\Program Files\dotnet`。用 dotnet-install 脚本装用户目录版（`%LOCALAPPDATA%\Microsoft\dotnet`）不生效，DshDesktop.exe 仍报 "You must install or update .NET"。必须 `winget install Microsoft.DotNet.DesktopRuntime.10`（弹 UAC，需用户点确认）。这是唯一必须人工接管的一步。
2. **git-bash / MSYS 下 `taskkill /PID <pid> /T /F` 会被路径转换搞坏**（`/PID` 被当成路径）。用 `MSYS_NO_PATHCONV=1 taskkill ...` 或 `cmd /c "taskkill ..."`。
3. **孤儿服务**：attach 模式下 app 退出不停服务。识别：`netstat -ano | findstr :3080` 拿到 PID，再 `tasklist /FI "PID eq <pid>"` 看是不是 pnpm/node。清理：`taskkill /PID <顶层进程> /T /F`。
4. **验证用一次性脚本，不要分步手工探测**：`powershell -ExecutionPolicy Bypass -File scripts\verify-install.ps1 -AppDir <app目录>`，一条命令输出 PASS/FAIL（前置、config、选择器补丁、端口 3080、HTTP 200、服务归属）。
5. **目录选择器报 `directory picker failed: ... win32 folder dialog worker exited before reporting a result`**：dsh 的原生 picker 子进程在桌面壳托管环境可能无结果退出，且 dsh 已删除其 PowerShell 兜底层。managed 模式下 app 启动服务前会自动打幂等补丁恢复兜底（`scripts\fix-directory-picker.ps1`）；若补丁未生效，手动跑该脚本并检查输出。

---

## Prompt A：从零开始（没有 Web UI，什么都没有）

适用：你还没有 dsh 的 Web UI，需要 agent 帮你从零搭好环境，最后能用 dsh-desktop。

````text
任务：在一台 Windows 11 机器上，从零搭建 DeepSeek Harness 的 Web UI，并用 dsh-desktop 桌面壳使用它。

背景：
- dsh-desktop（https://github.com/YarthsA/dsh-desktop）只是一个桌面壳：它渲染 dsh Web UI（默认 http://127.0.0.1:3080）并托管服务进程，本身不提供任何 AI 能力。
- 要真正跑任务还需要：dsh Web UI 在运行 + 一个模型 Provider（至少一个 DeepSeek API key）。
- Claude Code / Codex CLI 是可选的 subagent 后端，不是必需——请先确认用户是否需要。
- 请先阅读 dsh-desktop 的 README.md 和 docs/QUICKSTART.md，再动手。

环境（请先检查再执行，不要假设）：
- Windows 版本、Node.js / pnpm 是否已安装且在 PATH
- 是否已有 DEEPSEEK_API_KEY

步骤（每步完成后验证，不要跳过）：
1. 安装/确认 Node.js（>= 22.19）与 pnpm；确保 pnpm 在 PATH。
2. git clone https://github.com/deepseek-ai/deepseek-harness.git 到 <<D:\path\to\deepseek-harness>>。
3. 在克隆目录执行 pnpm install。
4. 询问用户是否需要 subagent 委派；需要则指导安装 Claude Code 或 Codex CLI（不要擅自全局安装）。
5. 启动验证 Web UI：pnpm dsh web（或 npx @deepseek-ai/dsh web），确认 http://127.0.0.1:3080 返回 200。
6. 告诉用户在 Web UI 的 Settings → Models 配置 API key（这一步需要用户自己填，不能替你操作）。
7. 构建 dsh-desktop：`powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1`（框架依赖，目标机需 .NET 10 Desktop Runtime；或 `-SelfContained` 免装运行时；也可以直接下载 Release zip）。
8. 在 exe 旁创建 config.json，dshDir 指向第 2 步的克隆目录；用 `scripts\verify-install.ps1 -AppDir <app目录>` 验证（前置 + 服务 + 归属一次性输出），再双击 DshDesktop.exe 确认窗口加载 Web UI。
9. 交付总结：报告最终 config.json 内容、服务由谁托管（attach 还是 managed）、托盘退出行为、verify-install.ps1 的最终结果。

约束：
- 不要修改 deepseek-harness 与 dsh-desktop 的任何源码。
- 遇到 WebView2 / 黑屏 / 端口问题，参考 dsh-desktop 的 docs/TROUBLESHOOTING.md。
- 每步的失败都要停下来分析原因，而不是盲目重试。
````

---

## Prompt B：已有 Web UI，配置 / 排查 dsh-desktop

适用：你的 dsh Web UI 已经能用（浏览器能打开），但 dsh-desktop 装好或配置有问题。

````text
任务：用户的 DeepSeek Harness Web UI 已可用（http://127.0.0.1:3080 能打开），请帮我配置/排查 dsh-desktop 桌面壳并解释它的行为。

背景：
- dsh-desktop 的行为：启动时先探测 url 指向的端口；若服务已在运行则直接挂接（attach，不重复拉起）；若配置了有效的 dshDir（含 node_modules）且服务没在跑，才由它托管拉起（managed）。
- 托盘「退出」只停它自己拉起的服务；attach 模式下退出不会停你的服务——这是设计行为。
- 日志在 %LOCALAPPDATA%\DshDesktop\app.log。
- 请先阅读 dsh-desktop 的 README.md 和 docs/TROUBLESHOOTING.md。

步骤：
1. 确认现状：探测 http://127.0.0.1:3080 是否 200；用 netstat 找出占用端口的进程（PID/命令行），判断服务是源码还是 npx 方式启动的。
2. 检查 exe 旁 config.json（不存在则说明解析走的是 DSH_DIR 或向上查找；首次运行会自动生成）。
3. 判断模式：
   - 服务是你自己启动的（attach）→ 确保 url 正确，dshDir 可留空；明确告知用户退出 app 不会停服务。
   - 想让 app 托管 → dshDir 指向源码目录（含 node_modules），退出时服务会被停止。
4. 启动 DshDesktop.exe，观察 app.log 的启动过程（探测、拉起、就绪）。
5. 排查用户报告的具体问题（黑屏、端口占用、退出后服务残留等），按 TROUBLESHOOTING.md 定位。
6. 输出结论：当前 attach 还是 managed 模式、退出时服务行为、问题根因与修复、遗留风险。

约束：
- 不要修改任何源码；配置层面的改动（config.json）先说明再做。
- 服务是否重启/停止这类有副作用的操作，先征得用户同意。
````
