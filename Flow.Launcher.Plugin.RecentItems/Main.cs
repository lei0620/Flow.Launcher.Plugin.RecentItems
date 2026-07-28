using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.RecentItems;

public sealed class Main : IAsyncPlugin, IAsyncHomeQuery
{
    private const int HomeResultLimit = 15;
    private const int CandidateLimit = 120;
    private const string PluginIcon = "Images\\history.svg";

    private PluginInitContext? _context;

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
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

    private static List<RecentItem> ReadRecentItems(CancellationToken token)
    {
        var recentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrWhiteSpace(recentDirectory) || !Directory.Exists(recentDirectory))
        {
            return [];
        }

        var items = new List<RecentItem>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            var title = isDirectory
                ? new DirectoryInfo(targetPath).Name
                : Path.GetFileName(targetPath);

            if (string.IsNullOrWhiteSpace(title))
            {
                title = targetPath;
            }

            items.Add(new RecentItem(
                title,
                targetPath,
                link.LastWriteTime,
                isDirectory));
        }

        return items;
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

        return new Result
        {
            Title = item.Title,
            SubTitle = $"最近使用：{item.LastUsed:MM-dd HH:mm} · {kind} · {item.TargetPath}",
            IcoPath = item.TargetPath,
            CopyText = item.TargetPath,
            Score = 10_000 - index,
            AddSelectedCount = false,
            RecordKey = item.TargetPath,
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

    private sealed record RecentItem(
        string Title,
        string TargetPath,
        DateTime LastUsed,
        bool IsDirectory);
}
