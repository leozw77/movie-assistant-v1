using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private static string DoubanContentTypeForUrl(string? url) => IsDoubanSearchPageUrl(url) ? "search" : IsAllowedDoubanTvUrl(url) ? "tv" : IsAllowedDoubanPersonalUrl(url) ? "personal" : "movie";

    private static string DoubanExploreModeForUrl(string? url) => $"explore-{DoubanContentTypeForUrl(url)}";

    private static string DoubanSourceModeForUrl(string? url)
    {
        if (IsDoubanSearchPageUrl(url)) return "search";
        if (IsAllowedDoubanPersonalUrl(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var match = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"^/people/\d+/(collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return match.Success ? $"personal-{match.Groups[1].Value}" : "personal";
        }
        return DoubanExploreModeForUrl(url);
    }

    private static string ReadContentType(JsonElement root, string sourceUrl)
    {
        var contentType = ReadString(root, "contentType");
        return contentType is "movie" or "tv" or "personal" or "search" ? contentType : DoubanContentTypeForUrl(sourceUrl);
    }

    internal static bool IsAllowedDoubanPersonalUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/people/\\d+/(?:collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    internal static bool IsSameDoubanPersonalScope(string? left, string? right)
    {
        if (!IsAllowedDoubanPersonalUrl(left) || !IsAllowedDoubanPersonalUrl(right) ||
            !Uri.TryCreate(left, UriKind.Absolute, out var leftUri) || !Uri.TryCreate(right, UriKind.Absolute, out var rightUri)) return false;
        var leftMatch = System.Text.RegularExpressions.Regex.Match(leftUri.AbsolutePath, @"^/people/(\d+)/(collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var rightMatch = System.Text.RegularExpressions.Regex.Match(rightUri.AbsolutePath, @"^/people/(\d+)/(collect|wish|do)/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return leftMatch.Success && rightMatch.Success &&
               string.Equals(leftMatch.Groups[1].Value, rightMatch.Groups[1].Value, StringComparison.Ordinal) &&
               string.Equals(leftMatch.Groups[2].Value, rightMatch.Groups[2].Value, StringComparison.Ordinal);
    }

    internal static bool IsAllowedDoubanExploreUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/explore", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedDoubanTvUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/tv", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedDoubanExploreOrTvUrl(string? url) =>
        IsAllowedDoubanExploreUrl(url) || IsAllowedDoubanTvUrl(url);

    internal static bool IsAllowedDoubanSourceUrl(string? url) =>
        IsAllowedDoubanExploreOrTvUrl(url) || IsAllowedDoubanPersonalUrl(url) || IsDoubanSearchPageUrl(url);

    internal static bool IsDoubanSearchPageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("search.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/movie/subject_search/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsDoubanSubjectPageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/subject/\\d+/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsDoubanPlusListPageUrl(string? url) =>
        IsDoubanSearchPageUrl(url) || IsAllowedDoubanPersonalUrl(url) || IsAllowedDoubanExploreOrTvUrl(url);

    private static string DoubanReturnButtonText() => "返回";

    private bool CanShowDoubanPlusReturnButton() =>
        IsDoubanSubjectPageUrl(_activeDoubanPlusNavigationUrl) &&
        IsDoubanPlusListPageUrl(_activeDoubanReturnUrl);

    private async Task<bool> ProbeDoubanPlusPageAsync()
    {
        try
        {
            var probe = await _doubanPlusView.CoreWebView2.ExecuteScriptAsync("""
(() => {
  const state = window.__qbDoubanPlusProbe || {};
  return {
       href: location.href,
       title: document.title,
       readyState: document.readyState,
       topFrame: window.top === window,
       systemLoaded: !!state.systemLoaded,
       namedRegisterLoaded: !!state.namedRegisterLoaded,
       importStarted: !!state.importStarted,
       importResolved: !!state.importResolved,
       importRejected: !!state.importRejected,
       importError: state.importError || "",
       importReadyState: state.importReadyState || "",
       domContentLoadedAt: state.domContentLoadedAt || 0,
       bodyClass: document.body?.className || "",
     rootCount: document.querySelectorAll("#atv-douban-root, #qb-douban-personal-root, #qb-douban-explore-root").length,
     personalRootCount: document.querySelectorAll("#qb-douban-personal-root").length,
     exploreRootCount: document.querySelectorAll("#qb-douban-explore-root").length,
    wrapperHidden: document.querySelector("#wrapper")?.style.display || "",
    startedAt: state.startedAt || 0,
    warnings: state.warnings || [],
    errors: state.errors || [],
    rejections: state.rejections || []
  };
})()
""").ConfigureAwait(true);
            DiagnosticLogger.Write($"WebView=DoubanPlus; RuntimeProbe={probe}");
            using var document = JsonDocument.Parse(probe);
            var root = document.RootElement;
            return root.TryGetProperty("topFrame", out var topFrame) && topFrame.GetBoolean()
                && root.TryGetProperty("rootCount", out var rootCount) && rootCount.GetInt32() > 0;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; RuntimeProbeFailed={ex.Message}");
            return false;
        }
    }

    private async Task RetryDoubanPlusNavigationAsync(string url, ulong previousNavigationId)
    {
        try
        {
            await Task.Delay(300).ConfigureAwait(true);
            if (_closing || _pendingDoubanPlusNavigationId != previousNavigationId ||
                !AreEquivalentDoubanNavigationUrls(_activeDoubanPlusNavigationUrl, url)) return;
            NavigateDoubanPlusToUrl(url, "content-probe-retry");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; ContentRecoveryFailed=True; Url={url}; Error={ex.Message}");
        }
    }

    private async Task<bool> HasStableDoubanPlusRootAsync()
    {
        try
        {
            var result = await _doubanPlusView.CoreWebView2.ExecuteScriptAsync("""
(() => {
   const root = document.querySelector("#atv-douban-root, #qb-douban-personal-root, #qb-douban-explore-root");
  const body = document.body;
  const wrapper = document.querySelector("#wrapper");
  const probe = window.__qbDoubanPlusProbe || {};
  if (!root || !body) return { ready: false, reason: "root-missing" };
  const style = getComputedStyle(root);
  const rect = root.getBoundingClientRect();
   const enhanced = body.classList.contains("atv-enhanced") || body.classList.contains("qb-douban-personal-enhanced") || body.classList.contains("qb-douban-explore-enhanced");
  const wrapperHidden = !wrapper || getComputedStyle(wrapper).display === "none";
  const isSearch = location.hostname === "search.douban.com" && /^\/movie\/subject_search\/?$/u.test(location.pathname);
   const isSubject = location.hostname === "movie.douban.com" && /^\/subject\/\d+\/?$/u.test(location.pathname);
   const isPersonal = location.hostname === "movie.douban.com" && /^\/people\/\d+\/(?:collect|wish|do)\/?$/u.test(location.pathname);
   const isExplore = location.hostname === "movie.douban.com" && /^\/(?:explore|tv)\/?$/u.test(location.pathname);
   const isTv = location.hostname === "movie.douban.com" && /^\/tv\/?$/u.test(location.pathname);
  const searchCardCount = root.querySelectorAll(".atv-search-page-card").length;
  const searchEmpty = root.querySelector(".atv-search-page-empty") !== null;
  const nativeSubjectLinkCount = isSearch ? (wrapper?.querySelectorAll("a[href*='/subject/']").length || 0) : 0;
  const detailTitle = root.querySelector(".atv-hero-title") !== null;
   const personalContent = root.querySelector(".qb-personal-header, .qb-personal-content, .qb-personal-empty") !== null;
   const exploreContent = root.querySelector(".qb-explore-header, .qb-explore-grid, .qb-explore-empty") !== null;
   const contentReady = isSearch ? searchCardCount > 0 || (searchEmpty && nativeSubjectLinkCount === 0) : isSubject ? detailTitle : isPersonal ? personalContent : isExplore ? exploreContent : root.childElementCount > 0;
  const ready = enhanced && wrapperHidden && style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) >= 0.98 && rect.width > 0 && rect.height > 0 && probe.importResolved === true && probe.importRejected !== true && contentReady;
   return { ready, contentReady, detailTitle, exploreContent, isExplore, isTv, isPersonal, isSearch, isSubject, nativeSubjectLinkCount, personalContent, searchCardCount, searchEmpty };
})()
""").ConfigureAwait(true);
            DiagnosticLogger.Write($"WebView=DoubanPlus; ContentProbe={result}");
            using var probeDocument = JsonDocument.Parse(result);
            var probeRoot = probeDocument.RootElement;
            return probeRoot.TryGetProperty("ready", out var ready) && ready.GetBoolean();
        }
        catch { return false; }
    }

    private async Task<bool> WaitForStableDoubanPlusRootAsync(ulong navigationId)
    {
        var isExplore = IsAllowedDoubanExploreOrTvUrl(_activeDoubanPlusNavigationUrl);
        var maximumAttempts = isExplore ? 360 : 60;
        for (var attempt = 0; attempt < maximumAttempts && navigationId == _pendingDoubanPlusNavigationId; attempt++)
        {
            if (await HasStableDoubanPlusRootAsync().ConfigureAwait(true))
            {
                DiagnosticLogger.Write($"WebView=DoubanPlus; StableRenderProbe=True; NavigationId={navigationId}; Attempt={attempt}");
                return true;
            }
            await Task.Delay(50).ConfigureAwait(true);
        }
        DiagnosticLogger.Write($"WebView=DoubanPlus; StableRenderProbe=False; NavigationId={navigationId}; TimeoutMs={maximumAttempts * 50}; ExploreWait={isExplore}");
        return false;
    }

    private async Task<object> OpenDoubanPlusDetailAsync(JsonElement payload, string bridgeRequestId)
    {
        var subjectId = RequiredDigits(payload, "subjectId");
        var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
        var requestId = ReadDetailRequestId(payload, bridgeRequestId);
        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        await EnsureDoubanPlusViewAsync().ConfigureAwait(true);
        await EnsureDoubanSubjectViewAsync().ConfigureAwait(true);

        _activeDetailSubjectId = subjectId;
        _activeDetailRequestId = requestId;
        _activeDoubanPlusSubjectUrl = subjectUrl;
        var listUrl = _activeDoubanPlusNavigationUrl;
        if (!IsDoubanPlusListPageUrl(listUrl)) listUrl = _activeDoubanReturnUrl;
        if (!IsDoubanPlusListPageUrl(listUrl))
            throw new InvalidOperationException("无法确定影片详情来源列表。请先返回列表页面。");
        _activeDoubanReturnUrl = listUrl;
        _activeDoubanSubjectNavigationUrl = subjectUrl;
        _returnToLibraryButton.Visible = false;
        _doubanPlusView.Visible = false;
        _doubanSubjectView.Visible = true;
        _doubanSubjectView.BringToFront();
        _doubanSubjectView.CoreWebView2!.Navigate(subjectUrl);
        DiagnosticLogger.Write($"HTML Douban Plus detail opened; WebView=DoubanSubject; ListWebView=DoubanPlus; SubjectId={subjectId}; RequestId={requestId}; Url={subjectUrl}; ReturnUrl={_activeDoubanReturnUrl}; Version={DoubanPlusWebView2Script.Version}; DualVisibleWebViews=True");
        return new { opened = true, source = "douban-plus-dual-webview2", version = DoubanPlusWebView2Script.Version, subjectId, requestId, subjectUrl };
    }

    private async Task<object> OpenDoubanPersonalPageAsync(JsonElement payload)
    {
        var status = ReadString(payload, "status");
        if (status is not ("collect" or "wish" or "do"))
            throw new InvalidDataException("豆瓣个人列表状态无效。");

        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        await EnsureDoubanPlusViewAsync().ConfigureAwait(true);
        var session = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
        if (!session.IsLoggedIn || !System.Text.RegularExpressions.Regex.IsMatch(session.ProfileId ?? "", "^\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("豆瓣尚未登录，请先扫码登录。");

        var personalUrl = $"https://movie.douban.com/people/{session.ProfileId}/{status}";
        _activeDetailSubjectId = "";
        _activeDetailRequestId = "";
        _activeDoubanPlusSubjectUrl = "";
        _activeDoubanPlusNavigationUrl = personalUrl;
        _activeDoubanSubjectNavigationUrl = "";
        _activeDoubanPersonalPageUrl = personalUrl;
        _activeDoubanReturnUrl = "";
        _returnToLibraryButton.Text = "返回";
        _returnToLibraryButton.Visible = false;
        _doubanSubjectView.Visible = false;
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _doubanPlusView.CoreWebView2.Navigate(personalUrl);
        DiagnosticLogger.Write($"HTML Douban personal page opened; WebView=DoubanPlus; ProfileId={session.ProfileId}; Status={status}; Url={personalUrl}; ReadOnly=True");
        return new { opened = true, source = "douban-personal-webview2", profileId = session.ProfileId, status, personalUrl };
    }

    private void ReturnToLibrary()
    {
        if (!_doubanSubjectView.Visible && !IsDoubanSubjectPageUrl(_activeDoubanSubjectNavigationUrl))
        {
            _returnToLibraryButton.Visible = false;
            DiagnosticLogger.Write($"WebView=DoubanSubject; ReturnIgnored=True; CurrentUrl={_activeDoubanSubjectNavigationUrl}; Reason=NoActiveSubjectView");
            return;
        }

        var listUrl = _activeDoubanReturnUrl;
        _doubanSubjectView.Visible = false;
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _returnToLibraryButton.Visible = false;
        _activeDetailSubjectId = "";
        _activeDetailRequestId = "";
        _activeDoubanPlusSubjectUrl = "";
        _activeDoubanSubjectNavigationUrl = "";
        _activeDoubanReturnUrl = "";
        DiagnosticLogger.Write($"WebView=DoubanSubject; DetailClosed=True; Mode=SwitchToListView; ListUrl={listUrl}; ListViewStateRestore=True; NavigationReused=True");
    }

    private bool TryGoBackToExpectedUrl(string expectedUrl, string reason)
    {
        if (!IsDoubanPlusListPageUrl(expectedUrl) || !_doubanPlusView.CoreWebView2.CanGoBack)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnAttempt=False; Reason=NoMatchingBackHistory; ExpectedUrl={expectedUrl}; Operation={reason}");
            return false;
        }

        _pendingDoubanHistoryReturnUrl = expectedUrl;
        try
        {
            _doubanPlusView.CoreWebView2.GoBack();
            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnAttempt=True; ExpectedUrl={expectedUrl}; Operation={reason}");
            return true;
        }
        catch (Exception ex)
        {
            _pendingDoubanHistoryReturnUrl = "";
            DiagnosticLogger.Write($"WebView=DoubanPlus; HistoryReturnAttempt=False; Reason=GoBackFailed; ExpectedUrl={expectedUrl}; Operation={reason}; Error={ex.Message}");
            return false;
        }
    }

    private void NavigateDoubanPlusToUrl(string url, string reason)
    {
        if (_closing) return;
        _activeDoubanPlusNavigationUrl = url;
        _activeDoubanSubjectNavigationUrl = "";
        _activeDoubanPersonalPageUrl = IsAllowedDoubanPersonalUrl(url) ? url : "";
        _returnToLibraryButton.Visible = false;
        _doubanSubjectView.Visible = false;
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _doubanPlusView.CoreWebView2.Navigate(url);
        DiagnosticLogger.Write($"WebView=DoubanPlus; NavigateFallback=True; Reason={reason}; Url={url}");
    }

    private static bool AreEquivalentDoubanNavigationUrls(string? left, string? right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri) || !Uri.TryCreate(right, UriKind.Absolute, out var rightUri)) return false;
        return string.Equals(NormalizeDoubanNavigationUrl(leftUri), NormalizeDoubanNavigationUrl(rightUri), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDoubanNavigationUrl(Uri uri) =>
        $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}{uri.Query}";

    private DoubanHistoryRecord GetWritableRecord(JsonElement payload, bool allowTombstoneRecreate)
    {
        var subjectId = RequiredDigits(payload, "subjectId");
        var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
        var record = FindOrCreateRecord(subjectId, subjectUrl);
        DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML save preflight", record);
        if (record.Tombstoned && !allowTombstoneRecreate)
            throw new InvalidOperationException("该条目已删除；如需重新添加，请选择想看、在看或看过并保存。");
        if (record.Tombstoned)
            DiagnosticLogger.Write($"HTML tombstone re-add requested; SubjectId={subjectId}; TombstonedAt={record.TombstonedAt:O}; Reason={record.TombstoneReason}; tombstone remains until confirmed official readback");
        record.SubjectUrl = subjectUrl;
        return record;
    }

    private void PromoteToHistory(DoubanHistoryRecord record)
    {
        if (_history.Items.TryGetValue(record.SubjectId, out var existing) && !ReferenceEquals(existing, record))
        {
            _history.Items[record.SubjectId] = record;
            _searchCache.Items.Remove(record.SubjectId);
            return;
        }
        if (!_history.Items.ContainsKey(record.SubjectId))
        {
            _history.Items[record.SubjectId] = record;
            _searchCache.Items.Remove(record.SubjectId);
        }
    }

    private static string ReadDetailRequestId(JsonElement payload, string bridgeRequestId)
    {
        var value = ReadString(payload, "requestId").Trim();
        if (string.IsNullOrWhiteSpace(value)) value = bridgeRequestId.Trim();
        return value.Length <= 80 ? value : value[..80];
    }

    private static string ReadString(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static string RequiredString(JsonElement value, string name, int maximumLength) { var result = ReadString(value, name).Trim(); if (result.Length is 0 || result.Length > maximumLength) throw new InvalidDataException($"{name}无效。"); return result; }
    private static string RequiredDigits(JsonElement value, string name) { var result = RequiredString(value, name, 30); if (!result.All(char.IsDigit)) throw new InvalidDataException($"{name}无效。"); return result; }
    private static string RequiredSubjectUrl(JsonElement value, string name) { var result = RequiredString(value, name, 300); if (!DoubanWebView2Connector.IsAllowedSubjectUrl(result)) throw new InvalidDataException("豆瓣影片地址无效。"); return result; }
    private static string RequiredLibraryStatus(JsonElement value, string name)
    {
        var result = RequiredString(value, name, 20);
        return result is "collect" or "wish" or "do" ? result : throw new InvalidDataException("豆瓣列表状态无效。");
    }
    private static int ReadInt(JsonElement value, string name, int minimum, int maximum) => value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) ? Math.Clamp(result, minimum, maximum) : minimum;
    private static int? ReadNullableInt(JsonElement value, string name, int minimum, int maximum)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (!property.TryGetInt32(out var result) || result < minimum || result > maximum) throw new InvalidDataException($"{name}无效。");
        return result;
    }
    private static bool ReadBool(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
    private static ReviewFieldAction ReadReviewFieldAction(JsonElement value, string name)
    {
        var text = ReadString(value, name).Trim().ToLowerInvariant();
        return text switch
        {
            "keep" => ReviewFieldAction.Keep,
            "set" => ReviewFieldAction.Set,
            "clear" => ReviewFieldAction.Clear,
            _ => throw new InvalidDataException($"{name}无效。")
        };
    }
    private static void Copy(string value, Action<string> setter) { if (!string.IsNullOrWhiteSpace(value)) setter(value.Trim()); }
}
