# 参与贡献

感谢你愿意改进 KeyStats。

## 开发环境

- Windows 10 或更高版本；
- x64 .NET 10 SDK；
- PowerShell 7 或 Windows PowerShell。

## 开发流程

1. Fork 或克隆仓库；
2. 从 `main` 创建功能分支；
3. 保持改动范围清晰，并为核心或存储逻辑补充回归测试；
4. 在提交前执行构建和测试；
5. 提交 Pull Request，说明动机、行为变化和验证方式。

```powershell
dotnet restore .\KeyStats.slnx
dotnet build .\KeyStats.slnx -c Release --no-restore
dotnet run --project .\tests\KeyStats.Core.Tests -c Release --no-build
```

## 代码约定

- 启用可空引用类型和警告即错误；
- 采集路径应避免阻塞、磁盘同步写入和高频对象分配；
- 不得新增输入内容、按键顺序、前台应用或鼠标位置的采集；
- 不得提交 `Data`、SQLite 数据库、导出 CSV、编译产物或个人测试数据；
- 涉及 UI 的改动请附截图，并说明 Windows 版本和缩放比例。

## 问题反馈建议

请尽量包含 Windows 版本、键盘型号、复现步骤、预期结果、实际结果，以及是否稳定复现。未知按键问题可以附界面显示的 Scan/VK/Flags，但不要上传包含个人数据的数据库。

