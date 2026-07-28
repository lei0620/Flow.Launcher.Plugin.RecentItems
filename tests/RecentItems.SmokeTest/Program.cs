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
