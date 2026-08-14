# 更新日志

本项目遵循 Keep a Changelog 规范。

## [1.0.0] - 2026-08-14

首个正式发布版本。这是一个针对 Windows 10/11 x64 的本地配额圆环工具，独立、非官方，与 OpenAI 无关联。

### Added

- 在主显示器任务栏托盘槽位渲染剩余周配额的圆环，支持 Green、Yellow、Red 阈值色与灰白过期状态。
- 通过本地 `codex app-server` JSON-RPC 标准输入输出子进程读取配额，不依赖非本机端点。
- 左键单击展开或收起摘要面板，右键单击打开应用菜单，双击不会触发重复的单次点击。
- 任务栏锚定不可用时自动隐藏覆盖层并恢复标准系统通知图标。
- 事件驱动 z 序恢复（前景与重排 WinEvents 加 50 毫秒尾部重断言）与 2 秒几何看门狗。
- 本地数据根目录 `%LocalAppData%\CodexTokenBar`，包含设置、最近成功快照与最多七个脱敏滚动日志（每个最多 1 MB）。

### Verified

- 构建证据：`126/126` 测试通过，Debug 与 Release 均 0 警告、0 错误。
- 发布方式：Windows x64 自包含单文件，无需额外安装 .NET 运行时。
- 实测环境：仅限 Windows 11 x64 双显示器；Windows 10、第三方任务栏、全部 DPI 组合以及未来 Windows 版本不在已验证范围。

### Known limitations

- EXE 尚未签名，可能触发 Windows 来源或信誉警告。
- 不提供长期配额历史、预测、通知、账户切换、五小时配额展示、钱包、下单或远程遥测。
- 任务栏锚定依赖系统行为；第三方任务栏或 Explorer 变化可能使锚定失效并回退到通知图标。

[1.0.0]: https://github.com/Jzx666666666/CodexTokenBar/releases/tag/v1.0.0
