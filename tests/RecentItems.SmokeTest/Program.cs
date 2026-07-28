using Flow.Launcher.Plugin.RecentItems;

var plugin = new Main();
var results = await plugin.HomeQueryAsync(CancellationToken.None);

Console.WriteLine($"RESULT_COUNT={results.Count}");
foreach (var result in results.Take(5))
{
    Console.WriteLine($"{result.Title}\t{result.CopyText}");
}

if (results.Count == 0)
{
    throw new InvalidOperationException("主页查询没有返回任何结果。");
}

if (results.Any(result => string.IsNullOrWhiteSpace(result.Title)))
{
    throw new InvalidOperationException("存在标题为空的结果。");
}

var actionableResults = results.Where(result => result.Action is not null).ToList();
if (actionableResults.Any(result => !string.Equals(
        result.IcoPath,
        result.CopyText,
        StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("存在未使用目标文件路径作为缩略图来源的结果。");
}

foreach (var result in actionableResults.Take(5))
{
    var contextMenus = plugin.LoadContextMenus(result);
    var menuTitles = contextMenus.Select(menu => menu.Title).ToHashSet();
    var isDirectory = result.SubTitle.Contains("· 文件夹 ·", StringComparison.Ordinal);

    if (!menuTitles.Contains("置顶") && !menuTitles.Contains("取消置顶"))
    {
        throw new InvalidOperationException("最近项目缺少插件置顶操作。");
    }

    if (isDirectory && menuTitles.Contains("打开所在位置"))
    {
        throw new InvalidOperationException("文件夹不应显示打开所在位置操作。");
    }

    if (!isDirectory && !menuTitles.Contains("打开所在位置"))
    {
        throw new InvalidOperationException("普通文件缺少打开所在位置操作。");
    }

    if (contextMenus.Any(menu => menu.Action is null))
    {
        throw new InvalidOperationException("最近项目操作菜单中存在不可执行项。");
    }

    var openLocationMenu = contextMenus.SingleOrDefault(menu => menu.Title == "打开所在位置");
    if (openLocationMenu is not null &&
        openLocationMenu.IcoPath != "Images\\folder-open.svg")
    {
        throw new InvalidOperationException("打开所在位置没有使用独立文件夹图标。");
    }
}

if (actionableResults.Count >= 2)
{
    var itemToPin = actionableResults[^1];
    var pinMenu = plugin.LoadContextMenus(itemToPin)
        .Single(menu => menu.Title == "置顶");

    if (pinMenu.IcoPath != "Images\\pin.svg" || pinMenu.Action!(null!))
    {
        throw new InvalidOperationException("置顶操作的图标或窗口保持行为不正确。");
    }

    var pinnedResults = await plugin.HomeQueryAsync(CancellationToken.None);
    if (!string.Equals(
            pinnedResults[0].CopyText,
            itemToPin.CopyText,
            StringComparison.OrdinalIgnoreCase) ||
        !pinnedResults[0].SubTitle.StartsWith("已置顶", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("置顶项目没有固定到最近项目列表顶部。");
    }

    var unpinMenu = plugin.LoadContextMenus(pinnedResults[0])
        .Single(menu => menu.Title == "取消置顶");
    if (unpinMenu.IcoPath != "Images\\pin-off.svg" || unpinMenu.Action!(null!))
    {
        throw new InvalidOperationException("取消置顶操作的图标或窗口保持行为不正确。");
    }

    var unpinnedResults = await plugin.HomeQueryAsync(CancellationToken.None);
    var unpinnedItem = unpinnedResults.Single(
        result => string.Equals(
            result.CopyText,
            itemToPin.CopyText,
            StringComparison.OrdinalIgnoreCase));
    if (unpinnedItem.SubTitle.StartsWith("已置顶", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("取消置顶后项目仍显示为已置顶。");
    }
}
