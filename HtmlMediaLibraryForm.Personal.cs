using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private async Task PrefetchMissingPublicScoresFromDetailDomAsync(
        IEnumerable<(string SubjectId, string SubjectUrl)> items)
    {
        foreach (var item in items
                     .Where(item =>
                         !string.IsNullOrWhiteSpace(item.SubjectId) &&
                         item.SubjectId.All(char.IsDigit) &&
                         item.SubjectUrl.StartsWith(
                             "https://movie.douban.com/subject/",
                             StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(item => item.SubjectId))
        {
            if (_closing) return;

            if (_doubanPublicScoreCache.TryGet(item.SubjectId, out var alreadyCached))
            {
                PostShellMessage(new
                {
                    type = "doubanShellPersonalItemMutation",
                    subjectId = item.SubjectId,
                    score = alreadyCached
                });
                continue;
            }

            lock (_doubanPublicScoreFetchGate)
            {
                if (!_doubanPublicScoreFetchRunning.Add(item.SubjectId))
                    continue;
            }

            try
            {
                DiagnosticLogger.Write(
                    $"Douban public score DOM fallback start; SubjectId={item.SubjectId}; Url={item.SubjectUrl}");

                // Reuse the proven detail metadata path. DoubanWebView2Connector
                // serializes navigation with its own navigation gate, so this
                // cannot race another metadata read on the same Detail WebView.
                var metadata = await _detailConnector
                    .ReadMetadataAsync(item.SubjectUrl, probeStatusCapabilities: false)
                    .ConfigureAwait(true);

                if (!metadata.LoggedIn)
                {
                    DiagnosticLogger.Write(
                        $"Douban public score DOM fallback skipped; SubjectId={item.SubjectId}; Reason=NotLoggedIn");
                    continue;
                }

                if (metadata.Score is not > 0 or > 10)
                {
                    DiagnosticLogger.Write(
                        $"Douban public score DOM fallback empty; SubjectId={item.SubjectId}; Error={metadata.Error}");
                    continue;
                }

                await _doubanPublicScoreCache
                    .StoreAsync(item.SubjectId, metadata.Score.Value)
                    .ConfigureAwait(true);

                DiagnosticLogger.Write(
                    $"Douban public score DOM fallback stored; SubjectId={item.SubjectId}; Score={metadata.Score.Value:0.0}");

                if (!_closing)
                {
                    PostShellMessage(new
                    {
                        type = "doubanShellPersonalItemMutation",
                        subjectId = item.SubjectId,
                        score = metadata.Score.Value
                    });
                }
            }
            catch (Exception ex) when (
                ex is InvalidDataException or InvalidOperationException or
                      HttpRequestException or TaskCanceledException)
            {
                DiagnosticLogger.Write(
                    $"Douban public score DOM fallback failed; SubjectId={item.SubjectId}; Error={ex.Message}");
            }
            finally
            {
                lock (_doubanPublicScoreFetchGate)
                    _doubanPublicScoreFetchRunning.Remove(item.SubjectId);
            }
        }
    }
    private JsonElement OverlayPersonalPublicMetadata(
        JsonElement items,
        string profileId,
        out List<(string SubjectId, string SubjectUrl)> missing)
    {
        missing = [];
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return items;

        var patched = new List<System.Text.Json.Nodes.JsonObject>();
        var publicCacheHits = 0;
        var frodoHits = 0;

        foreach (var item in items.EnumerateArray())
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(item.GetRawText()) as System.Text.Json.Nodes.JsonObject
                ?? new System.Text.Json.Nodes.JsonObject();
            var subjectId = node["subjectId"]?.GetValue<string>() ?? "";
            var subjectUrl = node["subjectUrl"]?.GetValue<string>() ?? "";

            if (subjectId.Length > 0 &&
                _doubanPublicScoreCache.TryGet(subjectId, out var cachedScore))
            {
                node["score"] = cachedScore;
                publicCacheHits++;
            }
            else if (subjectId.Length > 0 &&
                     _frodoPersonalIndex.TryGetCachedPublicRating(
                         profileId,
                         subjectId,
                         out var score,
                         out var ratingCount))
            {
                if (score is > 0)
                {
                    node["score"] = score.Value;
                    frodoHits++;
                }
                if (ratingCount is > 0)
                    node["voteCount"] = ratingCount.Value;
            }
            else if (subjectId.Length > 0 &&
                     subjectUrl.StartsWith("https://movie.douban.com/subject/", StringComparison.OrdinalIgnoreCase))
            {
                missing.Add((subjectId, subjectUrl));
            }

            patched.Add(node);
        }

        DiagnosticLogger.Write(
            $"Personal public score overlay; ProfileId={profileId}; Cards={patched.Count}; PublicCacheHits={publicCacheHits}; FrodoHits={frodoHits}; DomFallbackMisses={missing.Count}");
        return JsonSerializer.SerializeToElement(patched);
    }
    private async Task ForwardDoubanSourceResultToShellAsync(JsonElement root, string operation = "")
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Source DOM 读取结果不是 JSON 对象。");
        var sourceUrl = ReadString(root, "url");
        if (!IsAllowedDoubanSourceUrl(sourceUrl)) throw new InvalidDataException("Source 返回的豆瓣列表地址无效。");
        var items = root.TryGetProperty("items", out var itemsValue) && itemsValue.ValueKind == JsonValueKind.Array
            ? itemsValue.Clone()
            : JsonSerializer.SerializeToElement(Array.Empty<object>());

        var sourceKind = root.TryGetProperty("dom", out var overlayDom) && overlayDom.ValueKind == JsonValueKind.Object
            ? ReadString(overlayDom, "source")
            : "";
        var ordinaryPersonalDom =
            FrodoPersonalProvider.TryReadScope(sourceUrl, out var overlayProfileId, out var overlayStatus) &&
            sourceKind is not ("frodo-api" or "frodo-local-index");
        var missingPublicScores = new List<(string SubjectId, string SubjectUrl)>();
        if (ordinaryPersonalDom)
        {
            if (!_frodoPersonalIndex.TryGetStatus(overlayProfileId, overlayStatus, out var overlaySnapshot))
            {
                await _frodoPersonalIndex.LoadCacheAsync(overlayProfileId).ConfigureAwait(true);
                _frodoPersonalIndex.TryGetStatus(overlayProfileId, overlayStatus, out overlaySnapshot);
            }

            if (overlaySnapshot is not null)
            {
                items = OverlayPersonalPublicMetadata(items, overlayProfileId, out missingPublicScores);
                PostFrodoPersonalFilterState(
                    overlayProfileId,
                    overlayStatus,
                    overlaySnapshot,
                    new FrodoPersonalFilterCriteria(),
                    false,
                    overlaySnapshot.Items.Count,
                    overlaySnapshot.Total,
                    overlaySnapshot.Items.Count,
                    0,
                    "");
                DiagnosticLogger.Write($"Personal DOM metadata overlay applied; ProfileId={overlayProfileId}; Status={overlayStatus}; PageItems={items.GetArrayLength()}; MetadataItems={overlaySnapshot.Items.Count}");
            }
            else
            {
                _ = EnsureFrodoPersonalIndexAsync(overlayProfileId, overlayStatus, "personal-dom-capabilities");
                DiagnosticLogger.Write($"Personal DOM metadata overlay pending; ProfileId={overlayProfileId}; Status={overlayStatus}; Reason=IndexMissing");
            }
        }
        // Keep poster URLs as read from the real Douban DOM. The unified Shell
        // loads them directly; list forwarding must not download every
        // cumulative poster again on each paging operation.
        var error = ReadString(root, "error");
        var shellMessage = JsonSerializer.Serialize(new
        {
            type = "doubanShellData",
            requestId = ReadString(root, "requestId"),
            mode = ReadString(root, "mode"),
            generation = root.TryGetProperty("generation", out var generation) && generation.TryGetInt32(out var value) ? value : 0,
            contentType = ReadContentType(root, sourceUrl),
            personalStatus = ReadString(root, "personalStatus"),
            query = ReadString(root, "query"),
            searchPageUrl = IsDoubanSearchPageUrl(sourceUrl) ? sourceUrl : "",
            items,
            filters = root.TryGetProperty("filters", out var filtersValue) ? filtersValue.Clone() : JsonSerializer.SerializeToElement(new { }),
            paging = root.TryGetProperty("paging", out var pagingValue) ? pagingValue.Clone() : JsonSerializer.SerializeToElement(new { hasMore = false }),
            dom = root.TryGetProperty("dom", out var shellDomValue) ? shellDomValue.Clone() : JsonSerializer.SerializeToElement(new { }),
            searchPageLinks = root.TryGetProperty("searchPageLinks", out var searchPageLinksValue) ? searchPageLinksValue.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>()),
            operation,
            error
        });
        _pendingShellDataJson = shellMessage;
        PostPendingShellDataIfReady();

        if (ordinaryPersonalDom && missingPublicScores.Count > 0)
            _ = PrefetchMissingPublicScoresFromDetailDomAsync(missingPublicScores);

        var dom = root.TryGetProperty("dom", out var domValue) ? domValue.ToString() : "{}";
        DiagnosticLogger.Write($"Unified Shell Source DOM JSON forwarded; Url={sourceUrl}; Items={items.GetArrayLength()}; Error={error}; Dom={dom}; Generation={_doubanSourceGeneration}");
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task<JsonElement> ExecuteDoubanSourceBridgeAsync(string method, object? payload = null)
    {
        if (_doubanSourceView.CoreWebView2 is null) throw new InvalidOperationException("豆瓣 Source WebView 尚未准备好。");
        var request = JsonSerializer.Serialize(payload ?? new { });
        var bridgeName = IsAllowedDoubanPersonalUrl(_activeDoubanSourceNavigationUrl) ? "QbDoubanPersonalSourceBridge" : "QbDoubanSourceBridge";
        var script = $"(() => {{ try {{ return window.{bridgeName} && typeof window.{bridgeName}.{method} === 'function' ? window.{bridgeName}.{method}({request}) : {{ ok: false, error: 'Source bridge 方法不存在。' }}; }} catch (error) {{ return {{ ok: false, error: String(error?.message || error) }}; }} }})()";
        var raw = await _doubanSourceView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        using var document = JsonDocument.Parse(raw);
        var result = document.RootElement.Clone();
        if (result.ValueKind == JsonValueKind.String)
        {
            using var nested = JsonDocument.Parse(result.GetString() ?? "{}");
            result = nested.RootElement.Clone();
        }
        return result;
    }

    private async Task<JsonElement> ReadDoubanSourcePageAsync(string requestId, int generation)
    {
        var result = await ExecuteDoubanSourceBridgeAsync("readPage", new
        {
            requestId,
            mode = DoubanSourceModeForUrl(_activeDoubanSourceNavigationUrl),
            generation
        }).ConfigureAwait(true);
        return result;
    }

    private static JsonElement ReadSourceFilters(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Object
            ? filters.Clone()
            : JsonSerializer.SerializeToElement(new { });

    private static JsonElement ReadSourceGroupOptions(JsonElement filters, string title)
    {
        if (filters.ValueKind != JsonValueKind.Object || !filters.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return JsonSerializer.SerializeToElement(Array.Empty<object>());
        foreach (var group in groups.EnumerateArray())
        {
            if (ReadString(group, "title").Equals(title, StringComparison.Ordinal) &&
                group.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                return options.Clone();
        }
        return JsonSerializer.SerializeToElement(Array.Empty<object>());
    }

    private async Task HandleDoubanShellContentTypeAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("content-type");
        var contentType = ReadString(root, "contentType").Trim();
        if (contentType is not ("movie" or "tv"))
            throw new InvalidDataException("豆瓣探索内容类型无效。");

        var requestId = ReadString(root, "requestId");
        var targetUrl = contentType == "tv" ? "https://movie.douban.com/tv/" : "https://movie.douban.com/explore";
        var mode = DoubanExploreModeForUrl(targetUrl);
        PostShellMessage(new { type = "doubanShellContentTypeState", busy = true, contentType, operation = "content-type" });

        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"{mode}-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "content-type-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell content type no-op; ContentType={contentType}; Url={targetUrl}; Generation={generation}");
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        _activeDoubanPersonalPageUrl = "";
        var navigationAttempt = Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        _ = MonitorDoubanSourceContentTypeNavigationAsync(contentType, targetUrl, navigationAttempt);
        DiagnosticLogger.Write($"Unified Shell content type navigation; ContentType={contentType}; Mode={mode}; Url={targetUrl}");
    }

    private async Task HandleDoubanShellSearchAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("search");
        var query = ReadString(root, "query").Trim();
        var requestId = ReadString(root, "requestId");
        if (query.Length is 0 or > 160) throw new InvalidDataException("搜索关键词不能为空，且不能超过 160 个字符。");
        var targetUrl = $"https://search.douban.com/movie/subject_search?search_text={Uri.EscapeDataString(query)}";
        _pendingDoubanSourceReadRequestId = "";
        _pendingDoubanSourceReadOperation = "";
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "search" });

        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"search-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "search-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell search no-op; Query={query}; Url={targetUrl}; Generation={generation}");
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = "";
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell native Douban search navigation; Query={query}; Url={targetUrl}");
    }

    private async Task HandleDoubanShellSearchPageAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("search-page");
        var targetUrl = ReadString(root, "url").Trim();
        var requestId = ReadString(root, "requestId");
        var append = ReadBool(root, "append");
        if (!IsDoubanSearchPageUrl(targetUrl)) throw new InvalidDataException("豆瓣搜索分页地址无效。");

        _pendingDoubanSourceReadRequestId = append ? requestId : "";
        _pendingDoubanSourceReadOperation = append ? "load-more" : "";
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "search-page" });
        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"search-page-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, append ? "load-more" : "search-page-noop").ConfigureAwait(true);
            _pendingDoubanSourceReadRequestId = "";
            _pendingDoubanSourceReadOperation = "";
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = "";
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell native Douban search page navigation; Url={targetUrl}");
    }

    private async Task HandleDoubanShellPersonalStatusAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("personal-status-dom");
        var status = ReadString(root, "status").Trim();
        if (status is not ("collect" or "wish" or "do"))
            throw new InvalidDataException("豆瓣个人影片状态无效。");

        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        var session = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
        if (!session.IsLoggedIn || !System.Text.RegularExpressions.Regex.IsMatch(session.ProfileId ?? "", "^\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("豆瓣尚未登录，请先点击“豆瓣登录”。");

        var targetUrl = $"https://movie.douban.com/people/{session.ProfileId}/{status}";
        var requestId = ReadString(root, "requestId");
        PostShellMessage(new { type = "doubanShellPersonalState", busy = true, personalStatus = status, operation = "personal-status" });

        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var mode = DoubanSourceModeForUrl(targetUrl);
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"{mode}-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-status-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell personal status no-op; Status={status}; Url={targetUrl}; Generation={generation}");
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell personal status navigation; ProfileId={session.ProfileId}; Status={status}; Url={targetUrl}");
    }
    private async Task HandleDoubanShellApplyPersonalFilterAsync(JsonElement root)
    {
        DeactivateFrodoPersonal("personal-dom-filter");
        var targetUrl = ReadString(root, "url").Trim();
        var requestId = ReadString(root, "requestId");
        if (!IsAllowedDoubanPersonalUrl(targetUrl) || !IsSameDoubanPersonalScope(_activeDoubanSourceNavigationUrl, targetUrl))
            throw new InvalidDataException("豆瓣个人筛选地址无效，或筛选范围已离开当前状态。");

        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "personal-filter" });
        if (AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) && _doubanSourceNavigationCompleted)
        {
            var page = await ReadDoubanSourcePageAsync(string.IsNullOrWhiteSpace(requestId) ? $"personal-filter-{generation}" : requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-filter-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell personal filter no-op; Url={targetUrl}; Generation={generation}");
            return;
        }

        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell personal filter navigation; Url={targetUrl}; Generation={generation}");
    }

    private async Task<bool> TryRenderFrodoPersonalStoreAsync(
        string profileId,
        string status,
        string requestId,
        int generation,
        string operation)
    {
        if (!_frodoPersonalIndex.TryGetStatus(profileId, status, out var snapshot)) return false;
        var criteria = new FrodoPersonalFilterCriteria();
        var result = _frodoPersonalIndex.Query(profileId, status, criteria);
        _frodoPersonalQuery.Start(profileId, status, criteria, result);
        var firstPage = _frodoPersonalQuery.TakeInitial();
        var page = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, snapshot);
        await ForwardDoubanSourceResultToShellAsync(page, operation).ConfigureAwait(true);
        DiagnosticLogger.Write($"Frodo personal Store rendered immediately; ProfileId={profileId}; Status={status}; StoreItems={snapshot.Items.Count}; CloudTotal={snapshot.Total}; Shown={_frodoPersonalQuery.Shown}");
        return true;
    }

    private async Task SyncFrodoPersonalStoreInBackgroundAsync(string profileId, string status, string reason)
    {
        try
        {
            var snapshot = await _frodoPersonalIndex.SyncStatusHeadAsync(profileId, status).ConfigureAwait(true);
            if (snapshot is null || !IsCurrentFrodoPersonal(profileId, status)) return;

            var criteria = _frodoPersonalQuery.IsActiveFor(profileId, status)
                ? _frodoPersonalQuery.Criteria
                : new FrodoPersonalFilterCriteria();
            var shownBefore = _frodoPersonalQuery.IsActiveFor(profileId, status)
                ? _frodoPersonalQuery.Shown
                : 20;
            var result = _frodoPersonalIndex.Query(profileId, status, criteria);
            _frodoPersonalQuery.Start(profileId, status, criteria, result);
            var firstPage = _frodoPersonalQuery.TakeInitial();
            while (_frodoPersonalQuery.Shown < Math.Min(shownBefore, _frodoPersonalQuery.Total))
                _frodoPersonalQuery.TakeNext();

            if (shownBefore <= 20)
            {
                var generation = Interlocked.Increment(ref _doubanSourceGeneration);
                var requestId = $"personal-background-sync-{generation}";
                var page = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, snapshot);
                await ForwardDoubanSourceResultToShellAsync(page, "personal-background-sync").ConfigureAwait(true);
            }
            else
            {
                PostFrodoPersonalFilterState(
                    profileId,
                    status,
                    snapshot,
                    criteria,
                    false,
                    snapshot.Items.Count,
                    snapshot.Total,
                    result.Count,
                    _frodoPersonalQuery.Shown,
                    "");
            }

            DiagnosticLogger.Write($"Frodo personal background bounded sync completed; ProfileId={profileId}; Status={status}; Reason={reason}; StoreItems={snapshot.Items.Count}; Total={snapshot.Total}; ShownPreserved={_frodoPersonalQuery.Shown}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            DiagnosticLogger.Write($"Frodo personal background bounded sync deferred; ProfileId={profileId}; Status={status}; Reason={reason}; Error={ex.Message}; StorePreserved=True");
        }
    }

    private void DeactivateFrodoPersonal(string reason)
    {
        if (_frodoPersonalActive)
            DiagnosticLogger.Write($"Unified Shell Frodo personal deactivated; Reason={reason}; Url={_activeDoubanSourceNavigationUrl}");
        _frodoPersonalActive = false;
        _frodoPersonalQuery.Reset();
        _frodoPersonalProvider.Reset();
    }

    private void NavigatePersonalDomFallback(string targetUrl, string requestId, string operation, string reason)
    {
        DeactivateFrodoPersonal(reason);
        _pendingDoubanSourceReadRequestId = requestId;
        _pendingDoubanSourceReadOperation = operation;
        _activeDoubanPlusNavigationUrl = targetUrl;
        _activeDoubanSourceNavigationUrl = targetUrl;
        _activeDoubanPersonalPageUrl = targetUrl;
        _doubanSourceNavigationCompleted = false;
        Interlocked.Increment(ref _doubanSourceNavigationAttempt);
        _doubanSourceView.CoreWebView2!.Navigate(targetUrl);
        DiagnosticLogger.Write($"Unified Shell personal source fallback; Source=DOM; Reason={reason}; Url={targetUrl}; RequestId={requestId}");
    }

    private async Task RefreshFrodoPersonalAsync(string reason)
    {
        if (!_frodoPersonalActive ||
            !FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var profileId, out var status)) return;

        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        var requestId = $"personal-refresh-{generation}";
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "personal-refresh" });

        if (_frodoPersonalIndex.TryGetStatus(profileId, status, out _))
        {
            try
            {
                await _frodoPersonalIndex.SyncStatusHeadAsync(profileId, status).ConfigureAwait(true);
                if (!_frodoPersonalIndex.TryGetStatus(profileId, status, out var refreshed)) return;
                var criteria = _frodoPersonalQuery.IsActiveFor(profileId, status)
                    ? _frodoPersonalQuery.Criteria
                    : new FrodoPersonalFilterCriteria();
                var result = _frodoPersonalIndex.Query(profileId, status, criteria);
                _frodoPersonalQuery.Start(profileId, status, criteria, result);
                var firstPage = _frodoPersonalQuery.TakeInitial();
                var localPage = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, refreshed);
                await ForwardDoubanSourceResultToShellAsync(localPage, "personal-refresh").ConfigureAwait(true);
                DiagnosticLogger.Write($"Unified Shell personal refresh completed; Source=FrodoLocalStore+BoundedSync; Reason={reason}; ProfileId={profileId}; Status={status}; Matched={result.Count}; Generation={generation}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
            {
                // Refresh failure never destroys or abandons a usable Store.
                DiagnosticLogger.Write($"Unified Shell personal bounded refresh deferred; Reason={reason}; ProfileId={profileId}; Status={status}; Error={ex.Message}; StorePreserved=True");
                PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "personal-refresh" });
            }
            return;
        }

        _frodoPersonalQuery.Reset();
        var targetUrl = $"https://movie.douban.com/people/{profileId}/{status}";
        try
        {
            var page = await _frodoPersonalProvider.LoadInitialAsync(profileId, status, targetUrl, requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-refresh").ConfigureAwait(true);
            _ = EnsureFrodoPersonalIndexAsync(profileId, status, "personal-refresh-bootstrap");
            DiagnosticLogger.Write($"Unified Shell personal refresh completed; Source=FrodoBootstrap; Reason={reason}; Url={targetUrl}; Generation={generation}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
        {
            DiagnosticLogger.Write($"Unified Shell personal refresh Frodo failed; Reason={reason}; Url={targetUrl}; Fallback=DOM; Error={ex.Message}");
            NavigatePersonalDomFallback(targetUrl, requestId, "personal-refresh-fallback", "frodo-refresh-failed");
        }
    }
    private async Task MonitorDoubanSourceContentTypeNavigationAsync(string contentType, string targetUrl, int navigationAttempt)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!_closing &&
               navigationAttempt == Volatile.Read(ref _doubanSourceNavigationAttempt) &&
               AreEquivalentDoubanNavigationUrls(_activeDoubanSourceNavigationUrl, targetUrl) &&
               !_doubanSourceNavigationCompleted)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DoubanSourceDomWaitTimeout)
            {
                DiagnosticLogger.Write($"Unified Shell content type navigation timeout; ContentType={contentType}; Url={targetUrl}; Attempt={navigationAttempt}; NavigationVersion={_doubanSourceNavigationVersion}");
                PostShellMessage(new
                {
                    type = "doubanShellContentTypeError",
                    contentType,
                    error = "豆瓣探索页面加载超时，请重试。"
                });
                return;
            }
            await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
        }
    }

    private async Task<JsonElement> WaitForDoubanSourcePageAsync(string requestId, int generation, string beforeSignature, string operation)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        JsonElement result = default;
        while (true)
        {
            result = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            var signature = ReadString(result, "signature");
            var itemCount = result.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;
            var pageReady = ReadBool(result, "pageReady");
            if ((itemCount > 0 || (IsAllowedDoubanPersonalUrl(_activeDoubanSourceNavigationUrl) && pageReady)) &&
                (beforeSignature.Length == 0 || !signature.Equals(beforeSignature, StringComparison.Ordinal))) break;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DoubanSourceDomWaitTimeout) break;
            await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
        }
        DiagnosticLogger.Write($"Unified Shell Source operation settled; Operation={operation}; RequestId={requestId}; Signature={ReadString(result, "signature")}; Items={(result.TryGetProperty("items", out var settledItems) && settledItems.ValueKind == JsonValueKind.Array ? settledItems.GetArrayLength() : 0)}");
        return result;
    }

    private bool IsCurrentPersonalScope(string profileId, string status) =>
        !_closing &&
        FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var activeStatus) &&
        activeProfileId.Equals(profileId, StringComparison.Ordinal) &&
        activeStatus.Equals(status, StringComparison.Ordinal);

    private bool IsCurrentFrodoPersonal(string profileId, string status) =>
        _frodoPersonalActive && IsCurrentPersonalScope(profileId, status);

    private async Task EnsureFrodoPersonalIndexAsync(string profileId, string status, string reason)
    {
        try
        {
            await _frodoPersonalIndex.LoadCacheAsync(profileId).ConfigureAwait(true);
            if (_frodoPersonalIndex.TryGetStatus(profileId, status, out var cached))
            {
                if (IsCurrentPersonalScope(profileId, status))
                    PostFrodoPersonalFilterState(profileId, status, cached, new FrodoPersonalFilterCriteria(), false, cached.Items.Count, cached.Items.Count, cached.Items.Count, 0, "");
                return;
            }

            var key = $"{profileId}:{status}";
            lock (_frodoPersonalIndexBuildGate)
            {
                if (!_frodoPersonalIndexBuilds.Add(key)) return;
            }

            try
            {
                if (IsCurrentPersonalScope(profileId, status))
                    PostFrodoPersonalFilterState(profileId, status, null, new FrodoPersonalFilterCriteria(), true, 0, 0, 0, 0, "");

                var progress = new Progress<FrodoPersonalIndexProgress>(value =>
                {
                    if (!IsCurrentPersonalScope(profileId, status)) return;
                    PostFrodoPersonalFilterState(profileId, status, null, new FrodoPersonalFilterCriteria(), true, value.Loaded, value.Total, 0, 0, "");
                });
                var built = await _frodoPersonalIndex.BootstrapStatusAsync(profileId, status, progress).ConfigureAwait(true);
                if (IsCurrentPersonalScope(profileId, status))
                    PostFrodoPersonalFilterState(profileId, status, built, new FrodoPersonalFilterCriteria(), false, built.Items.Count, built.Items.Count, built.Items.Count, 0, "");
                DiagnosticLogger.Write($"Frodo personal local filter index ready; ProfileId={profileId}; Status={status}; Items={built.Items.Count}; Reason={reason}");
            }
            finally
            {
                lock (_frodoPersonalIndexBuildGate) _frodoPersonalIndexBuilds.Remove(key);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            DiagnosticLogger.Write($"Frodo personal local filter index failed; ProfileId={profileId}; Status={status}; Reason={reason}; Error={ex.Message}");
            if (IsCurrentPersonalScope(profileId, status))
                PostFrodoPersonalFilterState(profileId, status, null, new FrodoPersonalFilterCriteria(), false, 0, 0, 0, 0, ex.Message);
        }
    }

    private void PostFrodoPersonalFilterState(
        string profileId,
        string status,
        FrodoPersonalIndexStatus? snapshot,
        FrodoPersonalFilterCriteria criteria,
        bool building,
        int loaded,
        int sourceTotal,
        int matched,
        int shown,
        string error)
    {
        PostShellMessage(new
        {
            type = "doubanShellLocalPersonalFilters",
            personalStatus = status,
            filters = BuildFrodoPersonalFilterState(profileId, status, snapshot, criteria, building, loaded, sourceTotal, matched, shown, error)
        });
    }

    private static object BuildFrodoPersonalFilterState(
        string profileId,
        string status,
        FrodoPersonalIndexStatus? snapshot,
        FrodoPersonalFilterCriteria criteria,
        bool building,
        int loaded,
        int sourceTotal,
        int matched,
        int shown,
        string error)
    {
        return new
        {
            source = "frodo-local",
            ready = snapshot is not null,
            building,
            loaded,
            sourceTotal,
            total = snapshot?.Items.Count ?? 0,
            matched,
            shown,
            error,
            criteria = new
            {
                contentType = criteria.ContentType,
                playableOnly = criteria.PlayableOnly,
                scoreMin = criteria.ScoreMin,
                scoreMax = criteria.ScoreMax,
                myRating = criteria.MyRating,
                unrated = criteria.Unrated,
                period = criteria.Period,
                genre = criteria.Genre,
                country = criteria.Country,
                sort = criteria.Sort
            },
            facets = new
            {
                years = snapshot?.Years ?? new List<string>(),
                genres = snapshot?.Genres ?? new List<string>(),
                countries = snapshot?.Countries ?? new List<string>()
            }
        };
    }
    private async Task HandleDoubanShellApplyLocalPersonalFilterAsync(JsonElement root)
    {
        if (!FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var profileId, out var status))
            throw new InvalidOperationException("当前个人页不是可识别的豆瓣个人状态页，无法应用完整库筛选。");
        _frodoPersonalActive = true;
        if (!_frodoPersonalIndex.TryGetStatus(profileId, status, out var snapshot))
        {
            _ = EnsureFrodoPersonalIndexAsync(profileId, status, "filter-request-index-missing");
            throw new InvalidOperationException("完整个人库筛选索引仍在建立，请稍后再试。");
        }

        var criteria = ReadFrodoPersonalFilterCriteria(root);
        var isDefaultCriteria =
            string.IsNullOrWhiteSpace(criteria.ContentType) &&
            !criteria.PlayableOnly &&
            criteria.ScoreMin is null &&
            criteria.ScoreMax is null &&
            criteria.MyRating is null &&
            !criteria.Unrated &&
            string.IsNullOrWhiteSpace(criteria.Period) &&
            string.IsNullOrWhiteSpace(criteria.Genre) &&
            string.IsNullOrWhiteSpace(criteria.Country) &&
            string.Equals(criteria.Sort, "marked-desc", StringComparison.Ordinal);
        if (isDefaultCriteria)
        {
            var clearGeneration = Interlocked.Increment(ref _doubanSourceGeneration);
            var clearRequestId = ReadString(root, "requestId");
            if (string.IsNullOrWhiteSpace(clearRequestId))
                clearRequestId = $"personal-filter-clear-{clearGeneration}";
            var clearTargetUrl = $"https://movie.douban.com/people/{profileId}/{status}";
            NavigatePersonalDomFallback(clearTargetUrl, clearRequestId, "personal-filter-clear", "advanced-filter-cleared");
            DiagnosticLogger.Write($"Frodo personal filter cleared; ReturnSource=DOM; ProfileId={profileId}; Status={status}; Generation={clearGeneration}");
            return;
        }
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        var resolvedRequestId = string.IsNullOrWhiteSpace(requestId) ? $"personal-local-filter-{generation}" : requestId;
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "personal-local-filter" });

        var result = _frodoPersonalIndex.Query(profileId, status, criteria);
        _frodoPersonalQuery.Start(profileId, status, criteria, result);
        var firstPage = _frodoPersonalQuery.TakeInitial();
        var page = BuildFrodoPersonalQueryPayload(resolvedRequestId, generation, firstPage, snapshot);
        await ForwardDoubanSourceResultToShellAsync(page, "personal-local-filter").ConfigureAwait(true);
        DiagnosticLogger.Write($"Frodo personal local filter applied; ProfileId={profileId}; Status={status}; Matched={result.Count}; Shown={_frodoPersonalQuery.Shown}; ContentType={criteria.ContentType}; PlayableOnly={criteria.PlayableOnly}; ScoreMin={criteria.ScoreMin?.ToString("0.0") ?? "all"}; ScoreMax={criteria.ScoreMax?.ToString("0.0") ?? "all"}; Rating={criteria.MyRating?.ToString() ?? (criteria.Unrated ? "unrated" : "all")}; Period={criteria.Period}; Genre={criteria.Genre}; Country={criteria.Country}; Sort={criteria.Sort}");
    }

    private static FrodoPersonalFilterCriteria ReadFrodoPersonalFilterCriteria(JsonElement root)
    {
        if (!root.TryGetProperty("criteria", out var criteria) || criteria.ValueKind != JsonValueKind.Object)
            return new FrodoPersonalFilterCriteria();

        var contentType = ReadString(criteria, "contentType").Trim().ToLowerInvariant();
        if (contentType is not ("" or "movie" or "tv")) throw new InvalidDataException("影片类型筛选无效。");

        var playableOnly = ReadBool(criteria, "playableOnly");
        double? scoreMin = null;
        double? scoreMax = null;
        if (criteria.TryGetProperty("scoreMin", out var scoreMinValue) && scoreMinValue.ValueKind == JsonValueKind.Number && scoreMinValue.TryGetDouble(out var parsedMin))
            scoreMin = parsedMin;
        if (criteria.TryGetProperty("scoreMax", out var scoreMaxValue) && scoreMaxValue.ValueKind == JsonValueKind.Number && scoreMaxValue.TryGetDouble(out var parsedMax))
            scoreMax = parsedMax;
        if ((scoreMin is not null && (scoreMin.Value < 0 || scoreMin.Value > 10)) ||
            (scoreMax is not null && (scoreMax.Value < 0 || scoreMax.Value > 10)) ||
            (scoreMin is not null && scoreMax is not null && scoreMin.Value > scoreMax.Value))
            throw new InvalidDataException("豆瓣评分区间无效。");

        var myRating = ReadNullableInt(criteria, "myRating", 1, 5);
        var unrated = ReadBool(criteria, "unrated");
        if (unrated) myRating = null;

        var period = ReadBoundedString(criteria, "period", 24).Trim().ToLowerInvariant();
        if (period.Length > 0)
        {
            var parts = period.Split(':', 2, StringSplitOptions.TrimEntries);
            var kindValid = parts.Length == 2 && (parts[0] == "year" || parts[0] == "decade");
            if (!kindValid || !int.TryParse(parts[1], out var periodValue) ||
                (parts[0] == "year" && (periodValue < 1880 || periodValue > 2100)) ||
                (parts[0] == "decade" && (periodValue < 1880 || periodValue > 2100 || periodValue % 10 != 0)))
                throw new InvalidDataException("年代筛选无效。");
        }

        var genre = ReadBoundedString(criteria, "genre", 80);
        var country = ReadBoundedString(criteria, "country", 80);
        var sort = ReadString(criteria, "sort").Trim();
        if (string.IsNullOrWhiteSpace(sort)) sort = "marked-desc";
        if (sort is not ("marked-desc" or "my-rating-desc" or "douban-score-desc" or "year-desc" or "title-asc"))
            throw new InvalidDataException("个人库排序方式无效。");

        return new FrodoPersonalFilterCriteria(contentType, playableOnly, scoreMin, scoreMax, myRating, unrated, period, genre, country, sort);
    }
    private JsonElement BuildFrodoPersonalQueryPayload(
        string requestId,
        int generation,
        IReadOnlyList<FrodoPersonalItem> items,
        FrodoPersonalIndexStatus snapshot)
    {
        var filters = BuildFrodoPersonalFilterState(
            _frodoPersonalQuery.ProfileId,
            _frodoPersonalQuery.Status,
            snapshot,
            _frodoPersonalQuery.Criteria,
            false,
            snapshot.Items.Count,
            snapshot.Items.Count,
            _frodoPersonalQuery.Total,
            _frodoPersonalQuery.Shown,
            "");
        return JsonSerializer.SerializeToElement(new
        {
            requestId,
            mode = $"personal-{_frodoPersonalQuery.Status}",
            generation,
            url = _activeDoubanSourceNavigationUrl,
            contentType = "personal",
            personalStatus = _frodoPersonalQuery.Status,
            profileId = _frodoPersonalQuery.ProfileId,
            pageReady = true,
            items,
            paging = new { hasMore = _frodoPersonalQuery.HasMore, label = "加载更多" },
            filters,
            signature = string.Join("|", items.Select(item => $"{item.SubjectId}:{item.Title}")),
            dom = new { gridItemCount = 0, paginator = _frodoPersonalQuery.HasMore, ready = true, source = "frodo-local-index", total = _frodoPersonalQuery.Total, nextStart = _frodoPersonalQuery.Shown },
            error = ""
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
    private async Task SyncFrodoPersonalAfterConfirmedWriteAsync(DoubanHistoryRecord record, string beforeStatus, string reason)
    {
        try
        {
            var profileId = _frodoPersonalIndex.CurrentProfileId;
            if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
                record.Status is not ("collect" or "wish" or "do")) return;

            var providerItem = await _frodoPersonalProvider.ApplyConfirmedReviewAsync(
                record.SubjectId,
                beforeStatus,
                record.Status,
                record.Rating,
                record.Comment,
                record.MarkedDate).ConfigureAwait(true);
            var indexedItem = await _frodoPersonalIndex.ApplyConfirmedReviewAsync(
                profileId,
                record.SubjectId,
                record.Status,
                record.Rating,
                record.Comment,
                record.MarkedDate).ConfigureAwait(true);

            var insertedFromRecentReadback = false;
            if (indexedItem is null)
            {
                var recent = await _frodoPersonalIndex.FetchRecentItemAsync(
                    profileId,
                    record.Status,
                    record.SubjectId).ConfigureAwait(true);
                if (recent is not null)
                {
                    providerItem = await _frodoPersonalProvider.ApplyConfirmedReviewAsync(
                        record.SubjectId,
                        beforeStatus,
                        record.Status,
                        record.Rating,
                        record.Comment,
                        record.MarkedDate,
                        recent).ConfigureAwait(true);
                    indexedItem = await _frodoPersonalIndex.ApplyConfirmedReviewAsync(
                        profileId,
                        record.SubjectId,
                        record.Status,
                        record.Rating,
                        record.Comment,
                        record.MarkedDate,
                        recent).ConfigureAwait(true);
                    insertedFromRecentReadback = indexedItem is not null;
                }
            }

            if (indexedItem is null)
            {
                DiagnosticLogger.Write($"Frodo personal authoritative write index upsert deferred; SubjectId={record.SubjectId}; BeforeStatus={beforeStatus}; TargetStatus={record.Status}; Reason={reason}; Cause=RecentFrodoItemNotVisible");
            }

            var currentStatus = FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var activeStatus) &&
                                activeProfileId.Equals(profileId, StringComparison.Ordinal)
                ? activeStatus
                : "";
            var filtered = currentStatus.Length > 0 && _frodoPersonalQuery.IsActiveFor(profileId, currentStatus);
            await RefreshFrodoPersonalUiAfterMutationAsync(
                profileId,
                record.SubjectId,
                beforeStatus,
                record.Status,
                deleted: false,
                myRating: record.Rating,
                score: indexedItem?.Score ?? providerItem?.Score,
                reason: insertedFromRecentReadback ? reason + "-upsert" : reason).ConfigureAwait(true);

            if (insertedFromRecentReadback && !filtered && _frodoPersonalActive &&
                currentStatus.Equals(record.Status, StringComparison.Ordinal))
            {
                await RefreshFrodoPersonalAsync("authoritative-new-interest-upsert").ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException or HttpRequestException or TaskCanceledException)
        {
            DiagnosticLogger.Write($"Frodo personal authoritative write sync failed; SubjectId={record.SubjectId}; BeforeStatus={beforeStatus}; TargetStatus={record.Status}; Reason={reason}; Error={ex.Message}");
        }
    }
    private async Task SyncFrodoPersonalAfterConfirmedDeleteAsync(string subjectId, string beforeStatus, string reason)
    {
        try
        {
            var profileId = _frodoPersonalIndex.CurrentProfileId;
            if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit)) return;
            await _frodoPersonalProvider.ApplyConfirmedDeleteAsync(subjectId).ConfigureAwait(true);
            await _frodoPersonalIndex.ApplyConfirmedDeleteAsync(profileId, subjectId).ConfigureAwait(true);
            await RefreshFrodoPersonalUiAfterMutationAsync(
                profileId,
                subjectId,
                beforeStatus,
                "deleted",
                deleted: true,
                myRating: null,
                score: null,
                reason: reason).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            DiagnosticLogger.Write($"Frodo personal authoritative delete sync failed; SubjectId={subjectId}; BeforeStatus={beforeStatus}; Reason={reason}; Error={ex.Message}");
        }
    }

    private async Task RefreshFrodoPersonalUiAfterMutationAsync(
        string profileId,
        string subjectId,
        string beforeStatus,
        string targetStatus,
        bool deleted,
        int? myRating,
        double? score,
        string reason)
    {
        if (!_frodoPersonalActive ||
            !FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var currentStatus) ||
            !activeProfileId.Equals(profileId, StringComparison.Ordinal)) return;
        if (!currentStatus.Equals(beforeStatus, StringComparison.Ordinal) &&
            !currentStatus.Equals(targetStatus, StringComparison.Ordinal)) return;

        if (_frodoPersonalQuery.IsActiveFor(profileId, currentStatus) &&
            _frodoPersonalIndex.TryGetStatus(profileId, currentStatus, out var filteredSnapshot))
        {
            var criteria = _frodoPersonalQuery.Criteria;
            var result = _frodoPersonalIndex.Query(profileId, currentStatus, criteria);
            _frodoPersonalQuery.Start(profileId, currentStatus, criteria, result);
            var firstPage = _frodoPersonalQuery.TakeInitial();
            var generation = Interlocked.Increment(ref _doubanSourceGeneration);
            var requestId = $"personal-authoritative-sync-{generation}";
            var page = BuildFrodoPersonalQueryPayload(requestId, generation, firstPage, filteredSnapshot);
            await ForwardDoubanSourceResultToShellAsync(page, "personal-authoritative-sync").ConfigureAwait(true);
            DiagnosticLogger.Write($"Frodo personal filtered view recomputed after authoritative mutation; SubjectId={subjectId}; Status={currentStatus}; Matched={result.Count}; Reason={reason}");
            return;
        }

        if (_frodoPersonalIndex.TryGetStatus(profileId, currentStatus, out var snapshot))
        {
            PostFrodoPersonalFilterState(
                profileId,
                currentStatus,
                snapshot,
                new FrodoPersonalFilterCriteria(),
                false,
                snapshot.Items.Count,
                snapshot.Items.Count,
                snapshot.Items.Count,
                0,
                "");
        }

        PostShellMessage(new
        {
            type = "doubanShellPersonalItemMutation",
            subjectId,
            fromStatus = beforeStatus,
            toStatus = targetStatus,
            deleted,
            myRating,
            score
        });
        DiagnosticLogger.Write($"Frodo personal card mutation posted; SubjectId={subjectId}; BeforeStatus={beforeStatus}; TargetStatus={targetStatus}; Deleted={deleted}; Rating={myRating?.ToString() ?? "null"}; Reason={reason}");
    }
    private async Task HandleDoubanShellFilterGroupAsync(JsonElement root)
    {
        var title = RequiredString(root, "title", 120);
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "filter-group" });
        var action = await ExecuteDoubanSourceBridgeAsync("openFilterGroup", new { title }).ConfigureAwait(true);
        if (!ReadBool(action, "ok"))
        {
            PostShellMessage(new { type = "doubanShellFilterError", requestId, error = ReadString(action, "error") });
            return;
        }

        JsonElement page = default;
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            page = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            var options = ReadSourceGroupOptions(ReadSourceFilters(page), title);
            if (options.GetArrayLength() > 0 || System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DoubanSourceDomWaitTimeout) break;
            await Task.Delay(DoubanSourceDomPollInterval).ConfigureAwait(true);
        }
        var filters = ReadSourceFilters(page);
        var optionsResult = ReadSourceGroupOptions(filters, title);
        PostShellMessage(new { type = "doubanShellFilterOptions", requestId, groupTitle = title, options = optionsResult, filters });
        DiagnosticLogger.Write($"Unified Shell native filter group opened; Title={title}; Options={optionsResult.GetArrayLength()}; Generation={generation}");
    }

    private async Task HandleDoubanShellApplyFilterAsync(JsonElement root)
    {
        var kind = RequiredString(root, "kind", 30);
        var label = RequiredString(root, "label", 160);
        var title = ReadString(root, "title").Trim();
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "filter" });
        var action = await ExecuteDoubanSourceBridgeAsync("selectFilter", new { kind, label, title }).ConfigureAwait(true);
        if (!ReadBool(action, "ok"))
        {
            PostShellMessage(new { type = "doubanShellFilterError", requestId, error = ReadString(action, "error") });
            return;
        }
        var beforeSignature = ReadString(action, "beforeSignature");
        if (ReadBool(action, "noOp"))
        {
            var currentPage = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(currentPage, "filter-noop").ConfigureAwait(true);
            DiagnosticLogger.Write($"Unified Shell native filter no-op; Kind={kind}; Title={title}; Label={label}; Generation={generation}");
            return;
        }
        var page = await WaitForDoubanSourcePageAsync(requestId, generation, beforeSignature, "filter").ConfigureAwait(true);
        await ForwardDoubanSourceResultToShellAsync(page, "filter").ConfigureAwait(true);
    }

    private async Task HandleDoubanShellLoadMoreAsync(JsonElement root)
    {
        var requestId = ReadString(root, "requestId");
        var generation = Interlocked.Increment(ref _doubanSourceGeneration);
        PostShellMessage(new { type = "doubanShellOperationState", busy = true, operation = "load-more" });

        if (_frodoPersonalQuery.IsActive &&
            FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var localProfileId, out var localStatus) &&
            _frodoPersonalQuery.IsActiveFor(localProfileId, localStatus) &&
            _frodoPersonalIndex.TryGetStatus(_frodoPersonalQuery.ProfileId, _frodoPersonalQuery.Status, out var localSnapshot))
        {
            var localItems = _frodoPersonalQuery.TakeNext();
            var localPage = BuildFrodoPersonalQueryPayload(requestId, generation, localItems, localSnapshot);
            await ForwardDoubanSourceResultToShellAsync(localPage, "load-more").ConfigureAwait(true);
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
            DiagnosticLogger.Write($"Unified Shell Source load-more completed; Source=FrodoLocalIndex; RequestId={requestId}; Generation={generation}; Shown={_frodoPersonalQuery.Shown}; Total={_frodoPersonalQuery.Total}");
            return;
        }

        if (_frodoPersonalActive && _frodoPersonalProvider.IsActiveFor(_activeDoubanSourceNavigationUrl))
        {
            try
            {
                var page = await _frodoPersonalProvider.LoadMoreAsync(requestId, generation).ConfigureAwait(true);
                await ForwardDoubanSourceResultToShellAsync(page, "load-more").ConfigureAwait(true);
                PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
                DiagnosticLogger.Write($"Unified Shell Source load-more completed; Source=Frodo; RequestId={requestId}; Generation={generation}; Url={_activeDoubanSourceNavigationUrl}");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
            {
                var fallbackUrl = _frodoPersonalProvider.CurrentUrl;
                DiagnosticLogger.Write($"Unified Shell Source load-more failed; Source=Frodo; RequestId={requestId}; Generation={generation}; Url={fallbackUrl}; Fallback=DOM; Error={ex.Message}");
                NavigatePersonalDomFallback(fallbackUrl, requestId, "personal-api-fallback", "frodo-load-more-failed");
                return;
            }
        }

        var action = await ExecuteDoubanSourceBridgeAsync("loadMore").ConfigureAwait(true);
        if (!ReadBool(action, "ok"))
        {
            var error = ReadString(action, "error");
            DiagnosticLogger.Write($"Unified Shell Source load-more failed; RequestId={requestId}; Generation={generation}; Url={_activeDoubanSourceNavigationUrl}; Action={action}");
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
            PostShellMessage(new { type = "doubanShellLoadMoreError", requestId, error });
            return;
        }
        var beforeSignature = ReadString(action, "beforeSignature");
        if (ReadBool(action, "noOp"))
        {
            var currentPage = await ReadDoubanSourcePageAsync(requestId, generation).ConfigureAwait(true);
            await ForwardDoubanSourceResultToShellAsync(currentPage, "load-more-noop").ConfigureAwait(true);
            PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
            DiagnosticLogger.Write($"Unified Shell Source load-more no-op; RequestId={requestId}; Generation={generation}");
            return;
        }
        var pageDom = await WaitForDoubanSourcePageAsync(requestId, generation, beforeSignature, "load-more").ConfigureAwait(true);
        await ForwardDoubanSourceResultToShellAsync(pageDom, "load-more").ConfigureAwait(true);
        PostShellMessage(new { type = "doubanShellOperationState", busy = false, operation = "load-more" });
    }

    private async Task<JsonElement> PrepareSourcePosterItemsAsync(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return items;

        using var gate = new SemaphoreSlim(4, 4);
        using var totalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var prepared = await Task.WhenAll(items.EnumerateArray().Select(async item =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(item.GetRawText()) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            var sourceUrl = node["posterUrl"]?.GetValue<string>() ?? node["poster"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(sourceUrl)) return node;

            var acquired = false;
            try
            {
                await gate.WaitAsync(totalTimeout.Token).ConfigureAwait(true);
                acquired = true;
                var dataUri = await TryFetchDoubanPosterDataUriAsync(sourceUrl, totalTimeout.Token).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(dataUri)) node["posterUrl"] = dataUri;
            }
            catch (OperationCanceledException)
            {
                // Poster materialization is an enhancement. If it misses the
                // short budget, keep the original poster URL and return cards.
            }
            finally
            {
                if (acquired) gate.Release();
            }
            return node;
        })).ConfigureAwait(true);

        return JsonSerializer.SerializeToElement(prepared);
    }

    private async Task<string> TryFetchDoubanPosterDataUriAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        var posterUrl = ValidatePosterSourceUrl(sourceUrl);
        if (posterUrl.Length == 0) return "";

        lock (DoubanPosterCacheGate)
        {
            if (DoubanPosterDataUriCache.TryGetValue(posterUrl, out var cached)) return cached;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Get, posterUrl);
            request.Headers.Referrer = new Uri("https://movie.douban.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36");
            using var response = await DoubanPlusHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 4_000_000) return "";

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(true);
            if (bytes.Length == 0 || bytes.Length > 4_000_000) return "";
            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mediaType is not ("image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif")) return "";
            var dataUri = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
            if (dataUri.Length <= DoubanPosterCacheItemLimit)
            {
                lock (DoubanPosterCacheGate)
                {
                    if (DoubanPosterDataUriCache.Count >= DoubanPosterCacheLimit)
                        DoubanPosterDataUriCache.Remove(DoubanPosterDataUriCache.Keys.First());
                    DoubanPosterDataUriCache[posterUrl] = dataUri;
                }
            }
            return dataUri;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            DiagnosticLogger.Write($"Unified Shell poster materialization failed; Url={posterUrl}; Error={ex.Message}");
            return "";
        }
    }

}
