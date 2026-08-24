using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    internal async Task<string> RebuildFrodoPersonalCacheAsync(IProgress<FrodoPersonalIndexProgress>? progress = null)
    {
        if (_closing || IsDisposed)
            throw new InvalidOperationException("Douban Plus 已关闭。");

        var waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!_initialized && !_closing && System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted) < TimeSpan.FromSeconds(20))
            await Task.Delay(100).ConfigureAwait(true);
        if (!_initialized)
            throw new InvalidOperationException("Douban Plus 尚未初始化完成，请稍后重试。");

        await WaitForDoubanRecoveryAsync().ConfigureAwait(true);
        var session = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
        if (!session.IsLoggedIn ||
            string.IsNullOrWhiteSpace(session.ProfileId) ||
            !session.ProfileId.All(char.IsDigit))
            throw new InvalidOperationException("豆瓣尚未登录，请先完成扫码登录。");

        var profileId = session.ProfileId;
        await _frodoPersonalIndex.LoadCacheAsync(profileId).ConfigureAwait(true);
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var status in new[] { "collect", "wish", "do" })
        {
            var snapshot = await _frodoPersonalIndex.ForceFullReconcileAsync(profileId, status, progress).ConfigureAwait(true);
            totals[status] = snapshot.Items.Count;
        }

        if (FrodoPersonalProvider.TryReadScope(_activeDoubanSourceNavigationUrl, out var activeProfileId, out var activeStatus) &&
            activeProfileId.Equals(profileId, StringComparison.Ordinal) &&
            _frodoPersonalIndex.TryGetStatus(profileId, activeStatus, out var activeSnapshot))
        {
            PostFrodoPersonalFilterState(
                profileId,
                activeStatus,
                activeSnapshot,
                _frodoPersonalQuery.IsActiveFor(profileId, activeStatus)
                    ? _frodoPersonalQuery.Criteria
                    : new FrodoPersonalFilterCriteria(),
                false,
                activeSnapshot.Items.Count,
                activeSnapshot.Total,
                activeSnapshot.Items.Count,
                0,
                "");

            if (!_frodoPersonalActive && _doubanSourceNavigationCompleted)
            {
                _doubanSourceReadScheduledVersion = -1;
                await RequestDoubanSourceReadAsync("manual-personal-cache-rebuild").ConfigureAwait(true);
            }
        }

        DiagnosticLogger.Write($"Manual personal cache rebuild completed; Source=OpenDoubanPlus; ProfileId={profileId}; Collect={totals["collect"]}; Wish={totals["wish"]}; Do={totals["do"]}");
        return $"完成：看过 {totals["collect"]} / 想看 {totals["wish"]} / 在看 {totals["do"]}";
    }
    private async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            // Douban Plus is the only visible page. The former local MediaLibrary HTML shell is gone.
            await Task.WhenAll(
                _detailConnector.EnsureInitializedAsync(),
                _workerConnector.EnsureInitializedAsync(),
                EnsureDoubanPlusViewAsync(),
                EnsureDoubanSourceViewAsync(),
                EnsureDoubanSubjectViewAsync());
            _detailView.CoreWebView2.Navigate("about:blank");
            _workerView.CoreWebView2.Navigate("about:blank");
            DiagnosticLogger.Write($"Unified Shell stage 1 ready; DetailProfile={_environments.DoubanProfileDirectory}; WorkerProfile={_environments.DoubanProfileDirectory}; SourceProfile={_environments.DoubanProfileDirectory}; SharedEnvironment=True; DoubanPlus={DoubanPlusWebView2Script.Version}; Commit={DoubanPlusWebView2Script.SourceCommit}");
            var session = await _workerConnector.GetSessionStatusAsync().ConfigureAwait(true);
            _doubanAccountStatus.Text = session.Text;
            await NavigateInitialDoubanPageAsync(session).ConfigureAwait(true);
            _initialized = true;
        }
        catch (Exception ex)
        {
            var probe = WebView2EnvironmentProvider.ProbeRuntime();
            var message = "无法启动 Douban Plus。\n\n" + ex.Message + "\n\n请安装 Microsoft Edge WebView2 Evergreen Runtime。\n\n是否打开微软官方安装页面？";
            DiagnosticLogger.Write($"Douban Plus initialization failed; Runtime={probe.Version}; Error={ex}");
            if (MessageBox.Show(this, message, "缺少 WebView2 Runtime", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/consumer/") { UseShellExecute = true });
            Close();
        }
    }

    private async Task<object?> DispatchAsync(string operation, JsonElement payload, string bridgeRequestId)
    {
        if (operation is not ("init" or "sessionStatus" or "login" or "detailCached"))
            await WaitForDoubanRecoveryAsync().ConfigureAwait(true);

        switch (operation)
        {
            case "init": return await BuildInitialStateAsync();
            case "openDoubanPlusDetail": return await OpenDoubanPlusDetailAsync(payload, bridgeRequestId);
            case "openDoubanPersonalPage": return await OpenDoubanPersonalPageAsync(payload);
            case "sessionStatus":
                if (_doubanRecovering)
                    return SessionStatusDto(new DoubanSessionStatus("recovering", "豆瓣：浏览器正在恢复", _workerConnector.CurrentSessionStatus.ProfileId, _workerConnector.CurrentSessionStatus.VerifiedAt, "WebView2 BrowserProcessExited"));
                return SessionStatusDto(await _workerConnector.GetSessionStatusAsync(skipWhenBusy: true));
            case "login":
            {
                ShowDoubanLogin();
                return new { opened = true };
            }
            case "search":
            {
                var query = RequiredString(payload, "query", 120);
                var page = ReadInt(payload, "page", 0, 200);
                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.Search);
                var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.Search, WorkerJobQueue.PriorityFor(WorkerJobType.Search), "", bridgeRequestId,
                    $"search:{page}", DateTimeOffset.UtcNow);
                var result = await _workerQueue.EnqueueAsync(descriptor,
                    _ => _connector.SearchAsync(query, DoubanSearchPaging.StartForPage(page)));
                return new { loggedIn = result.LoggedIn, captcha = result.Captcha, hasMore = result.HasMore, error = result.Error, page, items = result.Items.Select(SearchCandidateDto).ToList(), jobId };
            }
            case "detailCached":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                _activeDetailSubjectId = subjectId;
                _activeDetailRequestId = requestId;
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                DiagnosticLogger.Write($"HTML detail cache returned; SubjectId={subjectId}; RequestId={requestId}; HasMetadata={record.DetailMetadataFetched}; Status={record.Status}; Rating={record.Rating?.ToString() ?? "null"}");
                return DetailDto(record, "cache");
            }
            case "detailMetadata":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                var now = DateTime.Now;
                if (!DetailCachePolicy.NeedsMetadataRefresh(record, now))
                {
                    DiagnosticLogger.Write($"HTML detail metadata cache hit; WebView=Detail; SubjectId={subjectId}; RequestId={requestId}; BasicFetchedAt={record.FullDetailsFetchedAt:O}");
                    return DetailDto(record, "cache-fresh");
                }
                var startedAt = DateTime.UtcNow;
                var metadata = await _detailConnector.ReadMetadataAsync(subjectUrl, probeStatusCapabilities: false);
                if (metadata.Captcha) throw new InvalidOperationException("豆瓣要求验证码，已保留本地数据。");
                if (!metadata.LoggedIn) throw new InvalidOperationException("内置豆瓣 Profile 尚未登录，请先扫码登录。");
                if (!metadata.IsSuccess) throw new InvalidDataException(string.IsNullOrWhiteSpace(metadata.Error) ? "豆瓣没有返回有效详情。" : metadata.Error);
                ApplyMetadata(record, metadata);
                DiagnosticLogger.Write($"HTML detail metadata refreshed; WebView=Detail; SubjectId={subjectId}; RequestId={requestId}; ElapsedMs={(DateTime.UtcNow - startedAt).TotalMilliseconds:F0}; Source={metadata.ConnectorSource}");
                return DetailDto(record, metadata.ConnectorSource);
            }
            case "detailReview":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                var startedAt = DateTime.UtcNow;
                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.OfficialReviewRead);
                var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.OfficialReviewRead, WorkerJobQueue.PriorityFor(WorkerJobType.OfficialReviewRead), subjectId, requestId,
                    subjectUrl, DateTimeOffset.UtcNow);
                var officialReview = await _workerQueue.EnqueueAsync(descriptor,
                    _ => _connector.ReadOfficialReviewAsync(subjectUrl));
                var officialLoaded = ApplyAuthoritativeReview(record, officialReview);
                if (!officialLoaded)
                {
                    DiagnosticLogger.Write($"HTML detail official review unavailable; WebView=Worker; JobId={jobId}; SubjectId={subjectId}; RequestId={requestId}; Source={officialReview.Source}; Error={officialReview.Error}");
                }
                DiagnosticLogger.Write($"HTML detail review refreshed; WebView=Worker; JobId={jobId}; SubjectId={subjectId}; RequestId={requestId}; ElapsedMs={(DateTime.UtcNow - startedAt).TotalMilliseconds:F0}; OfficialLoaded={officialLoaded}; Source={officialReview.Source}; Error={officialReview.Error ?? "<none>"}");
                return new
                {
                    detail = DetailDto(record, officialLoaded ? "official-review" : "cache"),
                    officialLoaded,
                    error = officialLoaded ? "" : (officialReview.Error ?? "豆瓣官方评价未读取完整。"),
                    jobId,
                    requestId
                };
            }
            case "detail":
            {
                // Compatibility path for older local assets. New assets use cache-first staged requests.
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                var metadata = await _detailConnector.ReadMetadataAsync(subjectUrl, probeStatusCapabilities: false);
                if (metadata.Captcha) throw new InvalidOperationException("豆瓣要求验证码，已保留本地数据。");
                if (!metadata.LoggedIn) throw new InvalidOperationException("内置豆瓣 Profile 尚未登录，请先扫码登录。");
                if (!metadata.IsSuccess) throw new InvalidDataException(string.IsNullOrWhiteSpace(metadata.Error) ? "豆瓣没有返回有效详情。" : metadata.Error);
                ApplyMetadata(record, metadata);
                var officialReview = await _connector.ReadOfficialReviewAsync(subjectUrl);
                ApplyAuthoritativeReview(record, officialReview);
                return DetailDto(record, metadata.ConnectorSource + "+official-review");
            }
            case "saveDoubanEntry":
            {
                var record = GetWritableRecord(payload, allowTombstoneRecreate: true);
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var uiTitle = ReadString(payload, "uiTitle").Trim();
                var beforeStatus = record.Status;
                var beforeRating = record.Rating;
                var beforeMarkedDate = record.MarkedDate;
                var cachedTitle = record.Title;
                var saveTimer = System.Diagnostics.Stopwatch.StartNew();
                var status = RequiredLibraryStatus(payload, "status");
                var ratingAction = ReadReviewFieldAction(payload, "ratingAction");
                var rating = ReadNullableInt(payload, "rating", 1, 5);
                var commentAction = ReadReviewFieldAction(payload, "commentAction");
                var comment = commentAction == ReviewFieldAction.Set ? ReadString(payload, "comment").Trim() : null;
                if (commentAction == ReviewFieldAction.Set && (comment?.Length ?? 0) > 330)
                    throw new InvalidDataException("短评不能超过 330 字。");

                var request = new DoubanEntryWriteRequestV2(status, ratingAction, rating, commentAction, comment);
                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.ReviewSave);
                var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.ReviewSave, WorkerJobQueue.PriorityFor(WorkerJobType.ReviewSave), record.SubjectId, requestId,
                    record.SubjectUrl, DateTimeOffset.UtcNow, NonCancelableOnceStarted: true);
                var connectorResult = await _workerQueue.EnqueueAsync(descriptor,
                    _ => _connector.SaveDoubanEntryAsync(record.SubjectUrl, request));
                var authoritative = SelectAuthoritativeSnapshot(connectorResult);
                var localUpdated = authoritative is not null && ApplyAuthoritativeReview(record, authoritative);
                var result = connectorResult with { LocalUpdated = connectorResult.LocalUpdated || localUpdated };
                if (result.OfficialConfirmed && authoritative is not null)
                    await SyncFrodoPersonalAfterConfirmedWriteAsync(record, beforeStatus, "save").ConfigureAwait(true);
                saveTimer.Stop();
                var subjectUrlSubjectId = DoubanSubjectIdentity.ExtractSubjectId(record.SubjectUrl);
                ReviewTransactionLogger.Write(new
                {
                    UiTitle = uiTitle, CachedTitle = cachedTitle, OfficialTitle = result.Official?.OfficialTitle ?? result.Before?.OfficialTitle ?? "",
                    SubjectId = record.SubjectId, SubjectUrlSubjectId = subjectUrlSubjectId,
                    BeforeStatus = beforeStatus, BeforeRating = beforeRating, BeforeMarkedDate = beforeMarkedDate,
                    TargetStatus = result.Target?.Status ?? status, TargetRating = result.Target?.Rating,
                    result.Submitted, result.NoChange, result.Changed, result.SubmitEventObserved,
                    OfficialStatus = result.Official?.Status ?? result.Before?.Status, OfficialRating = result.Official?.Rating ?? result.Before?.Rating,
                    OfficialMarkedDate = result.Official?.MarkedDate ?? result.Before?.MarkedDate, result.OfficialConfirmed, result.LocalUpdated,
                    DurationMs = Math.Round(saveTimer.Elapsed.TotalMilliseconds), result.Phase, result.Stage, Error = result.Error ?? "",
                    WebView = "Worker", JobId = jobId, RequestId = requestId
                });
                DiagnosticLogger.Write(
                    $"HTML review v2 final result; WebView=Worker; JobId={jobId}; RequestId={requestId}; UiTitle={uiTitle}; CachedTitle={cachedTitle}; OfficialTitle={result.Official?.OfficialTitle ?? result.Before?.OfficialTitle ?? ""}; SubjectId={record.SubjectId}; SubjectUrlSubjectId={subjectUrlSubjectId}; " +
                    $"BeforeStatus={beforeStatus}; BeforeRating={beforeRating?.ToString() ?? "null"}; BeforeMarkedDate={beforeMarkedDate}; TargetStatus={result.Target?.Status ?? status}; TargetRating={result.Target?.Rating?.ToString() ?? "null"}; " +
                    $"Submitted={result.Submitted}; NoChange={result.NoChange}; Changed={result.Changed}; SubmitEventObserved={result.SubmitEventObserved}; OfficialStatus={result.Official?.Status ?? result.Before?.Status}; " +
                    $"OfficialRating={(result.Official?.Rating ?? result.Before?.Rating)?.ToString() ?? "null"}; OfficialMarkedDate={result.Official?.MarkedDate ?? result.Before?.MarkedDate}; " +
                    $"Settled={result.Settled}; OfficialConfirmed={result.OfficialConfirmed}; LocalUpdated={result.LocalUpdated}; DurationMs={saveTimer.Elapsed.TotalMilliseconds:F0}; " +
                    $"Error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}");
                return WriteEnvelopeV2(record, result, "webview2-worker-write-v2", jobId, requestId);
            }
            case "deleteEntry":
            {
                var subjectId = RequiredDigits(payload, "subjectId");
                var subjectUrl = RequiredSubjectUrl(payload, "subjectUrl");
                var requestId = ReadDetailRequestId(payload, bridgeRequestId);
                var uiTitle = ReadString(payload, "uiTitle").Trim();
                var record = FindOrCreateRecord(subjectId, subjectUrl);
                DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML delete preflight", record);
                record.SubjectUrl = subjectUrl;
                var cachedTitle = record.Title;
                var beforeStatus = record.Status;
                var beforeRating = record.Rating;
                var beforeMarkedDate = record.MarkedDate;
                var timer = System.Diagnostics.Stopwatch.StartNew();

                var jobId = WorkerJobQueue.NewJobId(WorkerJobType.ReviewDelete);
                var descriptor = new WorkerJobDescriptor(
                    jobId,
                    WorkerJobType.ReviewDelete,
                    WorkerJobQueue.PriorityFor(WorkerJobType.ReviewDelete),
                    subjectId,
                    requestId,
                    subjectUrl,
                    DateTimeOffset.UtcNow,
                    NonCancelableOnceStarted: true);

                var connectorResult = await _workerQueue.EnqueueAsync(
                    descriptor,
                    _ => _connector.DeleteDoubanEntryAsync(subjectUrl));

                var localUpdated = false;
                if (connectorResult.OfficialConfirmed && connectorResult.Official is { ExistsKnown: true, Exists: false })
                {
                    localUpdated = ApplyConfirmedDeletion(record,
                        connectorResult.NoChange ? "豆瓣官方已无评价，删除状态同步" : "豆瓣官方删除确认");
                }
                var result = connectorResult with { LocalUpdated = connectorResult.LocalUpdated || localUpdated };
                if (result.OfficialConfirmed && result.Official is { ExistsKnown: true, Exists: false })
                    await SyncFrodoPersonalAfterConfirmedDeleteAsync(subjectId, beforeStatus, "delete").ConfigureAwait(true);
                timer.Stop();

                ReviewTransactionLogger.Write(new
                {
                    UiTitle = uiTitle,
                    CachedTitle = cachedTitle,
                    OfficialTitle = result.Official?.OfficialTitle ?? result.Before?.OfficialTitle ?? "",
                    SubjectId = subjectId,
                    SubjectUrlSubjectId = DoubanSubjectIdentity.ExtractSubjectId(subjectUrl),
                    BeforeStatus = result.Before?.Status ?? beforeStatus,
                    BeforeRating = result.Before?.Rating ?? beforeRating,
                    BeforeMarkedDate = result.Before?.MarkedDate ?? beforeMarkedDate,
                    TargetStatus = "delete",
                    TargetRating = (int?)null,
                    Submitted = result.Submitted,
                    NoChange = result.NoChange,
                    Changed = result.Submitted,
                    SubmitEventObserved = result.Submitted,
                    OfficialStatus = result.Official?.ExistsKnown == true && !result.Official.Exists ? "deleted" : (result.Official?.Status ?? ""),
                    OfficialRating = result.Official?.Rating,
                    OfficialMarkedDate = result.Official?.MarkedDate ?? "",
                    OfficialConfirmed = result.OfficialConfirmed,
                    LocalUpdated = result.LocalUpdated,
                    DurationMs = Math.Round(timer.Elapsed.TotalMilliseconds),
                    Phase = result.Phase,
                    Stage = result.Stage,
                    DeleteRoute = result.Route,
                    Error = result.Error ?? "",
                    WebView = "Worker",
                    JobId = jobId,
                    RequestId = requestId,
                    ListChecks = result.ListChecks
                });

                DiagnosticLogger.Write(
                    $"HTML delete v2 final result; WebView=Worker; JobId={jobId}; RequestId={requestId}; UiTitle={uiTitle}; CachedTitle={cachedTitle}; SubjectId={subjectId}; " +
                    $"BeforeStatus={result.Before?.Status ?? beforeStatus}; DeleteRoute={result.Route}; Submitted={result.Submitted}; NoChange={result.NoChange}; Settled={result.Settled}; " +
                    $"OfficialExists={(result.Official?.ExistsKnown == true ? result.Official.Exists.ToString() : "unknown")}; OfficialConfirmed={result.OfficialConfirmed}; " +
                    $"LocalUpdated={result.LocalUpdated}; DurationMs={timer.Elapsed.TotalMilliseconds:F0}; Error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}");

                return DeleteEnvelope(record, result, "webview2-worker-delete-v2", jobId, requestId);
            }
            case "ptSearch":
            {
                var imdbId = RequiredString(payload, "imdbId", 20);
                if (!BrowserCdpService.IsValidImdbId(imdbId)) throw new InvalidDataException("IMDb编号无效。");
                await _cdp.EnsureBackgroundAsync(_preferredBrowser);
                await _cdp.OpenPtDepilerSearchAsync(imdbId);
                return new { opened = true };
            }
            default: throw new InvalidDataException("不允许的操作。");
        }
    }

    private async Task<object> BuildInitialStateAsync()
    {
        var session = await _workerConnector.GetSessionStatusAsync();
        object? openDetail = null;
        var openDetailError = "";
        if (_initialOpenTarget is not null)
        {
            try { openDetail = await ResolveInitialOpenDetailAsync(_initialOpenTarget); }
            catch (Exception ex) { openDetailError = ex.Message; DiagnosticLogger.Write($"HTML initial target resolution failed; SearchTitle={_initialOpenTarget.SearchTitle}; Error={ex.Message}"); }
        }
        return new
        {
            version = AppInfo.Version,
            runtime = WebView2EnvironmentProvider.ProbeRuntime(),
            login = SessionStatusDto(session),
            profileDirectory = _environments.DoubanProfileDirectory,
            tabs = new
            {
                collect = _history.Items.Values.Where(x => !x.Tombstoned && x.Status == "collect").OrderByDescending(x => x.MarkedDate).Select(CardDto).ToList(),
                wish = _history.Items.Values.Where(x => !x.Tombstoned && x.Status == "wish").OrderByDescending(x => x.MarkedDate).Select(CardDto).ToList(),
                watching = _history.Items.Values.Where(x => !x.Tombstoned && x.Status == "do").OrderByDescending(x => x.MarkedDate).Select(CardDto).ToList(),
                software = _software.Videos.Values.OrderByDescending(x => x.WatchedSeconds).Select(x => new { key = x.Key, title = x.Title, subtitle = "软件真实观看记录", meta = $"真实观看 {TimeSpan.FromSeconds(x.WatchedSeconds):hh\\:mm\\:ss} · 最高进度 {x.HighestRatio:P0}", posterUrl = "", subjectId = "", subjectUrl = "" }).ToList()
            },
            openDetail,
            openDetailError
        };
    }

    private async Task NavigateInitialDoubanPageAsync(DoubanSessionStatus session)
    {
        if (_initialOpenTarget is not null)
        {
            try
            {
                var resolved = await ResolveInitialOpenDetailAsync(_initialOpenTarget).ConfigureAwait(true);
                var item = JsonSerializer.SerializeToElement(resolved).GetProperty("item");
                var subjectId = item.GetProperty("subjectId").GetString() ?? "";
                var subjectUrl = item.GetProperty("subjectUrl").GetString() ?? "";
                var payload = JsonSerializer.SerializeToElement(new { subjectId, subjectUrl });
                await OpenDoubanPlusDetailAsync(payload, "initial-target").ConfigureAwait(true);
                return;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Write($"Douban Plus initial target resolution failed; SearchTitle={_initialOpenTarget.SearchTitle}; Error={ex.Message}");
            }
        }

        _frodoPersonalActive = false;
        _frodoPersonalProvider.Reset();
        const string url = "https://movie.douban.com/explore";
        _activeDoubanPlusNavigationUrl = url;
        _activeDoubanSourceNavigationUrl = url;
        _doubanSourceNavigationCompleted = false;
        _activeDoubanPersonalPageUrl = "";
        _doubanPlusView.Visible = true;
        _doubanPlusView.BringToFront();
        _doubanAccountBar.Visible = false;
        _returnToLibraryButton.Visible = false;
        _doubanSourceView.CoreWebView2!.Navigate(url);
        DiagnosticLogger.Write($"Unified Shell initial page opened; Shell=visible; Source=hidden; Url={url}; LoggedIn={session.IsLoggedIn}; SourceGeneration={_doubanSourceGeneration}");
    }

    private async Task<object> ResolveInitialOpenDetailAsync(DoubanPlusOpenTarget target)
    {
        var aliases = target.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)).Select(alias => alias.Trim())
            .Append(target.SearchTitle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        var candidates = new Dictionary<string, DoubanSearchCandidate>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            var jobId = WorkerJobQueue.NewJobId(WorkerJobType.Search);
            var descriptor = new WorkerJobDescriptor(jobId, WorkerJobType.Search, WorkerJobQueue.PriorityFor(WorkerJobType.Search), "", "", $"initial-ai:{alias}", DateTimeOffset.UtcNow);
            var result = await _workerQueue.EnqueueAsync(descriptor, _ => _connector.SearchAsync(alias, 0));
            if (result.Captcha) throw new InvalidOperationException("豆瓣要求验证，请完成验证后重试。");
            if (!result.LoggedIn) throw new InvalidOperationException("豆瓣尚未登录，请先扫码登录。");
            foreach (var item in result.Items)
                if (!candidates.ContainsKey(item.SubjectId)) candidates[item.SubjectId] = item;
        }

        var input = new MovieTitleParts(target.SearchTitle, target.SearchTitle, "", "", target.Year, aliases);
        var ranked = candidates.Values.Select(item =>
        {
            var years = MovieTitle.YearsFromText(item.VisibleText);
            int? candidateYear = years.Count == 0 ? null : years[0];
            var evaluation = RecognitionMatcher.Evaluate(input, item.VisibleText, null, candidateYear);
            return new { item, evaluation };
        }).Where(candidate => candidate.evaluation.IsStrongMatch)
            .OrderByDescending(candidate => candidate.evaluation.Score)
            .ThenBy(candidate => candidate.item.SubjectId, StringComparer.Ordinal)
            .ToList();
        if (ranked.Count == 0) throw new InvalidOperationException("未找到与 PotPlayer 影片可靠匹配的豆瓣条目，请先在豆瓣页面搜索并确认。");
        if (ranked.Count > 1 && ranked[0].evaluation.Score - ranked[1].evaluation.Score < 10)
            throw new InvalidOperationException("豆瓣搜索返回多个相近影片，已阻止自动选择，请先在豆瓣页面搜索并确认。");

        var selected = ranked[0].item;
        return new
        {
            item = new
            {
                subjectId = selected.SubjectId,
                subjectUrl = selected.SubjectUrl,
                posterUrl = selected.PosterUrl,
                title = selected.VisibleText.Split('\n')[0].Trim(),
                statusOptions = (selected.StatusOptions ?? []).Select(StatusDto).ToList()
            },
            preferredStatus = target.PreferredStatus,
            source = target.SourceDescription
        };
    }

}
