# Changelog

## [Unreleased]

预发布开发阶段。以下改动均已合入工作区；版本号归零，
待优化到准备发布 GitHub 时再定 0.1.0（打 `v0.1.0` tag 触发 release 工作流）。

### Added

- WebView2 + C# WPF 桌面壳，渲染 dsh WebUI（无边框 WindowChrome + DWM 圆角）
- 启动 Splash：透明鲸鱼图标 + 白色荧光光晕，非阻塞脉动
- 系统托盘常驻：关窗折叠、双击恢复、黑色圆角菜单（显示/退出）
- 服务托管：隐藏拉起 `pnpm dsh web`，探测就绪，托盘退出时整树回收
- 单实例互斥 + 第二实例唤醒已有窗口
- 配置驱动：config.json / DSH_DIR / 相对回退，无硬编码绝对路径
