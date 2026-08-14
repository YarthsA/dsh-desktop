# Changelog

## [Unreleased]

## [0.1.0] - 2026-08-14

首个公开版本：把 DeepSeek Harness 的 WebUI 封装成 Windows 桌面壳（WebView2 + C# WPF）。

### Added

- WebView2 + C# WPF 桌面壳，渲染 dsh WebUI（无边框 WindowChrome + DWM 圆角）
- 启动 Splash：透明鲸鱼图标 + 白色荧光光晕，非阻塞脉动
- 系统托盘常驻：关窗折叠、双击恢复、黑色圆角菜单（显示/退出）
- 服务托管：隐藏拉起 `pnpm dsh web`，探测就绪，托盘退出时整树回收
- 单实例互斥 + 第二实例唤醒已有窗口
- 配置驱动：config.json / DSH_DIR / 相对回退，无硬编码绝对路径

### Changed

- 健康检查细化：探测只读响应头（不再下载整页），超时放宽到 3s；仅"端口拒绝连接"连续两次触发重启，"超时/异常"需连续约 90 秒（避免误杀响应慢的活服务）
- 退出时等待 taskkill 完成并确认进程树真正退出，避免退出后立即重开 App 连到旧服务（端口被占）
- DevTools 窗口匹配按 msedgewebview2 进程过滤（不再误置顶其他应用的 DevTools），等待改为异步，不再阻塞 UI 线程
- WebView2 进程异常退出自动重载兜底，30 秒内崩溃超 3 次才停止（防死循环）；初始化失败时停服务退出，不再留空窗口
- app.log 超过 1MB 自动轮转为 app.log.old
- 托盘菜单尺寸/颜色提取为常量，宽度按当前 DPI 实测（高 DPI 不裁字）
- run-dsh-web.cmd 末尾透传 exit code
- 启动命令收敛为 `scripts/run-dsh-web.cmd` 单一来源，C# 侧不再各自拼接 cmdline
- 服务启动前预检 dshDir / node_modules；启动的进程提前退出时快速报错（不再空等超时）
- 运行期掉线自愈：每 15s 探测，连续两次失败自动重启服务并刷新页面（失败才询问）
- 第二实例监听线程改为 1s 超时轮询 + 可退出标志，规避退出竞态下的未捕获异常
- 单实例互斥量处理 abandoned 状态（上次进程异常退出后不再启动崩溃）
- 相对回退路径改为向上查找 `deepseek-harness`（同名目录 + package.json），去掉硬编码层级
- 新增 `%LOCALAPPDATA%\DshDesktop\app.log` 日志，服务启停/重启/错误可追溯
- WebView2 背景色与窗口一致，消除加载白屏闪烁
- 标题栏新增「刷新」「开发者工具」按钮
- 标题栏新增最大化/还原按钮，新增 `startMaximized` 配置（默认普通窗口，可改为启动最大化）
- 最大化不再遮任务栏：WM_GETMINMAXINFO 把最大尺寸钳制到所在监视器工作区
- WebView2 四周留边距，边缘缩放指针/拖拽恢复正常（子 HWND 不再盖住窗口边缘）
- DevTools 打开后自动置顶激活，避免被主窗口抢回前台
- 托盘恢复窗口改用前台锁规避（模拟 Alt 键），不再出现任务栏红色闪烁
- 修复：第二实例监听线程误把超时当信号，每秒调用 ShowFromTray 抢前台（导致所有非最大化窗口被抢占）
- 修复：窗口最大化后折叠到托盘再「显示」时，ShowActivated=false + Maximized 触发 WPF 异常崩溃
- 兜底：未处理异常改为记日志 + 友好提示，不再弹 .NET 默认异常对话框
