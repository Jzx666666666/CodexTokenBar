# 架构说明

本文描述 CodexTokenBar v1.0.0 的内部结构与数据流。目标是让新读者能对照源码找到每个职责的位置。文中路径均相对仓库根目录。

## 总览

应用是 Windows 10/11 x64 上的任务栏配额指示器。它不内置 Codex CLI，也不直接调用远程配额端点。配额数据来自本机 `codex app-server` JSON-RPC 标准输入输出子进程，读取后经展示层渲染为任务栏圆环。主逻辑位于 [`src/CodexTokenBar`](../src/CodexTokenBar)，按领域划分成 `Codex`、`Domain`、`Lifecycle`、`Monitoring`、`Persistence`、`Taskbar`、`Tray`、`UI`、`Windows` 等目录。

## 进程与生命周期

入口是 [`App.xaml.cs`](../src/CodexTokenBar/App.xaml.cs) 与 [`Lifecycle/ApplicationLifecycle.cs`](../src/CodexTokenBar/Lifecycle/ApplicationLifecycle.cs)。应用保证单实例运行：[SingleInstanceCoordinator](../src/CodexTokenBar/Windows/SingleInstanceCoordinator.cs) 检测并拒绝重复实例。启动时定位 `codex` 可执行文件，随后建立本地子进程。[PowerResumeWatcher](../src/CodexTokenBar/Windows/PowerResumeWatcher.cs) 负责从睡眠恢复后重新评估状态。

## JSON-RPC 握手与配额读取

[`CodexCommandLocator`](../src/CodexTokenBar/Codex/CodexCommandLocator.cs) 定位已安装的 `codex` 命令行，[`CodexConnectionFactory`](../src/CodexTokenBar/Codex/CodexConnectionFactory.cs) 创建与 `codex app-server` 之间的标准输入输出连接。[`CodexProcessSupervisor`](../src/CodexTokenBar/Codex/CodexProcessSupervisor.cs) 管理该子进程的生命周期与重启，[`CodexAppServerClient`](../src/CodexTokenBar/Codex/CodexAppServerClient.cs) 承载 JSON-RPC 客户端逻辑。

流程为：[`UsageMonitor`](../src/CodexTokenBar/Monitoring/UsageMonitor.cs) 按调度周期调用并接收 [`CodexRateLimitReader`](../src/CodexTokenBar/Codex/CodexRateLimitReader.cs)；读取器通过连接向 `app-server` 发起限额请求并把响应解析成额度池快照；`UsageMonitor` 再把快照交给 [`QuotaSelector`](../src/CodexTokenBar/Domain/QuotaSelector.cs) 选择周额度。协议模型定义在 [`CodexProtocolModels.cs`](../src/CodexTokenBar/Codex/CodexProtocolModels.cs) 与 [`RateLimitModels.cs`](../src/CodexTokenBar/Domain/RateLimitModels.cs)。

## 配额选择与新鲜度

[`QuotaSelector`](../src/CodexTokenBar/Domain/QuotaSelector.cs) 在响应中选取常规 `limitId=codex` 的 10080 分钟周窗口，其他周池不参与展示主圆环。[`UsageState`](../src/CodexTokenBar/Domain/UsageState.cs) 表达配额状态，[`RetrySchedule`](../src/CodexTokenBar/Domain/RetrySchedule.cs) 计算重试间隔，[`QuotaPresentation`](../src/CodexTokenBar/Domain/QuotaPresentation.cs) 定义面向展示层的精简模型。

数值新鲜度由监测层维护：当读取失败、响应过期或内容不可信时，展示层转入灰色与长破折号（em dash）的过期态，而不是展示陈旧数据。

## 任务栏锚定与几何

[`TaskbarAnchor`](../src/CodexTokenBar/Taskbar/TaskbarAnchor.cs) 与 [`TaskbarAnchorCalculator`](../src/CodexTokenBar/Taskbar/TaskbarAnchorCalculator.cs) 计算主显示器任务栏托盘槽位的位置。[`WindowsTaskbarAnchorProbe`](../src/CodexTokenBar/Taskbar/WindowsTaskbarAnchorProbe.cs) 与 [`ITaskbarAnchorProbe`](../src/CodexTokenBar/Taskbar/ITaskbarAnchorProbe.cs) 探测锚定是否可用。槽位固定为 64 DIP，位于隐藏图标箭头左侧；圆环直径在 24 到 40 DIP 之间自适应。

## WPF 渲染

[`TaskbarRingRenderer`](../src/CodexTokenBar/Taskbar/TaskbarRingRenderer.cs) 只把配额状态绘制成圆弧位图：圆环从 12 点钟开始，顺时针灰弧表示已消耗配额，阈值色弧表示剩余配额。剩余大于 30% 使用绿色，10% 至 30% 使用黄色，小于 10% 使用红色；这些阈值和颜色由 `TaskbarRingRenderer` 负责。

中心整数百分比或过期态长破折号由 [`TaskbarQuotaPresentation`](../src/CodexTokenBar/Taskbar/TaskbarQuotaPresentation.cs) 生成文本，再由 [`TaskbarQuotaWindow.xaml`](../src/CodexTokenBar/Taskbar/TaskbarQuotaWindow.xaml) 中的 WPF `TextBlock` 显示。[`TaskbarVisualMetrics`](../src/CodexTokenBar/Taskbar/TaskbarVisualMetrics.cs) 只计算圆环直径、笔画宽度、字号和圆角等尺寸布局值，不提供额度颜色阈值。悬浮窗口逻辑位于 [`TaskbarQuotaWindow.xaml.cs`](../src/CodexTokenBar/Taskbar/TaskbarQuotaWindow.xaml.cs)。摘要面板由 [`SummaryWindow.xaml.cs`](../src/CodexTokenBar/UI/SummaryWindow.xaml.cs) 与 [`SummaryViewModel.cs`](../src/CodexTokenBar/UI/SummaryViewModel.cs) 维护。

## 交互与 z 序恢复

左键单击切换摘要面板的显示并触发刷新；右键单击打开托盘菜单；双击不会触发重复的单次点击行为。[`TaskbarOverlayCoordinator`](../src/CodexTokenBar/Taskbar/TaskbarOverlayCoordinator.cs) 协调覆盖层与托盘图标的切换。

覆盖层可能被顶部窗口遮挡。[`WindowsTaskbarZOrderWatcher`](../src/CodexTokenBar/Taskbar/WindowsTaskbarZOrderWatcher.cs) 与 [`ITaskbarZOrderWatcher`](../src/CodexTokenBar/Taskbar/ITaskbarZOrderWatcher.cs) 监听前景与重排 WinEvents，并在 50 毫秒后再次确认 z 序；另有 2 秒几何看门狗作为兜底。

## 回退行为

若任务栏锚定不可用，覆盖层隐藏，[`TrayIconController`](../src/CodexTokenBar/Tray/TrayIconController.cs) 与 [`TrayIconRenderer`](../src/CodexTokenBar/Tray/TrayIconRenderer.cs) 恢复标准系统通知图标的展示。托盘仍保留右键菜单与摘要入口。

## 持久化

[`AppPaths`](../src/CodexTokenBar/Persistence/AppPaths.cs) 解析本机数据根目录 `%LocalAppData%\CodexTokenBar`。[`AppSettings`](../src/CodexTokenBar/Persistence/AppSettings.cs) 读写 `settings.json`，[`AppStateStore`](../src/CodexTokenBar/Persistence/AppStateStore.cs) 保存最近一次成功的 `snapshot.json`，[`RollingLog`](../src/CodexTokenBar/Persistence/RollingLog.cs) 在同一目录维护 `app.log` 与 `app.1.log` 至 `app.6.log`，共计最多七个脱敏日志，每个不超过 1 MB。

应用不保存长期配额历史，也不发送远程遥测。

## 关闭流程

退出由 [`ApplicationLifecycle`](../src/CodexTokenBar/Lifecycle/ApplicationLifecycle.cs) 统一协调：停止监测调度、关闭 app-server 子进程、落盘设置与快照、清理悬浮窗口，最后释放托盘图标。任何路径都应避免留下孤儿子进程或损坏的状态文件。

## 安全边界

本应用只连接本机 `codex app-server` 子进程，不直接访问远程配额端点。这个边界不表示 Codex CLI 完全离线；CLI 为完成认证和自身功能，可能按其设计与 OpenAI 服务通信。应用不读取或复制 Codex CLI 凭据，也不应把原始配额载荷或未脱敏日志发布到公开渠道。[`CodexProtocolModels.cs`](../src/CodexTokenBar/Codex/CodexProtocolModels.cs) 与 [`RateLimitModels.cs`](../src/CodexTokenBar/Domain/RateLimitModels.cs) 只处理结构化限额数据，避免将未经校验的内容直接当作文本渲染。

## 测试对应

测试位于 [`tests/CodexTokenBar.Tests`](../tests/CodexTokenBar.Tests)，覆盖领域、JSON-RPC、进程监督、持久化、任务栏锚定、覆盖层、z 序看门狗、渲染、托盘、使用监测、视图模型与 Windows 适配。[`tests/CodexTokenBar.FakeAppServer`](../tests/CodexTokenBar.FakeAppServer) 提供假 app-server。当前构建证据为 `126/126` 测试通过，Debug 与 Release 均 0 警告、0 错误。
