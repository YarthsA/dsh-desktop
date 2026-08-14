# 常见问题排查

先看日志：`%LOCALAPPDATA%\DshDesktop\app.log`（服务启动/退出/重启与错误均有记录，超过 1MB 自动轮转为 `app.log.old`）。报 issue 时请附上该文件内容。

## 启动即报「未找到 dsh 源码目录」

app 在 `dshDir`（config.json → `DSH_DIR` 环境变量 → 相对 exe 向上查找）中都没找到含 `package.json` 的 `deepseek-harness` 目录。

- 你有 dsh 源码：在 exe 旁 `config.json` 里把 `dshDir` 指向它
- 你没有源码、服务是自己用 `npx @deepseek-ai/dsh web` 启动的：走 **attach 模式**——不用配 `dshDir`，只要服务在跑、`url` 正确，app 探测到就直接挂接

## 「dsh 源码目录缺少 node_modules」

app 拒绝拉起源码目录里没装依赖的 dsh。在该目录执行：

```sh
pnpm install
```

## 「dsh 服务进程提前退出（exit code …）」

启动命令（`pnpm dsh web`）启动后立刻退出，常见原因：

- `pnpm` 不在 PATH（app 用隐藏窗口调用 `cmd /c`，读不到你某个 shell 里临时设置的 PATH）
- 端口 3080 已被其他程序占用（此时你本可以走 attach 模式：先自己把服务跑起来，或改 `config.json` 的 `url` 指到实际端口）
- 源码目录不完整 / `pnpm install` 没成功

## 窗口黑屏 / 空白

- **Win10**：需要自行安装 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
- 已知限制：主窗口圆角走 DWM（`AllowsTransparency=True` 会让 WebView2 黑屏），Splash 才用透明窗口。这不是故障
- WebView2 初始化失败会在 `app.log` 记录 `WebView2 初始化失败`，并弹窗后退出

## 托盘「退出」后服务还在跑

设计行为：app 只停**它自己拉起**的服务。如果你的服务是自己启动的（attach 模式），退出 app 不会动它。要停：

```bat
netstat -ano | findstr :3080      :: 拿到 LISTENING 的 PID
tasklist /FI "PID eq <pid>"       :: 看是 pnpm / node（attach 遗留）还是被 DshDesktop 托管
taskkill /PID <顶层PID> /T /F     :: 整棵进程树杀掉
```

> 注意：在 git-bash 里 `taskkill /PID` 会被 MSYS 路径转换搞坏（`/PID` 被当路径），加 `MSYS_NO_PATHCONV=1` 前缀或用 `cmd /c`。

## 目录选择器报 `directory picker failed: ... win32 folder dialog worker exited before reporting a result`

在 WebUI 里选项目目录时 dsh 报这个错。根因：dsh 的原生 picker 会另起一个 koffi 驱动的子进程调用 Win32 文件夹对话框，在桌面壳托管环境下该子进程可能退出且未回报结果；dsh 在 2026-08-04 已删除它的 PowerShell 兜底层，失败直接上抛。

修复（dsh-desktop 已内置，幂等）：

- **managed 模式**（app 托管 dsh 源码目录）：app 每次启动服务前自动检查并打「PowerShell 兜底」补丁，无需手工操作。dsh 重新 `pnpm run build` 或升级后补丁被冲掉，下次启动自动重打。
- **手动打补丁**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts\fix-directory-picker.ps1 -DshDir <dsh源码目录>
  ```
  补丁给 `packages\host\directory-picker-native\lib\index.js` 的 win32 分支加 try/catch：原生 worker 失败时降级到 PowerShell `FolderBrowserDialog`。
- 验证补丁是否生效：`scripts\verify-install.ps1` 的「Directory picker repair」一节会检查。

> 若补丁脚本输出 `anchor not found ... skipping`，说明 dsh 构建产物结构已变，需等 dsh-desktop 更新补丁脚本（向 [YarthsA/dsh-desktop](https://github.com/YarthsA/dsh-desktop) 提 issue）。

## 运行中服务意外停止 / 页面打不开

- app 每 15 秒探测一次：端口拒绝连接连续 2 次、或超时/异常持续约 90 秒，会自动重启服务并刷新页面；自动重启失败才弹窗询问
- 想手动排查：浏览器直接开 `http://127.0.0.1:3080` 看服务本身是否健康；看 `app.log` 的 `检测到 dsh 服务停止` 记录

## 双击第二个 app 没反应

单实例设计：第二个实例会唤醒已有窗口，然后自己退出。找托盘图标或任务栏已有窗口。

## 其他

- 服务地址不是 3080？改 `config.json` 的 `url`（attach 模式）；注意托管拉起（managed）模式固定用源码目录的默认端口
- 想彻底重新开始：删掉 exe 旁的 `config.json` 与 `%LOCALAPPDATA%\DshDesktop\app.log`，重开 app（会自动重新生成）
- 窗口最大化后折叠到托盘再恢复：正常（内部处理了 WPF 的 ShowActivated+Maximized 冲突）
