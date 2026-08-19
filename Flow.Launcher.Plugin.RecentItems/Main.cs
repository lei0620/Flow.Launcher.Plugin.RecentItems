using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.RecentItems;

public sealed class Main : IAsyncPlugin, IAsyncHomeQuery, IContextMenu
{
    private const int HomeResultLimit = 15;
    private const int CandidateLimit = 120;
    private const int HomeScoreBase = 1_000_000;
    private const int SearchScoreBase = 100_000;
    private const string RecordKeyPrefix = "history-box";
    private const string PluginIcon = "Images\\history.svg";
    private const string OpenLocationIcon = "Images\\folder-open.svg";
    private const string PinIcon = "Images\\pin.svg";
    private const string UnpinIcon = "Images\\pin-off.svg";

    private readonly object _settingsLock = new();
    private readonly object _homeRecentLock = new();
    private readonly SemaphoreSlim _homeRefreshGate = new(1, 1);
    private PluginInitContext? _context;
    private PluginSettings _settings = new();
    private List<RecentItem> _homePinnedItems = [];
    private List<RecentItem> _homeRecentItems = [];

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        _settings = context.API.LoadSettingJsonStorage<PluginSettings>();
        _settings.PinnedPaths ??= [];
        NormalizePinnedSettings();
        RefreshHomePinnedItems();
        _ = RefreshHomeRecentItemsAsync(requestRequery: false);
        return Task.CompletedTask;
    }

    public Task<List<Result>> HomeQueryAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var results = BuildHomeResults();
        _ = RefreshHomeRecentItemsAsync(requestRequery: true);
        return Task.FromResult(results);
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        return Task.Run(
            () => BuildSearchResults(query.Search?.Trim() ?? string.Empty, HomeResultLimit, token),
            token);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not RecentItem item)
        {
            return [];
        }

        List<Result> contextMenus = [];

        contextMenus.Add(new Result
        {
            Title = item.IsHomePinned ? "从主页移除" : "固定到主页",
            IcoPath = item.IsHomePinned ? UnpinIcon : PinIcon,
            Score = 100,
            AddSelectedCount = false,
            Action = _ => ToggleHomePinned(item.TargetPath)
        });

        if (!item.IsDirectory)
        {
            contextMenus.Add(new Result
            {
                Title = "打开所在位置",
                SubTitle = "在文件资源管理器中选中此文件",
                IcoPath = OpenLocationIcon,
                Score = 90,
                AddSelectedCount = false,
                Action = _ => OpenContainingLocation(item.TargetPath)
            });
        }

        return contextMenus;
    }

    private List<Result> BuildHomeResults()
    {
        List<RecentItem> pinnedItems;
        lock (_settingsLock)
        {
            pinnedItems = [.. _homePinnedItems];
        }

        List<RecentItem> recentItems;
        lock (_homeRecentLock)
        {
            recentItems = [.. _homeRecentItems];
        }

        var pinnedPaths = pinnedItems
            .Select(item => item.TargetPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return pinnedItems
            .Concat(recentItems.Where(item => !pinnedPaths.Contains(item.TargetPath)))
            .Take(HomeResultLimit)
            .Select((item, index) => CreateResult(item, index, ResultSurface.Home))
            .ToList();
    }

    private List<Result> BuildSearchResults(string searchText, int limit, CancellationToken token)
    {
        try
        {
            var items = ReadSearchItems(token);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                items = items
                    .Where(item =>
                        item.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        item.TargetPath.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var results = items
                .Take(limit)
                .Select((item, index) => CreateResult(item, index, ResultSurface.Search))
                .ToList();

            if (results.Count == 0)
            {
                results.Add(new Result
                {
                    Title = string.IsNullOrWhiteSpace(searchText)
                        ? "暂无最近使用的文件或文件夹"
                        : $"没有找到“{searchText}”",
                    SubTitle = "记录来源：Windows 最近使用项目",
                    IcoPath = PluginIcon,
                    Score = 1,
                    AddSelectedCount = false
                });
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _context?.API.LogException(nameof(RecentItems), "读取最近使用项目失败", exception);
            return
            [
                new Result
                {
                    Title = "读取最近使用项目失败",
                    SubTitle = exception.Message,
                    IcoPath = PluginIcon,
                    Score = 1,
                    AddSelectedCount = false
                }
            ];
        }
    }

    private List<RecentItem> ReadSearchItems(CancellationToken token)
    {
        var items = ReadWindowsRecentItems(token);

        List<RecentItem> pinnedItems;
        lock (_settingsLock)
        {
            pinnedItems = [.. _homePinnedItems];
        }

        var recentByPath = items.ToDictionary(
            item => item.TargetPath,
            StringComparer.OrdinalIgnoreCase);
        var pinnedOnlyItems = new List<RecentItem>();

        foreach (var pinnedItem in pinnedItems)
        {
            token.ThrowIfCancellationRequested();

            if (recentByPath.TryGetValue(pinnedItem.TargetPath, out var recentItem))
            {
                var index = items.FindIndex(item => string.Equals(
                    item.TargetPath,
                    pinnedItem.TargetPath,
                    StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    items[index] = recentItem with { IsHomePinned = true };
                }
                continue;
            }

            pinnedOnlyItems.Add(pinnedItem);
        }

        return [.. items, .. pinnedOnlyItems];
    }

    private static List<RecentItem> ReadWindowsRecentItems(CancellationToken token)
    {
        var recentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        var items = new List<RecentItem>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(recentDirectory) && Directory.Exists(recentDirectory))
        {
            var links = new DirectoryInfo(recentDirectory)
                .EnumerateFiles("*.lnk", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(CandidateLimit);

            foreach (var link in links)
            {
                token.ThrowIfCancellationRequested();

                var targetPath = ResolveShortcutTarget(link.FullName);
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    continue;
                }

                targetPath = NormalizePath(targetPath);
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    continue;
                }

                var isDirectory = Directory.Exists(targetPath);
                var isFile = !isDirectory && File.Exists(targetPath);
                if ((!isDirectory && !isFile) || !seenTargets.Add(targetPath))
                {
                    continue;
                }

                items.Add(CreateRecentItem(
                    targetPath,
                    link.LastWriteTime,
                    isDirectory,
                    isHomePinned: false));
            }
        }

        return items;
    }

    private async Task RefreshHomeRecentItemsAsync(bool requestRequery)
    {
        if (!await _homeRefreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var latestItems = await Task.Run(
                    () => ReadWindowsRecentItems(CancellationToken.None))
                .ConfigureAwait(false);
            bool changed;

            lock (_homeRecentLock)
            {
                changed = !RecentItemsEqual(_homeRecentItems, latestItems);
                if (changed)
                {
                    _homeRecentItems = latestItems;
                }
            }

            if (changed && requestRequery)
            {
                _context?.API.ReQuery(reselect: false);
            }
        }
        catch (Exception exception)
        {
            _context?.API.LogException(
                nameof(RecentItems),
                "刷新主页最近项目失败",
                exception);
        }
        finally
        {
            _homeRefreshGate.Release();
        }
    }

    private static bool RecentItemsEqual(
        IReadOnlyList<RecentItem> current,
        IReadOnlyList<RecentItem> latest)
    {
        if (current.Count != latest.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!string.Equals(
                    current[index].TargetPath,
                    latest[index].TargetPath,
                    StringComparison.OrdinalIgnoreCase) ||
                current[index].LastUsed != latest[index].LastUsed)
            {
                return false;
            }
        }

        return true;
    }

    private static RecentItem CreateRecentItem(
        string targetPath,
        DateTime lastUsed,
        bool isDirectory,
        bool isHomePinned)
    {
        var title = isDirectory
            ? new DirectoryInfo(targetPath).Name
            : Path.GetFileName(targetPath);

        if (string.IsNullOrWhiteSpace(title))
        {
            title = targetPath;
        }

        return new RecentItem(title, targetPath, lastUsed, isDirectory, isHomePinned);
    }

    private void NormalizePinnedSettings()
    {
        lock (_settingsLock)
        {
            var normalizedPaths = (_settings.PinnedPaths ?? [])
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _settings.PinnedPaths = normalizedPaths;
        }
    }

    private void RefreshHomePinnedItems()
    {
        lock (_settingsLock)
        {
            _homePinnedItems = (_settings.PinnedPaths ?? [])
                .Where(path => Directory.Exists(path) || File.Exists(path))
                .Select(path => CreateRecentItem(
                    path,
                    DateTime.MinValue,
                    isDirectory: Directory.Exists(path),
                    isHomePinned: true))
                .ToList();
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(path.Trim());
            var fullPath = Path.GetFullPath(expandedPath);
            var rootPath = Path.GetPathRoot(fullPath);

            return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string? ResolveShortcutTarget(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            if (shortcut is null)
            {
                return null;
            }

            return shortcut.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null) as string;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static Result CreateResult(RecentItem item, int index, ResultSurface surface)
    {
        var kind = item.IsDirectory ? "文件夹" : "文件";
        var status = surface == ResultSurface.Home
            ? item.IsHomePinned
                ? $"已固定到主页 · {kind} · {item.TargetPath}"
                : $"最近使用：{item.LastUsed:MM-dd HH:mm} · {kind} · {item.TargetPath}"
            : item.IsHomePinned
                ? $"History Box 搜索 · 已固定到主页 · {kind} · {item.TargetPath}"
                : $"History Box 搜索 · 最近使用：{item.LastUsed:MM-dd HH:mm} · {kind} · {item.TargetPath}";
        var surfaceKey = surface == ResultSurface.Home ? "home" : "search";

        return new Result
        {
            Title = item.Title,
            SubTitle = status,
            IcoPath = item.TargetPath,
            CopyText = item.TargetPath,
            Score = surface == ResultSurface.Home
                ? HomeScoreBase - index
                : SearchScoreBase - index,
            AddSelectedCount = false,
            RecordKey = $"{RecordKeyPrefix}|{surfaceKey}|{item.TargetPath}",
            ContextData = item,
            Preview = new Result.PreviewInfo
            {
                FilePath = item.TargetPath
            },
            Action = _ => OpenTarget(item.TargetPath)
        };
    }

    private static bool OpenTarget(string targetPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ToggleHomePinned(string targetPath)
    {
        targetPath = NormalizePath(targetPath);

        lock (_settingsLock)
        {
            var pinnedPaths = _settings.PinnedPaths ??= [];
            var existingIndex = pinnedPaths.FindIndex(
                path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                pinnedPaths.RemoveAt(existingIndex);
            }
            else if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                pinnedPaths.Insert(0, targetPath);
            }

            RefreshHomePinnedItems();
            _context?.API.SaveSettingJsonStorage<PluginSettings>();
        }

        _context?.API.ReQuery(reselect: false);
        return false;
    }

    private bool OpenContainingLocation(string targetPath)
    {
        try
        {
            if (_context is not null)
            {
                var directoryPath = Path.GetDirectoryName(targetPath);

                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    return false;
                }

                _context.API.OpenDirectory(
                    directoryPath,
                    targetPath);
                return true;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add($"/select,{targetPath}");

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public sealed class PluginSettings
    {
        public List<string>? PinnedPaths { get; set; } = [];
    }

    private sealed record RecentItem(
        string Title,
        string TargetPath,
        DateTime LastUsed,
        bool IsDirectory,
        bool IsHomePinned);

    private enum ResultSurface
    {
        Home,
        Search
    }
}
