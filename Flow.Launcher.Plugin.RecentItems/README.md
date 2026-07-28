# History Box

适用于 Flow Launcher 2.0 及以上版本。

## 功能

- 输入框为空时，在主页显示最近使用的文件和文件夹。
- 输入 `rf 关键词` 可筛选最近项目。
- 每条结果使用文件自身的 Windows 缩略图或文件类型图标。
- 文件夹按 Enter 直接打开。
- 普通文件按 Enter 使用 Windows 默认方式打开。
- 按 `Ctrl+C` 可复制目标路径。
- 按右方向键、`Ctrl+O` 或 `Shift+Enter` 打开结果操作菜单，可持久置顶或取消置顶。
- 普通文件的操作菜单提供“打开所在位置”，并在文件资源管理器中选中文件。
- 文件夹的操作菜单不显示“打开所在位置”。

最近项目读取自 Windows 的 `%APPDATA%\Microsoft\Windows\Recent`。

## 安装

插件通过 Flow Launcher 官方插件商店审核后，可在插件商店搜索 `History Box` 安装。

审核期间也可从 [Releases](https://github.com/lei0620/Flow.Launcher.Plugin.RecentItems/releases)
下载 ZIP，在 Flow Launcher 的插件商店中选择“从本地路径安装插件”。

安装并重启 Flow Launcher 后，在“设置 → 插件 → 主页”中确认本插件已启用。
