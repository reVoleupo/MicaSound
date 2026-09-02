# 微声 MicaSound

基于 **WinUI 3 + Fluent Design** 的第三方网易云音乐桌面客户端。

使用 Mica 背景材质与封面取色,内嵌自托管的网易云音乐 API 进程,主打登录收藏、个性化推荐与流畅听歌体验。

## 技术栈

- C# / .NET 8 · WinUI 3 (Windows App SDK)
- CommunityToolkit.Mvvm
- LibVLC (libvlcsharp) 播放引擎(规划)
- SQLite 本地存储
- 内嵌 Node.js 自托管 API 子进程

## 仓库结构

```
src/
├─ MicaSound.Core/       # 纯逻辑类库(LRC 解析、数据模型)
├─ MicaSound.ApiHost/    # API 进程托管 + HTTP 桥接(ApiBridge)
├─ MicaSound.Cli/        # 命令行冒烟测试
└─ MicaSound.App/        # WinUI 3 主程序(规划)
scripts/
├─ ensure-api.ps1        # 克隆并准备自托管 API 服务
└─ api-boot.js           # 绕过 generateConfig 卡死的启动器
.github/workflows/       # GitHub Actions 构建
```

## 快速开始

```powershell
# 1. 准备自托管 API 服务(克隆 Asplla fork + npm install)
powershell -ExecutionPolicy Bypass -File scripts/ensure-api.ps1

# 2. 构建
dotnet build MicaSound.sln -c Release

# 3. 运行冒烟测试(拉起进程 → 搜索 → 歌词解析 → 播放链接)
dotnet run --project src/MicaSound.Cli/MicaSound.Cli.csproj -c Release
```

## CI

推送至 `main` 或发起 PR 时,GitHub Actions 自动在 `windows-latest` 上:

1. 还原 + 编译解决方案(Release)
2. 运行 CLI 冒烟测试

## 免责声明

本项目为学习与技术演示用途,UI 与数据来源来自第三方非官方接口,请遵守网易云音乐相关服务条款,勿用于商业用途。