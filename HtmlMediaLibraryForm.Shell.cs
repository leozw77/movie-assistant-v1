using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private void PostPendingShellDataIfReady()
    {
        if (!_shellDocumentReady || string.IsNullOrWhiteSpace(_pendingShellDataJson) || _doubanPlusView.CoreWebView2 is null) return;
        var payload = _pendingShellDataJson;
        _doubanPlusView.CoreWebView2.PostWebMessageAsJson(payload);
        _pendingShellDataJson = "";
        DiagnosticLogger.Write($"Unified Shell data posted; Bytes={payload.Length}; Payload={payload}");
    }

    private void PostShellMessage(object message)
    {
        if (!_shellDocumentReady || _doubanPlusView.CoreWebView2 is null) return;
        var payload = JsonSerializer.Serialize(message);
        _doubanPlusView.CoreWebView2.PostWebMessageAsJson(payload);
        DiagnosticLogger.Write($"Unified Shell message posted; Bytes={payload.Length}; Type={ReadString(JsonSerializer.SerializeToElement(message), "type")}");
    }

    private bool IsShellMessageSource(string? source) =>
        ReferenceEquals(_doubanPlusView.CoreWebView2, null) is false &&
        (string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase) || string.Equals(source, "data:text/html,", StringComparison.OrdinalIgnoreCase));

    private async Task HandleDoubanPlusWebMessageReceivedAsync(WebView2 responseView, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!IsAllowedDoubanPlusMessageSource(e.Source) && !IsShellMessageSource(e.Source)) return;
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var messageType = ReadString(root, "type");
            if (messageType == "doubanShellReady")
            {
                _shellDocumentReady = true;
                _activeShellViewKind = "explore";
                _doubanPlusView.Visible = true;
                _doubanPlusView.BringToFront();
                _doubanAccountBar.Visible = false;
                PostPendingShellDataIfReady();
                _ = RequestDoubanSourceReadAsync("shell-ready");
                DiagnosticLogger.Write($"Unified Shell ready; Mode={ReadString(root, "mode")}; Version={ReadString(root, "version")}");
                return;
            }
            if (messageType == "doubanShellDataApplied")
            {
                DiagnosticLogger.Write($"Unified Shell data applied; RequestId={ReadString(root, "requestId")}; ItemCount={ReadString(root, "itemCount")}; Error={ReadString(root, "error")}");
                return;
            }
            if (messageType == "doubanShellCardHover")
            {
                DiagnosticLogger.Write($"Unified Shell card hover; SubjectId={ReadString(root, "subjectId")}; Visible={ReadString(root, "visible")}; PanelTextLength={ReadString(root, "panelTextLength")}");
                return;
            }
            if (messageType == "doubanShellPosterFailed")
            {
                var subjectId = RequiredDigits(root, "subjectId");
                var posterUrl = ValidatePosterSourceUrl(ReadBoundedString(root, "posterUrl", 1200));
                if (posterUrl.Length == 0) return;
                var dataUri = await TryFetchDoubanPosterDataUriAsync(posterUrl).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(dataUri))
                {
                    PostShellMessage(new { type = "doubanShellPosterFallback", subjectId, posterUrl, dataUri });
                    DiagnosticLogger.Write($"Unified Shell poster fallback posted; SubjectId={subjectId}; Url={posterUrl}; Bytes={dataUri.Length}");
                }
                return;
            }
            if (messageType == "doubanShellNavigateContentType")
            {
                _activeShellViewKind = "explore";
                await HandleDoubanShellContentTypeAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellNavigatePersonal")
            {
                _activeShellViewKind = "personal";
                await HandleDoubanShellPersonalStatusAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellSearch")
            {
                _activeShellViewKind = "search";
                await HandleDoubanShellSearchAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellSearchPage")
            {
                _activeShellViewKind = "search";
                await HandleDoubanShellSearchPageAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellApplyLocalPersonalFilter")
            {
                await HandleDoubanShellApplyLocalPersonalFilterAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellApplyPersonalFilter")
            {
                await HandleDoubanShellApplyPersonalFilterAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellFilterGroup")
            {
                await HandleDoubanShellFilterGroupAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellApplyFilter")
            {
                await HandleDoubanShellApplyFilterAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellLoadMore")
            {
                await HandleDoubanShellLoadMoreAsync(root).ConfigureAwait(true);
                return;
            }
            if (messageType == "doubanShellLogin")
            {
                ShowDoubanLogin();
                return;
            }
            if (messageType == "doubanShellOpenDetail")
            {
                var subjectId = RequiredDigits(root, "subjectId");
                var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                var detailPayload = JsonSerializer.SerializeToElement(new { subjectId, subjectUrl, requestId = $"shell-{subjectId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" });
                await OpenDoubanPlusDetailAsync(detailPayload, $"shell-{subjectId}").ConfigureAwait(true);
                DiagnosticLogger.Write($"Unified Shell detail requested; SubjectId={subjectId}; Mode={ReadString(root, "mode")}; ReturnUrl={_activeDoubanReturnUrl}");
                return;
            }
            if (messageType == "doubanPersonalOpenSubject")
            {
                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var sourceUri) ||
                    !sourceUri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return;
                var subjectId = RequiredDigits(root, "subjectId");
                var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                var personalUrl = ReadString(root, "personalUrl");
                if (!IsAllowedDoubanPersonalUrl(personalUrl))
                    throw new InvalidDataException("豆瓣个人页面地址无效。");
                _activeDoubanPersonalPageUrl = personalUrl;
                var detailPayload = JsonSerializer.SerializeToElement(new
                {
                    subjectId,
                    subjectUrl,
                    requestId = $"personal-{subjectId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                });
                await OpenDoubanPlusDetailAsync(detailPayload, $"personal-{subjectId}").ConfigureAwait(true);
                DiagnosticLogger.Write($"HTML Douban personal subject click; ProfileId={ReadString(root, "profileId")}; SubjectId={subjectId}; PersonalUrl={personalUrl}; ScrollY={ReadString(root, "scrollY")}");
                return;
            }
            if (messageType == "doubanExploreOpenSubject")
            {
                if (!IsAllowedDoubanExploreOrTvUrl(e.Source)) return;
                var subjectId = RequiredDigits(root, "subjectId");
                var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                var exploreUrl = ReadString(root, "exploreUrl");
                if (!IsAllowedDoubanExploreOrTvUrl(exploreUrl))
                    throw new InvalidDataException("豆瓣探索页面地址无效。");
                var detailPayload = JsonSerializer.SerializeToElement(new
                {
                    subjectId,
                    subjectUrl,
                    requestId = ReadDetailRequestId(root, $"explore-{subjectId}")
                });
                await OpenDoubanPlusDetailAsync(detailPayload, $"explore-{subjectId}").ConfigureAwait(true);
                DiagnosticLogger.Write($"HTML Douban Explore subject click; SubjectId={subjectId}; ExploreUrl={exploreUrl}; ScrollY={ReadString(root, "scrollY")}");
                return;
            }
            if (messageType == "doubanPageRefresh")
            {
                var refreshUrl = ReadString(root, "url");
                var shellSource = IsShellMessageSource(e.Source);
                if ((!shellSource && !IsAllowedDoubanPlusMessageSource(e.Source)) ||
                    (!shellSource && !DoubanWebView2Connector.IsAllowedDoubanTopLevel(refreshUrl))) return;
                _pendingDoubanHistoryReturnUrl = "";
                DiagnosticLogger.Write($"WebView=DoubanPlus; PageRefreshRequested=True; Source={e.Source}; Url={refreshUrl}; RequestId={ReadString(root, "requestId")}");
                if (shellSource)
                {
                    if (string.Equals(ReadString(root, "viewKind"), "watchlist", StringComparison.Ordinal))
                        PostShellMessage(new { type = "doubanShellWatchlistRefresh" });
                    else
                        RefreshDoubanPlusPage();
                }
                else responseView.CoreWebView2?.Reload();
                return;
            }
            if (messageType == "doubanPageHome")
            {
                var homeSource = ReadString(root, "url");
                var shellSource = IsShellMessageSource(e.Source);
                if ((!shellSource && !IsAllowedDoubanPlusMessageSource(e.Source)) ||
                    (!shellSource && !DoubanWebView2Connector.IsAllowedDoubanTopLevel(homeSource))) return;
                await NavigateDoubanHomeAsync().ConfigureAwait(true);
                return;
            }
            if (messageType.StartsWith("doubanWatchlist", StringComparison.Ordinal))
            {
                await HandleWatchlistMessageAsync(root, e.Source, responseView).ConfigureAwait(true);
                return;
            }
            if (messageType != "doubanPlusGmRequest") return;

            var id = ReadString(root, "id");
            var method = ReadString(root, "method").ToUpperInvariant();
            var url = ReadString(root, "url");
            if (id.Length is 0 or > 100 || method is not ("GET" or "POST") || !IsAllowedDoubanPlusRatingUrl(url))
            {
                DiagnosticLogger.Write($"WebView=DoubanPlus; GMRequestRejected; Id={id}; Method={method}; Url={url}");
                throw new InvalidDataException("Douban Plus 外部评分请求无效。");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("headers", out var headersValue) && headersValue.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in headersValue.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                        headers[property.Name] = property.Value.GetString() ?? "";
                }
            }

            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method == "POST")
            {
                var data = root.TryGetProperty("data", out var dataValue) && dataValue.ValueKind == JsonValueKind.String
                    ? dataValue.GetString() ?? ""
                    : "";
                request.Content = new StringContent(data, System.Text.Encoding.UTF8, "application/json");
            }
            foreach (var header in headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Content is not null)
                        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(header.Value);
                }
                else if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                else if (header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var response = await DoubanPlusHttpClient.SendAsync(request).ConfigureAwait(true);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            PostDoubanPlusGmResponse(responseView, id, response.IsSuccessStatusCode, (int)response.StatusCode, responseText, "");
            DiagnosticLogger.Write($"WebView=DoubanPlus; GMRequest; Method={method}; Host={new Uri(url).Host}; Status={(int)response.StatusCode}; Bytes={responseText.Length}");
        }
        catch (Exception ex)
        {
            try
            {
                using var document = JsonDocument.Parse(e.WebMessageAsJson);
                var failedType = ReadString(document.RootElement, "type");
                if (failedType.StartsWith("doubanShell", StringComparison.Ordinal))
                {
                    DiagnosticLogger.Write($"Unified Shell message failed; Type={failedType}; Error={ex.Message}");
                    PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = failedType });
                    PostShellMessage(new { type = "doubanShellFilterError", requestId = ReadString(document.RootElement, "requestId"), error = ex.Message });
                    return;
                }
                var id = ReadString(document.RootElement, "id");
                if (id.Length > 0) PostDoubanPlusGmResponse(responseView, id, false, 0, "", ex.Message);
            }
            catch { }
        }
    }

    private void PostDoubanPlusGmResponse(WebView2 responseView, string id, bool ok, int status, string responseText, string error)
    {
        if (responseView.CoreWebView2 is null) return;
        responseView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "doubanPlusGmResponse",
            id,
            ok,
            status,
            statusText = "",
            responseText,
            error
        }));
    }

}
