# Changelog

## [0.2.0] - 2026-08-14

### Fixed
- 标题栏最小化/关闭按钮失效、悬停无效果：WindowChrome 的 caption 区域默认吞掉鼠标事件，给按钮面板加 `WindowChrome.IsHitTestVisibleInChrome`
- 窗口顶部小白边：改用 WindowChrome（`GlassFrameThickness=0`）消除系统 resize 边框

### Added
- 桌面窗口 DWM 圆角（`DWMWA_WINDOW_CORNER_PREFERENCE`）
- Splash 屏：透明鲸鱼图标放大 + 荧光光晕 + 减速闪烁
- 托盘菜单：黑色圆角、文字居中、面积与字体加大

### Changed
- ServiceHost 改为配置驱动（`config.json` / `DSH_DIR` / 相对默认），移除硬编码绝对路径，便于开源复用
- 服务脚本迁移至 `scripts/run-dsh-web.cmd` 并参数化

## [0.1.0] - 2026-08-14

### Added
- WebView2 + C# WPF 桌面壳，渲染 dsh WebUI
- 单实例互斥 + 第二实例唤醒已有窗口
- 服务托管：隐藏拉起 `pnpm dsh web`，探测就绪，托盘退出时整树回收
- 系统托盘常驻：关窗折叠、双击恢复、右键显示/退出
