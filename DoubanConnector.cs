namespace QbPotDoubanAi;

internal interface IDoubanConnector
{
    string Name { get; }
    Task<bool> IsLoggedInAsync();
    Task<DoubanSearchPageResult> SearchAsync(string text, int start);
    Task<DoubanSubjectMetadataResult> ReadMetadataAsync(string subjectUrl, bool probeStatusCapabilities = true);
    Task<OfficialReviewSnapshot> ReadOfficialReviewAsync(string subjectUrl);
    Task<ReviewWriteResultV2> SaveDoubanEntryAsync(string subjectUrl, DoubanEntryWriteRequestV2 request);
    Task<DeleteEntryResult> DeleteDoubanEntryAsync(string subjectUrl);
}

internal sealed record DoubanEntryWriteRequest(
    string Status,
    int? Rating,
    bool SetComment,
    string Comment);

internal sealed record DoubanWriteResult(
    bool Success,
    string SubjectUrl,
    string Action,
    string Status,
    int? Rating,
    string Review,
    bool Tombstoned,
    string Error = "",
    string Phase = "",
    string Stage = "",
    string RequestedStatus = "",
    int? RequestedRating = null,
    bool RequestedComment = false,
    int RequestedCommentLength = 0,
    bool LocalUpdated = false,
    bool Settled = false,
    List<DoubanStatusOption>? StatusOptions = null);

internal sealed class DoubanConnectorRouter : IDoubanConnector
{
    private readonly DoubanWebView2Connector _webView2;
    private readonly BrowserCdpService _cdp;
    private readonly string _preferredBrowser;

    internal DoubanConnectorRouter(DoubanWebView2Connector webView2, BrowserCdpService cdp, string preferredBrowser)
    {
        _webView2 = webView2;
        _cdp = cdp;
        _preferredBrowser = preferredBrowser;
    }

    public string Name => "WebView2（CDP回退）";
    public Task<bool> IsLoggedInAsync() => _webView2.IsLoggedInAsync();

    public async Task<DoubanSearchPageResult> SearchAsync(string text, int start)
    {
        try { return await _webView2.SearchAsync(text, start); }
        catch (Exception ex) when (CanFallback(ex))
        {
            DiagnosticLogger.Write("WebView2 search failed; falling back to CDP; Error=" + ex.Message);
            await _cdp.EnsureBackgroundAsync(_preferredBrowser);
            var result = await _cdp.ReadDoubanMovieSearchPageAsync(text, start);
            return result with { Error = string.IsNullOrWhiteSpace(result.Error) ? "已使用观影浏览器回退读取。" : result.Error };
        }
    }

    public async Task<DoubanSubjectMetadataResult> ReadMetadataAsync(string subjectUrl, bool probeStatusCapabilities = true)
    {
        try { return await _webView2.ReadMetadataAsync(subjectUrl, probeStatusCapabilities); }
        catch (Exception ex) when (CanFallback(ex))
        {
            DiagnosticLogger.Write("WebView2 metadata failed; falling back to CDP; Error=" + ex.Message);
            await _cdp.EnsureBackgroundAsync(_preferredBrowser);
            return (await _cdp.ReadDoubanSubjectMetadataAsync(subjectUrl)) with { ConnectorSource = "cdp-fallback" };
        }
    }

    public Task<OfficialReviewSnapshot> ReadOfficialReviewAsync(string subjectUrl) =>
        _webView2.ReadOfficialReviewAsync(subjectUrl);

    public Task<ReviewWriteResultV2> SaveDoubanEntryAsync(string subjectUrl, DoubanEntryWriteRequestV2 request) =>
        _webView2.SaveDoubanEntryAsync(subjectUrl, request);

    public Task<DeleteEntryResult> DeleteDoubanEntryAsync(string subjectUrl) =>
        _webView2.DeleteDoubanEntryAsync(subjectUrl);

    internal static bool CanFallback(Exception ex)
    {
        var text = ex.Message;
        return !text.Contains("验证码", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("风控", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("尚未登录", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("登录窗口正在使用", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("浏览器进程已退出", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("正在自动恢复", StringComparison.OrdinalIgnoreCase);
    }

}
