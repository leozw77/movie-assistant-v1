using Microsoft.Web.WebView2.Core;

namespace QbPotDoubanAi;

internal static class WatchlistWebView2Script
{
    private static readonly Lazy<string> Script = new(LoadScript);

    internal static async Task InstallAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Script.Value).ConfigureAwait(true);
    }

    private static string LoadScript()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        var scriptPath = Path.Combine(directory, "douban-watchlist.js");
        var cssPath = Path.Combine(directory, "douban-watchlist.css");
        var script = DoubanPlusAssetStore.ReadText(scriptPath, "本地待看脚本资源缺失。");
        var css = DoubanPlusAssetStore.ReadText(cssPath, "本地待看样式资源缺失。");
        const string placeholder = "__QB_DOUBAN_WATCHLIST_CSS__";
        if (!script.Contains(placeholder, StringComparison.Ordinal))
            throw new InvalidDataException("本地待看脚本缺少 CSS 占位符。");
        return script.Replace(placeholder, System.Text.Json.JsonSerializer.Serialize(css), StringComparison.Ordinal);
    }
}
