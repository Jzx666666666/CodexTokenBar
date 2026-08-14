# 贡献指南

感谢你考虑为 CodexTokenBar 贡献代码。这是一个独立、非官方的个人项目，我们不替 OpenAI 或 Codex CLI 代言。

## 开发环境

1. 安装 .NET 8 SDK[^dotnet-download][^dotnet-build]。
2. 安装并登录本地 `codex` CLI[^codex-cli]。应用不内置 CLI，也不复制其凭据。
3. 克隆仓库并切换到根目录。

## 分支命名

建议用短横线分隔的小写分支名，例如 `fix/gray-ring`、`feat/refresh-button` 或 `docs/architecture`。分支对应一个聚焦改进，避免一个分支混入多类改动。

## 构建与测试

在 PowerShell 中运行仓库的[构建脚本](scripts/build.ps1)（`scripts\build.ps1`）：

```powershell
.\scripts\build.ps1
```

该脚本先完成还原与 Debug 构建，再用 `dotnet .\tests\CodexTokenBar.Tests\bin\Debug\net8.0-windows\CodexTokenBar.Tests.dll` 运行 126 个自定义控制台测试，随后完成 Release 构建和 Windows x64 自包含单文件发布。期望结果是 `126/126` 测试通过，Debug 与 Release 均为 0 警告、0 错误，发布目录中只有一个 EXE。

本仓库当前不配置持续集成，请在提交前完整运行该脚本。

## 公开发布内容卫生

- 不要在 issue、PR 或日志中粘贴 Codex CLI 凭据、认证文件或原始配额载荷。
- 上报问题时只提供脱敏环境信息与可复现步骤，并优先使用[错误报告模板](.github/ISSUE_TEMPLATE/bug_report.yml)或[功能建议模板](.github/ISSUE_TEMPLATE/feature_request.yml)。
- 应用日志 `app.log` 至 `app.6.log` 位于 `%LocalAppData%\CodexTokenBar`，提交前请确认引用内容已脱敏。

## PR 检查清单

- 变更范围最小，仅包含与该 PR 目标相关的文件。
- 不触碰未要求的构建脚本、`csproj`、`.gitignore`、`.gitattributes` 或版本元数据。
- 新增或修改行为都配有对应测试，且完整测试套件通过。
- 文档随行为变化同步更新，不遗留未完成的占位标记或失效链接。
- 提交信息简洁，说明改动原因而非只罗列改动。

## 行为规范

请保持友善、具体并能接受建设性意见。对外发言时说明本项目与 OpenAI 无官方关联，避免给出未经证实的性能或兼容性承诺。

[^dotnet-download]: [.NET 8 下载](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
[^dotnet-build]: [dotnet build 命令行](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build)
[^codex-cli]: [OpenAI Codex CLI](https://developers.openai.com/codex/cli)
