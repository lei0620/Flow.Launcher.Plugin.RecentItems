using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.RecentItems;

var testRoot = Path.Combine(
    Path.GetTempPath(),
    $"HistoryBoxSmoke-{Guid.NewGuid():N}");
var firstFolder = Path.Combine(testRoot, "Pinned-A");
var secondFolder = Path.Combine(testRoot, "Pinned-B");
var filePath = Path.Combine(testRoot, "not-a-home-folder.txt");
var liveRecentFile = Path.Combine(testRoot, "live-recent-file.txt");
var recentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
var recentShortcut = Path.Combine(
    recentDirectory,
    $"HistoryBoxSmoke-{Guid.NewGuid():N}.lnk");

Directory.CreateDirectory(firstFolder);
Directory.CreateDirectory(secondFolder);
File.WriteAllText(filePath, "smoke test");
File.WriteAllText(liveRecentFile, "live refresh smoke test");

try
{
    var plugin = new Main();
    SetPinnedPaths(
        plugin,
        [
            firstFolder + Path.DirectorySeparatorChar,
            secondFolder,
            firstFolder,
            filePath
        ]);

    var homeResults = await plugin.HomeQueryAsync(CancellationToken.None);
    Console.WriteLine($"HOME_RESULT_COUNT={homeResults.Count}");

    if (homeResults.Count != 3)
    {
        throw new InvalidOperationException("空白主页没有显示去重后的固定文件和文件夹。");
    }

    if (!string.Equals(homeResults[0].CopyText, firstFolder, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(homeResults[1].CopyText, secondFolder, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(homeResults[2].CopyText, filePath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("空白主页没有保持固定项目顺序。");
    }

    if (homeResults.Any(result =>
            !result.SubTitle.StartsWith("已固定到主页", StringComparison.Ordinal) ||
            !result.RecordKey.StartsWith("history-box|home|", StringComparison.Ordinal) ||
            result.AddSelectedCount))
    {
        throw new InvalidOperationException("主页结果标识、记录键或排序保护不正确。");
    }

    var searchResults = await plugin.QueryAsync(
        CreateQuery("Pinned-A"),
        CancellationToken.None);
    var matchingSearchResult = searchResults.Single(result => string.Equals(
        result.CopyText,
        firstFolder,
        StringComparison.OrdinalIgnoreCase));

    if (!matchingSearchResult.RecordKey.StartsWith("history-box|search|", StringComparison.Ordinal) ||
        string.Equals(matchingSearchResult.RecordKey, homeResults[0].RecordKey, StringComparison.Ordinal) ||
        !matchingSearchResult.SubTitle.StartsWith("History Box 搜索 · 已固定到主页", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("搜索结果没有与主页固定状态使用独立记录键和文案。");
    }

    var contextMenus = plugin.LoadContextMenus(matchingSearchResult);
    var removeFromHome = contextMenus.Single(menu => menu.Title == "从主页移除");

    if (removeFromHome.IcoPath != "Images\\pin-off.svg" || removeFromHome.Action!(null!))
    {
        throw new InvalidOperationException("从主页移除操作的图标或窗口保持行为不正确。");
    }

    var fileSearchResults = await plugin.QueryAsync(
        CreateQuery("not-a-home-folder.txt"),
        CancellationToken.None);
    var pinnedFileResult = fileSearchResults.Single(result => string.Equals(
        result.CopyText,
        filePath,
        StringComparison.OrdinalIgnoreCase));
    var fileMenuTitles = plugin.LoadContextMenus(pinnedFileResult)
        .Select(menu => menu.Title)
        .ToHashSet(StringComparer.Ordinal);

    if (!fileMenuTitles.Contains("从主页移除") ||
        !fileMenuTitles.Contains("打开所在位置"))
    {
        throw new InvalidOperationException("固定文件缺少主页移除或打开所在位置操作。");
    }

    Console.WriteLine("FILE_HOME_PIN=PASS");

    var afterRemoval = await plugin.HomeQueryAsync(CancellationToken.None);
    if (afterRemoval.Any(result => string.Equals(
            result.CopyText,
            firstFolder,
            StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("从主页移除后文件夹仍出现在空白主页。");
    }

    InvokePrivate(plugin, "ToggleHomePinned", firstFolder + Path.DirectorySeparatorChar);
    var afterRestore = await plugin.HomeQueryAsync(CancellationToken.None);

    if (!string.Equals(afterRestore[0].CopyText, firstFolder, StringComparison.OrdinalIgnoreCase) ||
        afterRestore.Count(result => string.Equals(
            result.CopyText,
            firstFolder,
            StringComparison.OrdinalIgnoreCase)) != 1)
    {
        throw new InvalidOperationException("重新固定时路径规范化或去重失败。");
    }

    Console.WriteLine("HOME_PIN_ISOLATION=PASS");
    Console.WriteLine("SEARCH_RECORD_KEY_ISOLATION=PASS");
    Console.WriteLine("PATH_NORMALIZATION=PASS");

    CreateShortcut(recentShortcut, liveRecentFile);
    File.SetLastWriteTime(recentShortcut, DateTime.Now);
    List<Result> refreshedHomeResults = [];
    Result? refreshedRecentResult = null;

    for (var attempt = 0; attempt < 20 && refreshedRecentResult is null; attempt++)
    {
        await InvokePrivateAsync(plugin, "RefreshHomeRecentItemsAsync", false);
        refreshedHomeResults = (List<Result>?)InvokePrivate(plugin, "BuildHomeResults") ?? [];
        refreshedRecentResult = refreshedHomeResults.SingleOrDefault(result => string.Equals(
            result.CopyText,
            liveRecentFile,
            StringComparison.OrdinalIgnoreCase));

        if (refreshedRecentResult is null)
        {
            await Task.Delay(100);
        }
    }

    if (refreshedRecentResult is null ||
        !refreshedRecentResult.SubTitle.StartsWith("最近使用：", StringComparison.Ordinal) ||
        refreshedHomeResults.Take(3).Any(result =>
            !result.SubTitle.StartsWith("已固定到主页", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("主页没有保持固定项目优先并实时补充最近项目。");
    }

    Console.WriteLine("HOME_LIVE_RECENT_REFRESH=PASS");

    var manifestPath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "Flow.Launcher.Plugin.RecentItems",
        "plugin.json");
    using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
    var keywords = manifest.RootElement
        .GetProperty("ActionKeywords")
        .EnumerateArray()
        .Select(element => element.GetString())
        .ToHashSet(StringComparer.Ordinal);

    if (!keywords.SetEquals(["*", "rf"]))
    {
        throw new InvalidOperationException("插件没有同时保留全局搜索和 rf 显式入口。");
    }

    if (manifest.RootElement.GetProperty("Author").GetString() != "lei" ||
        manifest.RootElement.GetProperty("Version").GetString() != "1.2.5")
    {
        throw new InvalidOperationException("插件作者或版本元数据不正确。");
    }

    Console.WriteLine("GLOBAL_AND_RF_KEYWORDS=PASS");
    Console.WriteLine("AUTHOR_METADATA=PASS");
}
finally
{
    if (File.Exists(recentShortcut))
    {
        File.Delete(recentShortcut);
    }

    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, recursive: true);
    }
}

static void SetPinnedPaths(Main plugin, List<string> paths)
{
    var settingsField = typeof(Main).GetField(
        "_settings",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("找不到插件设置字段。");

    settingsField.SetValue(plugin, new Main.PluginSettings { PinnedPaths = paths });
    InvokePrivate(plugin, "NormalizePinnedSettings");
    InvokePrivate(plugin, "RefreshHomePinnedItems");
}

static object? InvokePrivate(Main plugin, string methodName, params object[] arguments)
{
    var method = typeof(Main).GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"找不到私有方法：{methodName}");

    return method.Invoke(plugin, arguments);
}

static async Task InvokePrivateAsync(
    Main plugin,
    string methodName,
    params object[] arguments)
{
    if (InvokePrivate(plugin, methodName, arguments) is not Task task)
    {
        throw new InvalidOperationException($"私有方法没有返回 Task：{methodName}");
    }

    await task;
}

static void CreateShortcut(string shortcutPath, string targetPath)
{
    object? shell = null;
    object? shortcut = null;

    try
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("找不到 WScript.Shell。");
        shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 WScript.Shell。");
        shortcut = shellType.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            binder: null,
            target: shell,
            args: [shortcutPath])
            ?? throw new InvalidOperationException("无法创建测试快捷方式。");
        var shortcutType = shortcut.GetType();
        shortcutType.InvokeMember(
            "TargetPath",
            BindingFlags.SetProperty,
            binder: null,
            target: shortcut,
            args: [targetPath]);
        shortcutType.InvokeMember(
            "Save",
            BindingFlags.InvokeMethod,
            binder: null,
            target: shortcut,
            args: null);
    }
    finally
    {
        if (shortcut is not null && Marshal.IsComObject(shortcut))
        {
            Marshal.FinalReleaseComObject(shortcut);
        }

        if (shell is not null && Marshal.IsComObject(shell))
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

static Query CreateQuery(string search)
{
    var query = new Query();
    var searchProperty = typeof(Query).GetProperty(
        "Search",
        BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("找不到 Query.Search 属性。");

    searchProperty.SetValue(query, search);
    return query;
}
