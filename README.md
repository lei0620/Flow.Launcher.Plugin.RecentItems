# History Box

一个适用于 Flow Launcher 2.0 及以上版本的 Windows 插件。打开 Flow Launcher
且输入框为空时，它会直接显示最近使用的文件和文件夹。

## 功能

- 空白主页显示最近 15 个文件和文件夹。
- 输入 `rf 关键词` 筛选最近项目。
- 使用 Windows 缩略图或文件类型图标。
- 文件夹按 Enter 直接打开，普通文件按 Enter 使用系统默认方式打开。
- 按 `Ctrl+C` 复制目标路径。
- 通过结果操作菜单持久置顶/取消置顶，不显示成功提示。
- 普通文件可在文件资源管理器中打开所在位置；文件夹不显示此操作。
- 数据仅从本机的 `%APPDATA%\Microsoft\Windows\Recent` 读取。

可按右方向键、`Ctrl+O` 或 `Shift+Enter` 打开结果操作菜单。

## 安装

插件通过 Flow Launcher 官方插件商店审核后，可在插件商店中搜索 `History Box` 安装。

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
