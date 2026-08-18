using Microsoft.Web.WebView2.Core;

namespace QbPotDoubanAi;

internal sealed class WebView2EnvironmentProvider
{
    private readonly string _dataDirectory;
    private readonly object _gate = new();
    private Task<CoreWebView2Environment>? _localEnvironment;
    private Task<CoreWebView2Environment>? _doubanEnvironment;

    internal WebView2EnvironmentProvider(string dataDirectory) => _dataDirectory = dataDirectory;

    internal string LocalProfileDirectory => Path.Combine(_dataDirectory, "WebView2", "LocalUiProfile");
    internal string DoubanProfileDirectory => Path.Combine(_dataDirectory, "WebView2", "DoubanProfile");

    internal static (bool Available, string Version, string Error) ProbeRuntime()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return (!string.IsNullOrWhiteSpace(version), version ?? "", "");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    internal Task<CoreWebView2Environment> GetLocalEnvironmentAsync()
    {
        lock (_gate)
            return _localEnvironment ??= CreateAsync(LocalProfileDirectory);
    }

    internal Task<CoreWebView2Environment> GetDoubanEnvironmentAsync()
    {
        lock (_gate)
            return _doubanEnvironment ??= CreateAsync(DoubanProfileDirectory);
    }

    private static Task<CoreWebView2Environment> CreateAsync(string userDataFolder)
    {
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());
    }
}
