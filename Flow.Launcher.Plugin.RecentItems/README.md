# History Box

适用于 Flow Launcher 2.0 及以上版本。

## 功能

- 输入框为空时，只在主页显示已固定的文件和文件夹。
- 直接输入关键词时继续由 History Box 搜索最近项目，选中结果可正常打开或使用结果操作菜单。
- 输入 `rf` 也可显式进入 History Box，输入 `rf 关键词` 可筛选最近项目。
- 每条结果使用文件自身的 Windows 缩略图或文件类型图标。
- 文件夹按 Enter 直接打开。
- 普通文件按 Enter 使用 Windows 默认方式打开。
- 按 `Ctrl+C` 可复制目标路径。
- 文件和文件夹都可通过结果操作菜单“固定到主页”或“从主页移除”。
- Flow 自带的“在当前查询中置顶”只影响当前搜索，与 History Box 主页固定互不混用。
- 普通文件的操作菜单提供“打开所在位置”，并在文件资源管理器中选中文件。
- 文件夹的操作菜单不显示“打开所在位置”。

最近项目读取自 Windows 的 `%APPDATA%\Microsoft\Windows\Recent`。
插件同时支持全局关键词 `*` 和显式关键词 `rf`。History Box 的主页、搜索记录键与文件管理扩展相互隔离，搜索结果会用 `History Box 搜索` 标明来源。

## 安装

插件通过 Flow Launcher 官方插件商店审核后，可在插件商店搜索 `History Box` 安装。

审核期间也可从 [Releases](https://github.com/lei0620/Flow.Launcher.Plugin.RecentItems/releases)
下载 ZIP，在 Flow Launcher 的插件商店中选择“从本地路径安装插件”。

安装并重启 Flow Launcher 后，在“设置 → 插件 → 主页”中确认本插件已启用。
