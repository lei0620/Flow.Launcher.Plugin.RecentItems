# 最近文件与文件夹

一个适用于 Flow Launcher 2.0 及以上版本的 Windows 插件。打开 Flow Launcher
且输入框为空时，它会直接显示最近使用的文件和文件夹。

## 功能

- 空白主页显示最近 15 个文件和文件夹。
- 输入 `rf 关键词` 筛选最近项目。
- 使用 Windows 缩略图或文件类型图标。
- 按 Enter 使用系统默认方式打开。
- 按 `Ctrl+C` 复制目标路径。
- 数据仅从本机的 `%APPDATA%\Microsoft\Windows\Recent` 读取。

## 安装

插件通过 Flow Launcher 官方插件商店审核后，可在插件商店中搜索“最近文件与文件夹”安装。

也可以从 [Releases](https://github.com/lei0620/Flow.Launcher.Plugin.RecentItems/releases)
下载 ZIP，在 Flow Launcher 的插件商店中选择“从本地路径安装插件”。

## 开发与验证

```powershell
dotnet build .\Flow.Launcher.Plugin.RecentItems\Flow.Launcher.Plugin.RecentItems.csproj -c Release
dotnet run --project .\tests\RecentItems.SmokeTest\RecentItems.SmokeTest.csproj -c Release
```

## 许可证

源代码使用 MIT License。图标来源和许可见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
