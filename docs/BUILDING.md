# 构建与发布

## 环境

- Windows x64；
- .NET 10 SDK；
- PowerShell。

仓库的 `global.json` 指定 .NET SDK 10.0.400，并允许使用同一功能带的更新补丁版本。

## 构建

```powershell
dotnet restore .\KeyStats.slnx
dotnet build .\KeyStats.slnx -c Release --no-restore
```

## 回归测试

测试项目是一个无第三方测试框架的控制台程序：

```powershell
dotnet run --project .\tests\KeyStats.Core.Tests -c Release --no-build
```

进程返回码为 0 表示全部通过。

## 离线或受限环境

- NuGet 漏洞审计需要联网获取漏洞库，离线时会以 `NU1900` 失败；由于仓库开启 `TreatWarningsAsErrors`，
  该警告会被升级为错误，此时可追加 `-p:NuGetAudit=false`；
- 受限（沙箱/容器）环境下 MSBuild 的多进程节点依赖命名管道，可能整体构建失败且没有报错信息，
  可追加 `-m:1 -nodeReuse:false` 强制单节点。

## 脚本编码

`scripts/` 下的 PowerShell 脚本包含中文，必须保存为 **UTF-8 带 BOM**。
在中文 Windows 上，缺少 BOM 的 `.ps1` 会被按 GBK 解码，导致中文串损坏，甚至出现语法错误而无法运行。

## 发行包

```powershell
.\scripts\publish.ps1
```

脚本会生成：

- `KeyStats-V1.0-full-win-x64.zip`：包含 .NET 桌面运行时；
- `KeyStats-V1.0-lite-win-x64.zip`：依赖 x64 .NET 10 Desktop Runtime；
- `SHA256SUMS.txt`：两个 ZIP 的 SHA-256。

