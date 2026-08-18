using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QbPotDoubanAi;

public sealed record BrowserSession(string BrowserName, int Port, string ProfileDirectory, int ProcessId);
public sealed record DoubanSessionStatus(string State, string Text, string ProfileId, DateTime? VerifiedAt, string Error)
{
    public bool IsLoggedIn => State.Equals("logged-in", StringComparison.OrdinalIgnoreCase);
}
public sealed record DoubanSearchPageResult(bool LoggedIn, bool Captcha, List<DoubanSearchCandidate> Items, bool HasMore, string Error);
public sealed record DoubanSubjectMetadataResult(string Title, double? Score, string Poster, bool Captcha, bool LoggedIn, int HttpStatus, string Error)
{
    public bool IsSuccess => LoggedIn && !Captcha && string.IsNullOrWhiteSpace(Error) && !string.IsNullOrWhiteSpace(Title);
    public string Year { get; init; } = "";
    public string Genres { get; init; } = "";
    public string Directors { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string Countries { get; init; } = "";
    public string ImdbId { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<DoubanStatusOption> StatusOptions { get; init; } = [];
    public bool StatusCapabilitiesKnown { get; init; }
    public string StatusCapabilitySource { get; init; } = "detail-metadata";
    public string StatusCapabilityError { get; init; } = "";
    public string ConnectorSource { get; init; } = "cdp";
}
public sealed record BrowserMediaSnapshot(string TargetId, string Url, string Title, int? Year, string Genre, double Duration, double CurrentTime, bool Paused)
{
    public bool HasVideo => Duration > 0 && CurrentTime >= 0;
    public double Ratio => HasVideo ? Math.Clamp(CurrentTime / Duration, 0, 1) : 0;
    public string Key => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Url.Split('?')[0] + "|" + Title)))[..20];
}

public static class WatchTimeRules
{
    public static bool IsQuickCompletionReady(double watchedSeconds, double ratio) => watchedSeconds >= 30 && ratio >= 0.70;
    public static double CountableSeconds(BrowserMediaSnapshot? previous, BrowserMediaSnapshot current, double elapsed)
    {
        if (previous is null || previous.TargetId != current.TargetId || previous.Paused || current.Paused) return 0;
        var advance = current.CurrentTime - previous.CurrentTime;
        var maximumNaturalAdvance = Math.Max(3, elapsed * 2.5 + 1);
        return advance > 0.05 && advance <= maximumNaturalAdvance ? Math.Clamp(elapsed, 0, 5) : 0;
    }
}

public static class DoubanPageRules
{
    public static bool IsRiskControl(string title, string href, string body) =>
        href.Contains("/misc/sorry", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("禁止访问", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("像机器人程序", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("点击证明", StringComparison.OrdinalIgnoreCase);

    public static bool IsExpectedPage(string actualUrl, string expectedUrl)
    {
        if (!Uri.TryCreate(actualUrl, UriKind.Absolute, out var actual) || !Uri.TryCreate(expectedUrl, UriKind.Absolute, out var expected)) return false;
        return actual.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase) &&
               actual.AbsolutePath.TrimEnd('/').Equals(expected.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class BrowserCdpService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string _dataDirectory;
    private readonly SemaphoreSlim _doubanNavigationGate = new(1, 1);
    private static int _nextCdpCommandId = 700;
    private string _doubanWorkerTargetId = "";
    private string _doubanWorkerWebSocket = "";
    private const string DoubanWorkerMarkerUrl = "about:blank#douban-browser-reminder-worker";
    public BrowserSession? Session { get; private set; }
    public BrowserCdpService(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        try
        {
            var path = Path.Combine(_dataDirectory, "browser-session.json");
            if (File.Exists(path)) Session = JsonSerializer.Deserialize<BrowserSession>(File.ReadAllText(path));
        }
        catch { Session = null; }
    }
    public static string? FindInstalledBrowserName(string preferred = "Chrome") => FindBrowser(preferred)?.Name;
    public static int ProbeFreeLoopbackPort() => GetFreeLoopbackPort();
    internal static bool IsValidImdbId(string? value) => Regex.IsMatch(value?.Trim() ?? "", @"^tt\d{5,10}$", RegexOptions.IgnoreCase);
    internal static string BuildPtDepilerSearchUrl(string extensionId, string imdbId)
    {
        if (!Regex.IsMatch(extensionId ?? "", @"^[a-p]{32}$", RegexOptions.IgnoreCase)) throw new InvalidOperationException("PT-Depiler 扩展 ID 无效。");
        var normalizedImdbId = imdbId?.Trim() ?? "";
        if (!IsValidImdbId(normalizedImdbId)) throw new InvalidOperationException("IMDb 编号无效。");
        return $"chrome-extension://{extensionId}/src/entries/options/index.html#/search-entity?search={Uri.EscapeDataString("imdb|" + normalizedImdbId)}&plan=default&flush=1";
    }

    internal async Task AttachExistingAsync(int port)
    {
        if (!await IsAvailableAsync(port)) throw new InvalidOperationException($"127.0.0.1:{port} 不是可用的 Chrome CDP 端口。");
        Session = new BrowserSession("Google Chrome", port, Path.Combine(_dataDirectory, "ChromeProfile"), 0);
        SaveSession();
    }

    public async Task<BrowserSession> LaunchAsync(string preferredBrowser = "Chrome", bool background = false)
    {
        if (Session is not null && await IsAvailableAsync(Session.Port))
        {
            if (!background) await RestoreBrowserWindowAsync();
            return Session;
        }
        var candidate = FindBrowser(preferredBrowser);
        if (candidate is null) throw new FileNotFoundException("未找到 Microsoft Edge 或 Google Chrome。请先安装其中一个浏览器。");
        var profileDirectory = Path.Combine(_dataDirectory, candidate.Value.Id + "Profile");
        Directory.CreateDirectory(profileDirectory);
        var port = GetFreeLoopbackPort();
        var backgroundArgs = background ? " --start-minimized --window-position=80,80 --window-size=1280,850" : "";
        var args = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port} --user-data-dir=\"{profileDirectory}\" --no-first-run --no-default-browser-check{backgroundArgs} https://www.iqiyi.com/";
        var process = Process.Start(new ProcessStartInfo(candidate.Value.Path, args) { UseShellExecute = false, WindowStyle = background ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal }) ?? throw new InvalidOperationException("浏览器启动失败。");
        for (var i = 0; i < 30; i++) { await Task.Delay(200); if (await IsAvailableAsync(port)) break; }
        if (!await IsAvailableAsync(port)) throw new InvalidOperationException("浏览器已启动，但本机 CDP 端口没有就绪。");
        Session = new BrowserSession(candidate.Value.Name, port, profileDirectory, process.Id);
        SaveSession();
        if (!background) await RestoreBrowserWindowAsync();
        return Session;
    }

    public async Task<BrowserSession> EnsureBackgroundAsync(string preferredBrowser = "Chrome")
    {
        var session = await LaunchAsync(preferredBrowser, true);
        await CleanupStaleDoubanWorkerTargetsAsync();
        return session;
    }

    public async Task RestoreBrowserWindowAsync()
    {
        if (Session is null || !await IsAvailableAsync(Session.Port)) throw new InvalidOperationException("观影浏览器尚未连接。");
        try
        {
            using var pages = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/list"));
            var targetId = pages.RootElement.EnumerateArray()
                .Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "page")
                .Select(x => x.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "")
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            using var version = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/version"));
            if (!string.IsNullOrWhiteSpace(targetId) && version.RootElement.TryGetProperty("webSocketDebuggerUrl", out var browserSocket))
            {
                var window = await SendCdpCommandAsync(browserSocket.GetString()!, "Browser.getWindowForTarget", new { targetId }, 10);
                if (window is not null && window.Value.TryGetProperty("windowId", out var windowIdValue))
                {
                    var windowId = windowIdValue.GetInt32();
                    await SendCdpCommandAsync(browserSocket.GetString()!, "Browser.setWindowBounds", new { windowId, bounds = new { windowState = "normal" } }, 10);
                    await SendCdpCommandAsync(browserSocket.GetString()!, "Browser.setWindowBounds", new { windowId, bounds = new { left = 80, top = 80, width = 1280, height = 850 } }, 10);
                }
            }
        }
        catch (Exception ex) { DiagnosticLogger.Write("Browser window CDP restore failed: " + ex.Message); }

        RestoreNativeBrowserWindow(Session.ProcessId);
        DiagnosticLogger.Write($"Browser window restore requested; ProcessId={Session.ProcessId}; Port={Session.Port}");
    }

    private static void RestoreNativeBrowserWindow(int processId)
    {
        if (processId <= 0) return;
        NativeMethods.EnumWindows((window, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(window, out var ownerProcessId);
            if (ownerProcessId != (uint)processId) return true;
            var className = new StringBuilder(128);
            NativeMethods.GetClassName(window, className, className.Capacity);
            if (!className.ToString().Equals("Chrome_WidgetWin_1", StringComparison.Ordinal)) return true;
            NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
            NativeMethods.SetWindowPos(window, IntPtr.Zero, 80, 80, 1280, 850, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetForegroundWindow(window);
            return false;
        }, IntPtr.Zero);
    }

    private void SaveSession()
    {
        try { if (Session is not null) File.WriteAllText(Path.Combine(_dataDirectory, "browser-session.json"), JsonSerializer.Serialize(Session)); } catch { }
    }

    public async Task<bool> IsConnectedAsync() => Session is not null && await IsAvailableAsync(Session.Port);

    public async Task OpenDoubanLoginAsync()
    {
        if (Session is null || !await IsAvailableAsync(Session.Port)) throw new InvalidOperationException("请先启动观影浏览器。");
        var request = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{Session.Port}/json/new?{Uri.EscapeDataString("https://movie.douban.com/mine?status=collect")}");
        using var response = await _http.SendAsync(request); response.EnsureSuccessStatusCode();
    }

    // Open PT-Depiler's own IMDb search route directly. Keyboard simulation is
    // intentionally avoided because Chrome's omnibox and the active IME can
    // transform "ptd" / IMDb text before the extension receives it.
    public async Task OpenPtDepilerSearchAsync(string imdbId)
    {
        var normalizedImdbId = imdbId?.Trim() ?? "";
        if (!IsValidImdbId(normalizedImdbId))
            throw new InvalidOperationException("没有有效的 IMDb 编号，无法发起 PT-Depiler 搜索。");
        if (Session is null || !await IsAvailableAsync(Session.Port)) throw new InvalidOperationException("观影浏览器尚未连接。");
        var extensionId = await FindPtDepilerExtensionIdAsync();
        if (string.IsNullOrWhiteSpace(extensionId)) throw new InvalidOperationException("观影浏览器中没有找到 PT-Depiler 扩展，请先安装或启用扩展。");
        var searchUrl = BuildPtDepilerSearchUrl(extensionId, normalizedImdbId);
        using var version = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/version"));
        if (!version.RootElement.TryGetProperty("webSocketDebuggerUrl", out var browserSocket)) throw new InvalidOperationException("独立 Chrome 没有提供新标签页接口。");
        var created = await SendCdpCommandAsync(browserSocket.GetString()!, "Target.createTarget", new { url = searchUrl, background = false }, 10);
        var targetId = created is not null && created.Value.TryGetProperty("targetId", out var id) ? id.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(targetId)) throw new InvalidOperationException("无法打开 PT-Depiler 搜索标签页。");
        await SendCdpCommandAsync(browserSocket.GetString()!, "Target.activateTarget", new { targetId }, 10);
        await RestoreBrowserWindowAsync();
        DiagnosticLogger.Write($"PT-Depiler IMDb search opened; IMDb={normalizedImdbId}; ExtensionId={extensionId}; TargetId={targetId}");
    }

    private async Task<string> FindPtDepilerExtensionIdAsync()
    {
        if (Session is null) return "";
        try
        {
            using var pages = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/list"));
            foreach (var target in pages.RootElement.EnumerateArray())
            {
                var title = target.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? "" : "";
                var url = target.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? "" : "";
                if (!title.Contains("PT Depiler", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase)) continue;
                if (Regex.IsMatch(uri.Host, @"^[a-p]{32}$", RegexOptions.IgnoreCase)) return uri.Host;
            }
        }
        catch (Exception ex) { DiagnosticLogger.Write("PT-Depiler target discovery failed: " + ex.Message); }

        foreach (var preferenceName in new[] { "Secure Preferences", "Preferences" })
        {
            try
            {
                var path = Path.Combine(Session.ProfileDirectory, "Default", preferenceName);
                if (!File.Exists(path)) continue;
                using var preferences = JsonDocument.Parse(File.ReadAllText(path));
                if (!preferences.RootElement.TryGetProperty("extensions", out var extensions) || !extensions.TryGetProperty("settings", out var settings)) continue;
                foreach (var extension in settings.EnumerateObject())
                {
                    if (!extension.Value.TryGetProperty("manifest", out var manifest)) continue;
                    var name = manifest.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
                    var home = manifest.TryGetProperty("homepage_url", out var homeValue) ? homeValue.GetString() ?? "" : "";
                    if ((name.Equals("PT Depiler", StringComparison.OrdinalIgnoreCase) || home.Contains("pt-plugins/PT-depiler", StringComparison.OrdinalIgnoreCase)) && Regex.IsMatch(extension.Name, @"^[a-p]{32}$", RegexOptions.IgnoreCase))
                        return extension.Name;
                }
            }
            catch (Exception ex) { DiagnosticLogger.Write($"PT-Depiler preference discovery failed; File={preferenceName}; Error={ex.Message}"); }
        }
        return "";
    }

    public async Task<bool> HasDoubanPageAsync() => await FindPageAsync(u => u.Host.EndsWith("douban.com", StringComparison.OrdinalIgnoreCase)) is not null;

    public async Task<DoubanSearchPageResult> ReadDoubanMovieSearchPageAsync(string searchText, int start)
    {
        searchText = searchText.Trim();
        if (string.IsNullOrWhiteSpace(searchText)) return new(false, false, [], false, "请输入片名。");
        await _doubanNavigationGate.WaitAsync();
        try
        {
            if (Session is null || !await IsAvailableAsync(Session.Port)) return new(false, false, [], false, "独立 Chrome 未连接。");
            var worker = await EnsureDoubanWorkerAsync();
            var url = $"https://search.douban.com/movie/subject_search?search_text={Uri.EscapeDataString(searchText)}&cat=1002&start={Math.Max(0, start)}";
            DiagnosticLogger.Write($"Douban search START; Text={searchText}; Start={start}; Url={url}");
            await SendCdpCommandAsync(worker.WebSocketUrl, "Page.navigate", new { url }, 20);
            JsonElement? value = null;
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                value = await EvaluateAsync(worker.WebSocketUrl, DoubanSearchExtractionScript);
                if (value is null) continue;
                var root = value.Value;
                if (root.TryGetProperty("ready", out var ready) && ready.GetBoolean()) break;
            }
            if (value is null) return new(false, false, [], false, "豆瓣搜索页在 30 秒内没有返回结果。");
            var result = value.Value;
            if (!result.TryGetProperty("ready", out var finalReady) || !finalReady.GetBoolean())
                return new(false, false, [], false, "豆瓣搜索页在 30 秒内没有返回可用结果。");
            var captcha = result.TryGetProperty("captcha", out var cp) && cp.GetBoolean();
            var loggedIn = !result.TryGetProperty("loggedIn", out var li) || li.GetBoolean();
            var error = result.TryGetProperty("error", out var er) ? er.GetString() ?? "" : "";
            if (captcha) return new(loggedIn, true, [], false, "豆瓣要求人工验证，请在观影浏览器中完成验证后重试。");
            if (!loggedIn) return new(false, false, [], false, "独立浏览器中的豆瓣尚未登录。");
            var items = new List<DoubanSearchCandidate>();
            if (result.TryGetProperty("items", out var array))
                foreach (var item in array.EnumerateArray())
                {
                    var id = item.TryGetProperty("subjectId", out var idValue) ? idValue.GetString() ?? "" : "";
                    var subjectUrl = item.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? "" : "";
                    var posterUrl = item.TryGetProperty("poster", out var posterValue) ? posterValue.GetString() ?? "" : "";
                    var visibleText = item.TryGetProperty("visibleText", out var textValue) ? textValue.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(subjectUrl) && !string.IsNullOrWhiteSpace(visibleText))
                    {
                        var statusOptions = new List<DoubanStatusOption>();
                        if (item.TryGetProperty("statusOptions", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                            foreach (var status in statuses.EnumerateArray())
                            {
                                var text = status.TryGetProperty("text", out var statusText) ? statusText.GetString() ?? "" : "";
                                var selected = status.TryGetProperty("selected", out var selectedValue) && selectedValue.GetBoolean();
                                if (!string.IsNullOrWhiteSpace(text)) statusOptions.Add(new DoubanStatusOption(text, selected));
                            }
                        items.Add(new DoubanSearchCandidate(id, subjectUrl, posterUrl, visibleText, statusOptions));
                    }
                }
            var hasMore = result.TryGetProperty("hasMore", out var next) && next.GetBoolean();
            var debug = result.TryGetProperty("debug", out var debugValue) ? debugValue.ToString() : "";
            DiagnosticLogger.Write($"Douban search RESULT; Text={searchText}; Start={start}; Items={items.Count}; HasMore={hasMore}; Error={error}; Debug={debug}");
            return new(loggedIn, false, items, hasMore, error);
        }
        finally
        {
            await CloseDoubanWorkerCoreAsync();
            _doubanNavigationGate.Release();
        }
    }

    public async Task<DoubanSubjectMetadataResult> ReadDoubanSubjectMetadataAsync(string subjectUrl)
    {
        await _doubanNavigationGate.WaitAsync();
        try
        {
        if (Session is null || !await IsAvailableAsync(Session.Port)) return new("", null, "", false, false, 0, "独立 Chrome 未连接");
        var worker = await EnsureDoubanWorkerAsync();
        JsonElement? navigation = null;
        try { navigation = await SendCdpCommandAsync(worker.WebSocketUrl, "Page.navigate", new { url = subjectUrl }, 20); }
        catch (OperationCanceledException)
        {
            var navigated = await FindTargetByIdAsync(worker.TargetId);
            if (navigated is null || !navigated.Value.Url.StartsWith(subjectUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return new("", null, "", false, true, 0, "Chrome 导航详情页超时");
        }
        if (navigation is not null && navigation.Value.TryGetProperty("errorText", out var navigationError) && !string.IsNullOrWhiteSpace(navigationError.GetString()))
            return new("", null, "", false, true, 0, navigationError.GetString() ?? "Chrome 导航失败");
        JsonElement? value = null;
        string lastEvaluateError = "";
        var consecutiveEvaluateFailures = 0;
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(500);
            try
            {
                value = await EvaluateAsync(worker.WebSocketUrl, DoubanRenderedSubjectScript);
                consecutiveEvaluateFailures = 0;
            }
            catch (Exception ex)
            {
                lastEvaluateError = ex.ToString();
                consecutiveEvaluateFailures++;
                DiagnosticLogger.Write($"Runtime.evaluate exception; Expression=DoubanRenderedSubjectScript; Attempt={i + 1}; ConsecutiveFailures={consecutiveEvaluateFailures}; Error={ex}");
                if (consecutiveEvaluateFailures >= 3) break;
            }
            if (value is null) continue;
            var pageState = value.Value;
            var ready = pageState.TryGetProperty("ready", out var readyValue) && readyValue.GetBoolean();
            var captcha = pageState.TryGetProperty("captcha", out var captchaValue) && captchaValue.GetBoolean();
            if (ready || captcha) break;
        }
        if (value is null)
        {
            string pageProbe;
            try
            {
                var page = await EvaluateAsync(worker.WebSocketUrl, "({title:document.title,href:location.href,readyState:document.readyState,bodyText:(document.body?.innerText||'').substring(0,500)})");
                pageProbe = page?.ToString() ?? "null";
            }
            catch (Exception probeException)
            {
                pageProbe = "probe-failed: " + probeException;
            }
            var failureReason = string.IsNullOrWhiteSpace(lastEvaluateError)
                ? "Chrome 详情页在 30 秒内没有就绪"
                : "Chrome 详情页脚本连续执行失败，已保留本地数据";
            DiagnosticLogger.Write($"Subject metadata FAILED; Runtime.evaluate returned no value; LastError={lastEvaluateError}; PageProbe={pageProbe}");
            return new("", null, "", false, true, 0, failureReason);
        }
        var root = value.Value;
        if (root.TryGetProperty("debug", out var subjectDebug))
            DiagnosticLogger.Write($"Subject DOM DEBUG; {subjectDebug}");
        var resultHref = root.TryGetProperty("href", out var currentHref) ? currentHref.GetString() ?? "" : "";
        var resultTitle = root.TryGetProperty("title", out var currentTitle) ? currentTitle.GetString() ?? "" : "";
        var readyResult = root.TryGetProperty("ready", out var finalReady) && finalReady.GetBoolean();
        var captchaResult = (root.TryGetProperty("captcha", out var finalCaptcha) && finalCaptcha.GetBoolean()) || DoubanPageRules.IsRiskControl(resultTitle, resultHref, "");
        if ((!readyResult || !DoubanPageRules.IsExpectedPage(resultHref, subjectUrl)) && !captchaResult)
        {
            DiagnosticLogger.Write($"Subject metadata FAILED; Page never became valid; Href={resultHref}; Result={root}");
            return new("", null, "", false, true, 0, "豆瓣电影详情页没有返回有效内容，已保留本地数据");
        }
        double? score = null;
        if (root.TryGetProperty("score", out var s) && s.TryGetDouble(out var n) && n is > 0 and <= 10) score = n;
        var metadata = new DoubanSubjectMetadataResult(
            root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            score,
            root.TryGetProperty("poster", out var p) ? p.GetString() ?? "" : "",
            root.TryGetProperty("captcha", out var c) && c.GetBoolean(),
            !root.TryGetProperty("loggedIn", out var l) || l.GetBoolean(),
            captchaResult ? 429 : 200,
            root.TryGetProperty("error", out var error) ? error.GetString() ?? "" : "")
        {
            Year = root.TryGetProperty("year", out var year) ? year.GetString() ?? "" : "",
            Genres = root.TryGetProperty("genres", out var genres) ? genres.GetString() ?? "" : "",
            Directors = root.TryGetProperty("directors", out var directors) ? directors.GetString() ?? "" : "",
            Runtime = root.TryGetProperty("runtime", out var runtime) ? runtime.GetString() ?? "" : "",
            Countries = root.TryGetProperty("countries", out var countries) ? countries.GetString() ?? "" : "",
            ImdbId = root.TryGetProperty("imdbId", out var imdb) ? imdb.GetString() ?? "" : "",
            Summary = root.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "",
            StatusOptions = ReadStatusOptions(root)
        };
        DiagnosticLogger.Write($"Subject metadata RESULT; Title={metadata.Title}");
        return metadata;
        }
        finally
        {
            await CloseDoubanWorkerCoreAsync();
            _doubanNavigationGate.Release();
        }
    }

    internal static List<DoubanStatusOption> ReadStatusOptions(JsonElement root)
    {
        var result = new List<DoubanStatusOption>();
        if (!root.TryGetProperty("statusOptions", out var statuses) || statuses.ValueKind != JsonValueKind.Array) return result;
        foreach (var status in statuses.EnumerateArray())
        {
            var text = status.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? "" : "";
            var selected = status.TryGetProperty("selected", out var selectedValue) && selectedValue.GetBoolean();
            if (!string.IsNullOrWhiteSpace(text) && result.All(x => !x.Text.Equals(text, StringComparison.Ordinal)))
                result.Add(new DoubanStatusOption(text, selected));
        }
        return result;
    }

    private async Task<(string TargetId, string WebSocketUrl)> EnsureDoubanWorkerAsync()
    {
        if (!string.IsNullOrWhiteSpace(_doubanWorkerTargetId) && !string.IsNullOrWhiteSpace(_doubanWorkerWebSocket))
        {
            var existing = await FindTargetByIdAsync(_doubanWorkerTargetId);
            if (existing is not null) { _doubanWorkerWebSocket = existing.Value.WebSocketUrl; return (_doubanWorkerTargetId, _doubanWorkerWebSocket); }
        }
        if (Session is null) throw new InvalidOperationException("独立 Chrome 未连接。");
        using var version = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/version"));
        if (!version.RootElement.TryGetProperty("webSocketDebuggerUrl", out var browserSocket))
            throw new InvalidOperationException("独立 Chrome 没有提供后台标签页接口。");
        var created = await SendCdpCommandAsync(browserSocket.GetString()!, "Target.createTarget", new { url = DoubanWorkerMarkerUrl, background = true }, 15);
        if (created is null || !created.Value.TryGetProperty("targetId", out var targetIdValue))
            throw new InvalidOperationException("无法创建豆瓣后台详情标签页。");
        _doubanWorkerTargetId = targetIdValue.GetString() ?? "";
        for (var i = 0; i < 20; i++)
        {
            var target = await FindTargetByIdAsync(_doubanWorkerTargetId);
            if (target is not null)
            {
                _doubanWorkerWebSocket = target.Value.WebSocketUrl;
                return (_doubanWorkerTargetId, _doubanWorkerWebSocket);
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("豆瓣后台详情标签页没有就绪。");
    }

    internal async Task RestoreDoubanWorkerAsync()
    {
        await _doubanNavigationGate.WaitAsync();
        try { await CloseDoubanWorkerCoreAsync(); }
        finally { _doubanNavigationGate.Release(); }
    }

    private async Task CloseDoubanWorkerCoreAsync()
    {
        var targetId = _doubanWorkerTargetId;
        if (string.IsNullOrWhiteSpace(targetId) || Session is null || !await IsAvailableAsync(Session.Port)) return;
        try
        {
            using var version = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/version"));
            if (!version.RootElement.TryGetProperty("webSocketDebuggerUrl", out var browserSocket)) return;
            var result = await SendCdpCommandAsync(browserSocket.GetString()!, "Target.closeTarget", new { targetId }, 10);
            if (result is not null && result.Value.TryGetProperty("success", out var success) && !success.GetBoolean())
                throw new InvalidOperationException("Chrome 拒绝关闭豆瓣后台标签页。");
            if (_doubanWorkerTargetId == targetId) { _doubanWorkerTargetId = ""; _doubanWorkerWebSocket = ""; }
            DiagnosticLogger.Write($"Douban worker target closed; TargetId={targetId}");
        }
        catch (Exception ex) { DiagnosticLogger.Write($"Douban worker target close failed; TargetId={targetId}; Error={ex.Message}"); }
    }

    internal async Task<int> CleanupStaleDoubanWorkerTargetsAsync()
    {
        if (Session is null || !await IsAvailableAsync(Session.Port)) return 0;
        try
        {
            using var pages = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/list"));
            var pageTargets = pages.RootElement.EnumerateArray()
                .Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "page")
                .Select(x => new
                {
                    Id = x.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Url = x.TryGetProperty("url", out var url) ? url.GetString() ?? "" : ""
                }).Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
            var disposable = pageTargets.Where(x => IsDisposableWorkerUrl(x.Url)).ToList();
            if (disposable.Count == 0) return 0;
            if (pageTargets.Count == disposable.Count) disposable = disposable.Skip(1).ToList();
            if (disposable.Count == 0) return 0;
            using var version = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/version"));
            if (!version.RootElement.TryGetProperty("webSocketDebuggerUrl", out var browserSocket)) return 0;
            var closed = 0;
            foreach (var target in disposable)
            {
                try
                {
                    var result = await SendCdpCommandAsync(browserSocket.GetString()!, "Target.closeTarget", new { targetId = target.Id }, 10);
                    if (result is not null && (!result.Value.TryGetProperty("success", out var success) || success.GetBoolean())) closed++;
                }
                catch (Exception ex) { DiagnosticLogger.Write($"Stale blank target close failed; TargetId={target.Id}; Error={ex.Message}"); }
            }
            DiagnosticLogger.Write($"Stale blank targets cleaned; Count={closed}");
            return closed;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write("Stale blank target cleanup failed: " + ex.Message);
            return 0;
        }
    }

    internal static bool IsDisposableWorkerUrl(string url) =>
        url.Equals(DoubanWorkerMarkerUrl, StringComparison.OrdinalIgnoreCase) ||
        url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

    public async Task<BrowserMediaSnapshot?> ReadIqiyiAsync()
    {
        if (Session is null || !await IsAvailableAsync(Session.Port)) return null;
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/list"));
        foreach (var target in doc.RootElement.EnumerateArray())
        {
            if (target.GetProperty("type").GetString() != "page") continue;
            var url = target.GetProperty("url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("iqiyi.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (!target.TryGetProperty("webSocketDebuggerUrl", out var wsProp)) continue;
            var value = await EvaluateAsync(wsProp.GetString()!, ExtractionScript);
            if (value is null) continue;
            var root = value.Value;
            var duration = ReadDouble(root, "duration");
            if (duration <= 0) continue;
            var title = CleanTitle(root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "");
            if (string.IsNullOrWhiteSpace(title)) title = CleanTitle(target.GetProperty("title").GetString() ?? "爱奇艺影片");
            int? year = null; if (root.TryGetProperty("year", out var y) && int.TryParse(Regex.Match(y.GetString() ?? "", @"(?:19|20)\d{2}").Value, out var parsed)) year = parsed;
            return new BrowserMediaSnapshot(target.GetProperty("id").GetString() ?? "", url, title, year,
                root.TryGetProperty("genre", out var g) ? g.GetString() ?? "" : "", duration, ReadDouble(root, "currentTime"), root.GetProperty("paused").GetBoolean());
        }
        return null;
    }

    private async Task<JsonElement?> EvaluateAsync(string websocketUrl, string expression, bool awaitPromise = false)
    {
        // Always return a JSON string envelope. Chrome sometimes omits RemoteObject.value
        // for complex object graphs even when returnByValue=true; serializing inside the
        // page makes the transport deterministic and also lets us preserve JS exception
        // details together with the current page state.
        var wrappedExpression =
            "(()=>{try{const value=(" + expression +
            ");return JSON.stringify({ok:true,value:value,href:location.href||'',title:document.title||'',readyState:document.readyState||''});}" +
            "catch(error){return JSON.stringify({ok:false,error:String(error),description:error&&error.message?String(error.message):'',stack:error&&error.stack?String(error.stack):'',href:location.href||'',title:document.title||'',readyState:document.readyState||''});}})()";

        var result = await SendCdpCommandAsync(websocketUrl, "Runtime.evaluate",
            new { expression = wrappedExpression, returnByValue = true, awaitPromise }, awaitPromise ? 35 : 8);
        var expressionPreview = expression[..Math.Min(120, expression.Length)];
        if (result is null)
        {
            DiagnosticLogger.Write($"Runtime.evaluate DEBUG; Expression={expressionPreview}; Result=null; Timeout={(awaitPromise ? 35 : 8) * 1000}ms");
            return null;
        }

        if (result.Value.TryGetProperty("exceptionDetails", out var exceptionDetails))
        {
            var description = exceptionDetails.TryGetProperty("exception", out var exception) && exception.TryGetProperty("description", out var exceptionDescription)
                ? exceptionDescription.GetString() ?? ""
                : exceptionDetails.TryGetProperty("text", out var exceptionText) ? exceptionText.GetString() ?? "" : "";
            DiagnosticLogger.Write($"Runtime.evaluate DEBUG; Expression={expressionPreview}; ExceptionDetails={exceptionDetails}; Description={description}");
        }

        if (!result.Value.TryGetProperty("result", out var remote))
        {
            DiagnosticLogger.Write($"Runtime.evaluate DEBUG; Expression={expressionPreview}; MissingRemoteResult; Result={result}");
            return null;
        }
        if (!remote.TryGetProperty("value", out var remoteValue))
        {
            var description = remote.TryGetProperty("description", out var remoteDescription) ? remoteDescription.GetString() ?? "" : "";
            DiagnosticLogger.Write($"Runtime.evaluate DEBUG; Expression={expressionPreview}; MissingValue; Description={description}; Remote={remote}");
            return null;
        }

        if (remoteValue.ValueKind != JsonValueKind.String)
            return remoteValue.Clone();

        var envelopeJson = remoteValue.GetString();
        if (string.IsNullOrWhiteSpace(envelopeJson)) return null;
        using var envelopeDocument = JsonDocument.Parse(envelopeJson);
        var envelope = envelopeDocument.RootElement;
        if (envelope.ValueKind != JsonValueKind.Object)
        {
            DiagnosticLogger.Write($"Runtime.evaluate DEBUG; Expression={expressionPreview}; InvalidEnvelope={envelope}; EnvelopeKind={envelope.ValueKind}");
            return null;
        }
        var href = envelope.TryGetProperty("href", out var hrefValue) ? hrefValue.GetString() ?? "" : "";
        var title = envelope.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? "" : "";
        var readyState = envelope.TryGetProperty("readyState", out var readyStateValue) ? readyStateValue.GetString() ?? "" : "";
        var ok = envelope.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        if (!ok)
        {
            var error = envelope.TryGetProperty("error", out var errorValue) ? errorValue.GetString() ?? "JavaScript 执行失败" : "JavaScript 执行失败";
            var description = envelope.TryGetProperty("description", out var descriptionValue) ? descriptionValue.GetString() ?? "" : "";
            var stack = envelope.TryGetProperty("stack", out var stackValue) ? stackValue.GetString() ?? "" : "";
            DiagnosticLogger.Write($"Runtime.evaluate SCRIPT ERROR; Expression={expressionPreview}; Error={error}; Description={description}; Href={href}; Title={title}; ReadyState={readyState}; Stack={stack}");
            throw new InvalidOperationException($"豆瓣页面脚本执行失败：{(string.IsNullOrWhiteSpace(description) ? error : description)}；URL={href}");
        }
        if (!envelope.TryGetProperty("value", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            DiagnosticLogger.Write($"Runtime.evaluate DEBUG; Expression={expressionPreview}; EnvelopeValue=null; Href={href}; Title={title}; ReadyState={readyState}");
            return null;
        }
        return value.Clone();
    }

    private static async Task<JsonElement?> SendCdpCommandAsync(string websocketUrl, string method, object parameters, int timeoutSeconds)
    {
        using var ws = new ClientWebSocket(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)); var token = timeout.Token;
        await ws.ConnectAsync(new Uri(websocketUrl), token);
        var commandId = Interlocked.Increment(ref _nextCdpCommandId);
        var request = JsonSerializer.Serialize(new { id = commandId, method, @params = parameters });
        await ws.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, true, token);
        var buffer = new byte[65536];
        while (true)
        {
            using var output = new MemoryStream();
            while (true) { var part = await ws.ReceiveAsync(buffer, token); output.Write(buffer, 0, part.Count); if (part.EndOfMessage) break; }
            using var response = JsonDocument.Parse(output.ToArray());
            if (!response.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != commandId) continue;
            if (response.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
            return response.RootElement.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
    }

    private async Task<(string WebSocketUrl, string Url)?> FindPageAsync(Func<Uri, bool> predicate)
    {
        if (Session is null || !await IsAvailableAsync(Session.Port)) return null;
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/list"));
        foreach (var target in doc.RootElement.EnumerateArray())
        {
            if (target.GetProperty("type").GetString() != "page") continue;
            var url = target.GetProperty("url").GetString() ?? "";
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && predicate(uri) && target.TryGetProperty("webSocketDebuggerUrl", out var ws)) return (ws.GetString()!, url);
        }
        return null;
    }

    private async Task<(string WebSocketUrl, string Url)?> FindTargetByIdAsync(string targetId)
    {
        if (Session is null || !await IsAvailableAsync(Session.Port)) return null;
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"http://127.0.0.1:{Session.Port}/json/list"));
        foreach (var target in doc.RootElement.EnumerateArray())
        {
            if (!target.TryGetProperty("id", out var id) || id.GetString() != targetId || !target.TryGetProperty("webSocketDebuggerUrl", out var ws)) continue;
            return (ws.GetString()!, target.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "");
        }
        return null;
    }

    private async Task<bool> IsAvailableAsync(int port) { try { using var r = await _http.GetAsync($"http://127.0.0.1:{port}/json/version"); return r.IsSuccessStatusCode; } catch { return false; } }
    private static int GetFreeLoopbackPort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    private static (string Id, string Name, string Path)? FindBrowser(string preferred)
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
        (string Id, string Name, string Path)? Chrome() { foreach (var root in roots) { var p = Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"); if (File.Exists(p)) return ("Chrome", "Google Chrome", p); } return null; }
        (string Id, string Name, string Path)? Edge() { foreach (var root in roots) { var p = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"); if (File.Exists(p)) return ("Edge", "Microsoft Edge", p); } return null; }
        return preferred.Equals("Edge", StringComparison.OrdinalIgnoreCase) ? Edge() ?? Chrome() : Chrome() ?? Edge();
    }
    private static double ReadDouble(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.TryGetDouble(out var n) && double.IsFinite(n) ? n : 0;
    private static string CleanTitle(string s) => Regex.Replace(s, @"\s*[-—_｜|].*?(爱奇艺|iQIYI).*$", "", RegexOptions.IgnoreCase).Trim();
    private const string ExtractionScript = """
(() => {
 const v=[...document.querySelectorAll('video')].sort((a,b)=>(b.clientWidth*b.clientHeight)-(a.clientWidth*a.clientHeight))[0]; if(!v)return null;
 const meta=n=>document.querySelector(`meta[property='${n}'],meta[name='${n}']`)?.content||'';
 const txt=document.body?.innerText?.slice(0,20000)||'';
 const json=[...document.querySelectorAll('script[type="application/ld+json"]')].map(x=>{try{return JSON.parse(x.textContent)}catch{return null}}).flat().find(x=>x&&typeof x==='object')||{};
 const title=meta('og:title')||json.name||document.querySelector('h1')?.innerText||document.title;
 const year=String(json.datePublished||meta('video:release_date')||(txt.match(/(?:19|20)\d{2}/)||[])[0]||'');
 const genre=Array.isArray(json.genre)?json.genre.join(' / '):(json.genre||meta('video:tag')||'');
 return {title,year,genre,duration:Number(v.duration)||0,currentTime:Number(v.currentTime)||0,paused:!!v.paused};
})()
""";
internal const string DoubanProfileProbeScript = """
(()=>{
 const href=location.href||''; const body=document.body?.innerText||''; const account=document.querySelector('.nav-user-account,.top-nav-info .nav-user-account,a[href*="/accounts/logout"]'); const accountUrls=[...(document.querySelectorAll('.nav-user-account a[href],.top-nav-info a[href]')||[])].map(x=>x.href||''); const accountMatch=accountUrls.map(x=>String(x).match(/^https:\/\/(?:www|movie)\.douban\.com\/people\/(\d+)\/?/)).find(Boolean); const accountProfileId=accountMatch?.[1]||'';
 const loginPage=href.includes('accounts.douban.com')||(!account&&/登录豆瓣|登录\/注册/.test(body));
 const urls=[href,...[...document.querySelectorAll('a[href]')].map(x=>x.href||'')]; const match=urls.map(x=>String(x).match(/^https:\/\/(?:www|movie)\.douban\.com\/people\/(\d+)\/?/)).find(Boolean); const profileId=match?.[1]||'';
 const ready=!!profileId&&!loginPage&&!!document.querySelector('body');
 return {href,profileId,accountProfileId,accountLoggedIn:!!account,loginPage,ready,bodyPreview:body.slice(0,200)};
})()
""";
    internal const string DoubanSearchExtractionScript = """
(()=>{
 const body=document.body?.innerText||''; const href=location.href||'';
 const captcha=href.includes('/misc/sorry')||document.title.includes('禁止访问')||!!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image')||/异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
 const subjectLinks=[...document.querySelectorAll('a[href*="movie.douban.com/subject/"]')];
 const seen=new Set(); const items=[];
 for(const link of subjectLinks){
   const url=link.href||''; const subjectId=(url.match(/\/subject\/(\d+)/)||[])[1]||''; if(!subjectId||seen.has(subjectId))continue;
   const node=link.closest('.item-root,.item,.result,.search-result')||link.parentElement?.parentElement||link.parentElement;
   if(!node)continue; seen.add(subjectId);
   const visibleText=(node.innerText||'').replace(/\r/g,'').split('\n').map(x=>x.trim()).filter(Boolean).join('\n');
   const image=node.querySelector('img'); const poster=image?.currentSrc||image?.src||image?.getAttribute('data-src')||image?.getAttribute('data-original')||'';
    const statusOptions=[...node.querySelectorAll('a,button,span,label,input')].map(element=>{const text=(element.value||element.textContent||'').replace(/\s+/g,'').trim();if(!/^(想看|在看|看过)$/.test(text))return null;const classes=String(element.className||'');const selected=element.checked===true||element.getAttribute('aria-pressed')==='true'||element.getAttribute('aria-selected')==='true'||/(?:^|\s)(?:active|selected|on|current)(?:\s|$)/i.test(classes);return {text,selected};}).filter(Boolean).filter((value,index,array)=>array.findIndex(x=>x.text===value.text)===index);
    if(visibleText)items.push({subjectId,url,poster,visibleText,statusOptions});
 }
 const expected=href.includes('search.douban.com/movie/subject_search');
 const loginForm=!!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
 const login=href.includes('accounts.douban.com')||(!expected&&loginForm);
 const noResults=/没有找到|暂无相关|没有搜索到/.test(body);
 const ready=captcha||login||items.length>0||(expected&&noResults);
 const hasMore=!!document.querySelector('.paginator .next a,.paginator a.next,.pagination .next a');
 const error=captcha?'豆瓣要求人工验证':!expected?'Chrome 未停留在豆瓣电影搜索页':'';
 return {ready,captcha,loggedIn:!login,items,hasMore,error,href,debug:{title:document.title,subjectLinkCount:subjectLinks.length,body:body.substring(0,1200)}};
})()
""";
    internal const string DoubanRenderedSubjectScript = """
(()=>{
 const safe=(fn,fallback)=>{try{const value=fn();return value==null?fallback:value;}catch{return fallback;}};
 const clean=value=>String(value||'').replace(/\s+/g,' ').replace(/\s*[（(]豆瓣[）)]\s*$/,'').trim();
 const href=location.href||'';const body=document.body?.innerText||'';
 const captcha=href.includes('/misc/sorry')||document.title.includes('禁止访问')||!!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image')||/异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
 const loggedIn=!href.includes('accounts.douban.com')&&!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
 const usableUrl=value=>{let url=String(value||'').trim().replace(/^url\(["']?|["']?\)$/g,'');if(!url||url.startsWith('data:'))return '';if(url.startsWith('//'))url='https:'+url;try{if(/^https?%3A/i.test(url))url=decodeURIComponent(url);}catch{}return /^https?:\/\//i.test(url)?url:'';};
 const srcset=value=>String(value||'').split(',').map(part=>part.trim().split(/\s+/)).filter(part=>part[0]).sort((a,b)=>(parseFloat(b[1])||0)-(parseFloat(a[1])||0)).map(part=>usableUrl(part[0])).find(Boolean)||'';
 const styleUrl=value=>usableUrl((String(value||'').match(/url\(["']?([^"')]+)["']?\)/i)||[])[1]||'');
 const jsonLd=[];for(const script of document.querySelectorAll('script[type="application/ld+json"]'))safe(()=>{const parsed=JSON.parse(script.textContent||'null');const queue=Array.isArray(parsed)?parsed.slice():[parsed];while(queue.length){const item=queue.shift();if(!item||typeof item!=='object')continue;jsonLd.push(item);if(Array.isArray(item['@graph']))queue.push(...item['@graph']);}},null);const ld=jsonLd.find(item=>item.name||item.headline||item.description)||{};
 let title=clean(document.querySelector('[property="v:itemreviewed"]')?.textContent)||clean(document.querySelector('#content h1 span')?.textContent)||clean(document.querySelector('h1 span')?.textContent)||clean(document.querySelector('meta[property="og:title"]')?.content)||clean(ld.name||ld.headline)||clean(document.title.replace(/\s*[（(]?豆瓣[）)]?.*$/,''));if(/^豆瓣(?:电影)?$/.test(title))title='';
 const score=Number(clean(document.querySelector('[property="v:average"],strong.rating_num,.rating_num')?.textContent))||0;
 const poster=usableUrl(document.querySelector('#mainpic img,.nbgnbg img')?.currentSrc)||usableUrl(document.querySelector('#mainpic img,.nbgnbg img')?.getAttribute('src'))||usableUrl(document.querySelector('meta[property="og:image"]')?.content)||usableUrl(Array.isArray(ld.image)?ld.image[0]:typeof ld.image==='object'?ld.image?.url:ld.image);
 const info=document.querySelector('#info')?.innerText||'';const year=(document.querySelector('.year')?.textContent||String(ld.datePublished||'')).match(/(?:19|20)\d{2}/)?.[0]||'';const genres=[...document.querySelectorAll('[property="v:genre"]')].map(node=>clean(node.textContent)).filter(Boolean).join(' / ')||(Array.isArray(ld.genre)?ld.genre.map(clean).filter(Boolean).join(' / '):clean(ld.genre));const directors=[...document.querySelectorAll('a[rel="v:directedBy"]')].map(node=>clean(node.textContent)).filter(Boolean).join(' / ')||safe(()=>{const value=ld.director;const list=Array.isArray(value)?value:[value];return list.map(item=>clean(typeof item==='string'?item:item?.name)).filter(Boolean).join(' / ');},'');
 const runtime=clean(document.querySelector('[property="v:runtime"]')?.textContent)||clean(ld.duration);const countries=(info.match(/(?:制片国家\/地区|製片國家\/地區)[:：]\s*([^\n]+)/)||[])[1]?.trim()||'';const imdbId=(info.match(/IMDb[:：]\s*(tt\d+)/i)||[])[1]||'';let summary=clean(document.querySelector('[property="v:summary"]')?.textContent)||clean(document.querySelector('#link-report-intra span.all.hidden,#link-report-intra span,.related-info .indent span.all.hidden,.related-info .indent')?.textContent)||clean(ld.description)||clean(document.querySelector('meta[name="description"],meta[property="og:description"]')?.content);summary=summary.replace(/^.*?(?:剧情简介|简介)[:：]\s*/,'').trim();
 const statusOptions=[...document.querySelectorAll('#interest_sectl a,#interest_sectl button,#interest_sectl span,#interest_sectl label,#interest_sectl input,.interest_sect_level a,.interest_sect_level span')].map(element=>{const text=clean(element.value||element.textContent).replace(/\s+/g,'');if(!/^(想看|在看|看过)$/.test(text))return null;const classes=String(element.className||'');const selected=element.checked===true||element.getAttribute('aria-pressed')==='true'||element.getAttribute('aria-selected')==='true'||/(?:^|\s)(?:active|selected|on|current)(?:\s|$)/i.test(classes)||!!element.closest('.a_collect_btn,.collect_btn')?.classList.contains('selected');return {text,selected};}).filter(Boolean).filter((value,index,array)=>array.findIndex(item=>item.text===value.text)===index);
 const expected=/^https:\/\/movie\.douban\.com\/subject\/\d+\/?(?:[?#].*)?$/.test(href);const ready=captcha||(document.readyState!=='loading'&&expected&&!!title);const error=captcha?'豆瓣要求人工验证':href.startsWith('chrome-error://')?'Chrome 页面加载失败':!expected?'Chrome 未停留在目标电影详情页':document.readyState!=='loading'&&!title?'豆瓣详情页未找到片名':'';const debug={title:document.title,href,readyState:document.readyState,summaryLength:summary.length};return {ready,title,score,poster,year,genres,directors,runtime,countries,imdbId,summary,statusOptions,captcha,loggedIn,error,href,debug};
})()
""";

    private static class NativeMethods
    {
        internal const int SW_RESTORE = 9;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr window);
    }
}
