using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.RecentItems;

public sealed class Main : IAsyncPlugin, IAsyncHomeQuery, IContextMenu
{
    private const int HomeResultLimit = 15;
    private const int CandidateLimit = 120;
    private const string PluginIcon = "Images\\history.svg";
    private const string OpenLocationIcon = "Images\\folder-open.svg";
    private const string PinIcon = "Images\\pin.svg";
    private const string UnpinIcon = "Images\\pin-off.svg";

    private readonly object _settingsLock = new();
    private PluginInitContext? _context;
    private PluginSettings _settings = new();

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        _settings = context.API.LoadSettingJsonStorage<PluginSettings>();
        _settings.PinnedPaths ??= [];
        return Task.CompletedTask;
    }

    public Task<List<Result>> HomeQueryAsync(CancellationToken token)
    {
        return Task.Run(
            () => BuildResults(searchText: string.Empty, HomeResultLimit, token),
            token);
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        return Task.Run(
            () => BuildResults(query.Search?.Trim() ?? string.Empty, HomeResultLimit, token),
            token);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not RecentItem item)
        {
            return [];
        }

        List<Result> contextMenus =
        [
            new Result
            {
                Title = item.IsPinned ? "取消置顶" : "置顶",
                IcoPath = item.IsPinned ? UnpinIcon : PinIcon,
                Score = 100,
                AddSelectedCount = false,
                Action = _ => TogglePinned(item.TargetPath)
            }
        ];

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

    private List<Result> BuildResults(string searchText, int limit, CancellationToken token)
    {
        try
        {
            var items = ReadRecentItems(token);

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
                .Select((item, index) => CreateResult(item, index))
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

    private List<RecentItem> ReadRecentItems(CancellationToken token)
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

                targetPath = Environment.ExpandEnvironmentVariables(targetPath.Trim());

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
                    isPinned: false));
            }
        }

        List<string> pinnedPaths;
        lock (_settingsLock)
        {
            var savedPinnedPaths = _settings.PinnedPaths ??= [];
            pinnedPaths = [.. savedPinnedPaths];
        }

        var recentByPath = items.ToDictionary(
            item => item.TargetPath,
            StringComparer.OrdinalIgnoreCase);
        var pinnedItems = new List<RecentItem>();

        foreach (var pinnedPath in pinnedPaths)
        {
            token.ThrowIfCancellationRequested();

            if (recentByPath.Remove(pinnedPath, out var recentItem))
            {
                pinnedItems.Add(recentItem with { IsPinned = true });
                continue;
            }

            var isDirectory = Directory.Exists(pinnedPath);
            if (isDirectory || File.Exists(pinnedPath))
            {
                pinnedItems.Add(CreateRecentItem(
                    pinnedPath,
                    DateTime.MinValue,
                    isDirectory,
                    isPinned: true));
            }
        }

        return [.. pinnedItems, .. items.Where(item => recentByPath.ContainsKey(item.TargetPath))];
    }

    private static RecentItem CreateRecentItem(
        string targetPath,
        DateTime lastUsed,
        bool isDirectory,
        bool isPinned)
    {
        var title = isDirectory
            ? new DirectoryInfo(targetPath).Name
            : Path.GetFileName(targetPath);

        if (string.IsNullOrWhiteSpace(title))
        {
            title = targetPath;
        }

        return new RecentItem(title, targetPath, lastUsed, isDirectory, isPinned);
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

    private static Result CreateResult(RecentItem item, int index)
    {
        var kind = item.IsDirectory ? "文件夹" : "文件";
        var status = item.IsPinned
            ? $"已置顶 · {kind} · {item.TargetPath}"
            : $"最近使用：{item.LastUsed:MM-dd HH:mm} · {kind} · {item.TargetPath}";

        return new Result
        {
            Title = item.Title,
            SubTitle = status,
            IcoPath = item.TargetPath,
            CopyText = item.TargetPath,
            Score = 10_000 - index,
            AddSelectedCount = false,
            RecordKey = item.TargetPath,
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

    private bool TogglePinned(string targetPath)
    {
        lock (_settingsLock)
        {
            var pinnedPaths = _settings.PinnedPaths ??= [];
            var existingIndex = pinnedPaths.FindIndex(
                path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                pinnedPaths.RemoveAt(existingIndex);
            }
            else
            {
                pinnedPaths.Insert(0, targetPath);
            }

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
        bool IsPinned);
}
