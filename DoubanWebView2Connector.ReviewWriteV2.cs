using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace QbPotDoubanAi;

internal sealed partial class DoubanWebView2Connector : IDoubanOfficialReviewGateway
{
    private bool _reviewWriteInProgress;
    private bool _reviewNavigationStarted;
    private bool _reviewNavigationCompleted;
    private string _reviewNavigationUrl = "";
    private string _reviewNavigationError = "";
    private static readonly TimeSpan FreshOfficialReviewTtl = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, FreshOfficialReviewEntry> _freshOfficialReviews = new(StringComparer.Ordinal);

    private static readonly IOfficialReviewCacheWriter DeferredReviewCacheWriter = new DeferredCacheWriter();

    public async Task<OfficialReviewSnapshot> ReadOfficialReviewAsync(string subjectUrl)
    {
        if (!IsAllowedSubjectUrl(subjectUrl))
            return OfficialReviewSnapshot.Unknown("snapshot", "豆瓣影片地址无效。");
        if (_loginWindowActive)
            return OfficialReviewSnapshot.Unknown("snapshot", "豆瓣登录窗口正在使用，请关闭后再读取。");

        await _navigationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            var subjectId = ExtractSubjectId(subjectUrl);
            var result = await ((IDoubanOfficialReviewGateway)this)
                .ReadOfficialAsync(subjectId, subjectUrl, CancellationToken.None)
                .ConfigureAwait(true);
            RememberFreshOfficialReview(subjectId, result, "detail-review");
            DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 manual-read; SubjectId={subjectId}; Snapshot={SerializeDiagnostic(result)}");
            return result;
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async Task<ReviewWriteResultV2> SaveDoubanEntryAsync(
        string subjectUrl,
        DoubanEntryWriteRequestV2 request)
    {
        if (!IsAllowedSubjectUrl(subjectUrl))
            return ReviewWriteResultV2.Blocked("snapshot", request, null, "豆瓣影片地址无效。");
        if (_loginWindowActive)
            return ReviewWriteResultV2.Blocked("snapshot", request, null, "豆瓣登录窗口正在使用，请关闭后再保存。");

        await _navigationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            var subjectId = ExtractSubjectId(subjectUrl);
            var freshBefore = TryConsumeFreshOfficialReview(subjectId, subjectUrl);
            var coordinator = new ReviewWriteCoordinator(this, DeferredReviewCacheWriter);
            var result = await coordinator.SaveAsync(
                    subjectId,
                    subjectUrl,
                    request,
                    CancellationToken.None,
                    freshBefore)
                .ConfigureAwait(true);
            if (result.Official is not null) RememberFreshOfficialReview(subjectId, result.Official, "write-result");
            else if (result.Before is not null) RememberFreshOfficialReview(subjectId, result.Before, "write-before");
            // This connector deliberately defers the live-cache write to HtmlMediaLibraryForm.
            // Do not report the deferred connector-local flag as a final user-visible failure.
            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Douban review v2 connector result; SubjectId={subjectId}; Phase={result.Phase}; Stage={result.Stage}; " +
                $"Settled={result.Settled}; OfficialConfirmed={result.OfficialConfirmed}; ConnectorCacheUpdate=Deferred; " +
                $"Error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}; " +
                $"Before={SerializeDiagnostic(result.Before)}; Official={SerializeDiagnostic(result.Official)}");
            return result;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 failed before result mapping; URL={subjectUrl}; Error={ex}");
            return new ReviewWriteResultV2(
                ReviewWritePhase.Failed,
                "exception",
                false,
                false,
                request,
                null,
                null,
                null,
                ex.Message,
                ex.ToString());
        }
        finally
        {
            _reviewWriteInProgress = false;
            _navigationGate.Release();
        }
    }

    async Task<OfficialReviewSnapshot> IDoubanOfficialReviewGateway.ReadOfficialAsync(
        string subjectId,
        string subjectUrl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fixedProfileId = await EnsureFixedProfileCoreAsync("review-v2-read").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(fixedProfileId))
            return OfficialReviewSnapshot.Unknown("official-form", "无法确认当前豆瓣用户 Profile。");

        var opened = false;
        try
        {
            await EnsureReviewSubjectPageAsync(subjectUrl, subjectId, "official-read").ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await EvaluateAsync(
                DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)))
                .ConfigureAwait(true);

            var validationError = ValidateOfficialDetailSnapshot(detail, subjectId, fixedProfileId);
            if (!string.IsNullOrWhiteSpace(validationError))
                return OfficialReviewSnapshot.Unknown("detail-snapshot", validationError);

            var detailStatus = ReviewTargetResolver.NormalizeStatus(String(detail, "detectedStatus"));
            var detailStatusKnown = Boolean(detail, "statusKnown");
            var detailExistsKnown = detailStatusKnown;
            var detailExists = detailStatus is "wish" or "do" or "collect";

            var open = await EvaluateAsync(
                DoubanWriteOpenCapabilityScript.Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId)))
                .ConfigureAwait(true);
            opened = Boolean(open, "clicked");
            if (!opened)
            {
                // A complete, explicit "none" snapshot is still authoritative even if no edit form can be opened.
                if (detailExistsKnown && !detailExists)
                {
                    return new OfficialReviewSnapshot(
                        ExistsKnown: true,
                        Exists: false,
                        StatusKnown: true,
                        Status: null,
                        RatingKnown: true,
                        Rating: null,
                        CommentKnown: true,
                        Comment: string.Empty,
                        CapabilitiesKnown: false,
                        SupportedStatuses: Array.Empty<string>(),
                        CanSetRating: false,
                        CanClearRating: true,
                        CanEditComment: false,
                        Source: "detail-snapshot",
                        Error: null)
                    {
                        MarkedDateKnown = Boolean(detail, "markedDateKnown"),
                        MarkedDate = String(detail, "markedDate"),
                        OfficialTitle = String(detail, "officialTitle"),
                        OfficialSubjectId = String(detail, "subjectId")
                    };
                }
                return OfficialReviewSnapshot.Unknown("official-form", String(open, "error", "官方编辑表单入口缺失。"));
            }

            var wait = await WaitForOfficialFormAsync(subjectId, requireSelectedInterest: detailExists).ConfigureAwait(true);
            var form = wait.Form;
            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Douban review v2 official-read; SubjectId={subjectId}; Stable={wait.Stable}; Attempts={wait.Attempts}; " +
                $"Detail={detail}; Open={open}; Form={form}");

            if (Boolean(form, "captcha"))
                return OfficialReviewSnapshot.Unknown("official-form", "豆瓣要求人工验证，请完成验证后重试。");
            if (Boolean(form, "loginPage"))
                return OfficialReviewSnapshot.Unknown("official-form", "豆瓣登录已失效。");
            if (!Boolean(form, "ready") || !wait.Stable)
                return OfficialReviewSnapshot.Unknown("official-form", wait.Stable
                    ? String(form, "error", "官方编辑表单没有出现。")
                    : "官方编辑表单字段在等待时间内未稳定。");

            var formStatus = ReviewTargetResolver.NormalizeStatus(String(form, "selectedInterest"));
            var openIndicatesExisting = Boolean(open, "generic") || Boolean(open, "selected");

            // A visible target-specific create form is not proof that no review exists.
            // Absence is authoritative only when the detail snapshot explicitly recognized "none".
            var formIndicatesExisting = openIndicatesExisting && formStatus is ("wish" or "do" or "collect");
            var existsKnown = detailExistsKnown || formIndicatesExisting;
            var exists = detailExistsKnown ? detailExists : formIndicatesExisting;

            var statusKnown = existsKnown;
            string? status = exists ? (formStatus is ("wish" or "do" or "collect") ? formStatus : detailStatus) : null;
            if (exists && status is not ("wish" or "do" or "collect"))
                statusKnown = false;

            var options = ReadOfficialFormStatusOptions(form);
            var supported = options
                .Select(option => option.Text switch { "想看" => "wish", "在看" => "do", "看过" => "collect", _ => "" })
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var capabilitiesKnown = supported.Length >= 2 || Boolean(open, "generic") || Boolean(open, "selected");

            int? rating = null;
            var ratingKnown = Boolean(form, "ratingKnown");
            if (ratingKnown && form.TryGetProperty("rating", out var ratingValue) &&
                ratingValue.ValueKind == JsonValueKind.Number && ratingValue.TryGetInt32(out var ratingNumber))
                rating = ratingNumber is >= 1 and <= 5 ? ratingNumber : null;

            var commentKnown = Boolean(form, "commentKnown");
            var comment = commentKnown ? ReviewTargetResolver.NormalizeComment(String(form, "comment")) : null;

            if (!exists)
            {
                // Opening a target-specific create form must not turn its defaults into an existing review.
                ratingKnown = true;
                rating = null;
                commentKnown = true;
                comment = string.Empty;
            }

            var canSetRating = (Int(form, "ratingControlCount") ?? 0) > 0;
            var canClearRating = rating is null || (Int(form, "clearRatingControlCount") ?? 0) == 1 || Boolean(form, "clearRatingViaWishSupported");
            var canEditComment = (Int(form, "commentControlCount") ?? 0) > 0;

            if (!existsKnown || !statusKnown || !ratingKnown || !commentKnown)
                return OfficialReviewSnapshot.Unknown("official-form", "豆瓣官方当前评价字段未读取完整。");

            return new OfficialReviewSnapshot(
                existsKnown,
                exists,
                statusKnown,
                status,
                ratingKnown,
                rating,
                commentKnown,
                comment,
                capabilitiesKnown,
                supported,
                canSetRating,
                canClearRating,
                canEditComment,
                "official-form",
                null)
            {
                MarkedDateKnown = Boolean(detail, "markedDateKnown"),
                MarkedDate = String(detail, "markedDate"),
                OfficialTitle = String(detail, "officialTitle"),
                OfficialSubjectId = String(detail, "subjectId")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 official-read failed; SubjectId={subjectId}; Error={ex}");
            return OfficialReviewSnapshot.Unknown("official-form", ex.Message);
        }
        finally
        {
            if (opened)
            {
                try { await NavigateAsync(subjectUrl).ConfigureAwait(true); }
                catch (Exception ex) { DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 restore subject failed; SubjectId={subjectId}; Error={ex.Message}"); }
            }
        }
    }

    async Task<ReviewSubmitReceipt> IDoubanOfficialReviewGateway.SubmitTargetAsync(
        string subjectId,
        string subjectUrl,
        ResolvedReviewTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fixedProfileId = await EnsureFixedProfileCoreAsync("review-v2-submit").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(fixedProfileId))
            return FailedReceipt("无法确认当前豆瓣用户 Profile。");

        try
        {
            await EnsureReviewSubjectPageAsync(subjectUrl, subjectId, "submit").ConfigureAwait(true);
            var detail = await EvaluateAsync(
                DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)))
                .ConfigureAwait(true);
            var validationError = ValidateOfficialDetailSnapshot(detail, subjectId, fixedProfileId);
            if (!string.IsNullOrWhiteSpace(validationError))
                return FailedReceipt(validationError, detail);

            var open = await EvaluateAsync(DoubanWriteOpenScriptV2
                    .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                    .Replace("__STATUS__", JsonSerializer.Serialize(target.Status)))
                .ConfigureAwait(true);
            if (!Boolean(open, "clicked"))
                return FailedReceipt(String(open, "error", "官方表单不提供请求状态。"), open);

            var wait = await WaitForOfficialFormAsync(subjectId, requireSelectedInterest: true).ConfigureAwait(true);
            var form = wait.Form;
            if (!wait.Stable || !Boolean(form, "ready"))
                return FailedReceipt(wait.Stable
                    ? String(form, "error", "官方编辑表单没有出现。")
                    : "官方编辑表单字段在等待时间内未稳定。", new { open, form, wait.Attempts, wait.StableSamples });
            if (!FormOffersStatus(form, target.Status))
                return FailedReceipt("官方表单不提供请求状态。", form);
            if (target.Rating is not null && (Int(form, "ratingControlCount") ?? 0) <= 0)
                return FailedReceipt("官方评分控件缺失。", form);
            if ((Int(form, "commentControlCount") ?? 0) <= 0)
                return FailedReceipt("官方短评控件缺失。", form);

            _reviewNavigationStarted = false;
            _reviewNavigationCompleted = false;
            _reviewNavigationUrl = "";
            _reviewNavigationError = "";
            _reviewWriteInProgress = true;

            JsonElement submit;
            try
            {
                submit = await EvaluateAsync(DoubanOfficialFormScripts.BuildSubmitScript(subjectId, target))
                    .ConfigureAwait(true);
            }
            catch (Exception ex) when (_reviewNavigationStarted)
            {
                // ExecuteScript can be interrupted by the navigation it initiated. Navigation is only
                // a process signal; final success still requires settlement and authoritative readback.
                DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 submit script interrupted by navigation; SubjectId={subjectId}; Error={ex.Message}");
                return new ReviewSubmitReceipt(
                    SubmitEventObserved: true,
                    SubmitDefaultPrevented: false,
                    InitialUrl: subjectUrl,
                    FormAction: null,
                    Error: null,
                    Diagnostic: new { inferredFromNavigation = true, navigationUrl = _reviewNavigationUrl, exception = ex.Message });
            }

            var observed = Boolean(submit, "submitEventObserved");
            var submitted = Boolean(submit, "submitted");
            var error = String(submit, "error");
            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Douban review v2 submit-event; SubjectId={subjectId}; Submitted={submitted}; Observed={observed}; " +
                $"DefaultPrevented={Boolean(submit, "submitDefaultPrevented")}; Submit={submit}");

            if (!submitted || !observed)
            {
                _reviewWriteInProgress = false;
                return FailedReceipt(string.IsNullOrWhiteSpace(error) ? "官方表单未观察到 submit 事件。" : error, submit);
            }

            return new ReviewSubmitReceipt(
                observed,
                Boolean(submit, "submitDefaultPrevented"),
                String(submit, "initialUrl", subjectUrl),
                String(submit, "formAction"),
                null,
                submit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _reviewWriteInProgress = false;
            throw;
        }
        catch (Exception ex)
        {
            _reviewWriteInProgress = false;
            DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 submit failed; SubjectId={subjectId}; Error={ex}");
            return FailedReceipt(ex.Message, ex.ToString());
        }
    }

    async Task<ReviewSettlementResult> IDoubanOfficialReviewGateway.WaitForSettlementAsync(
        string subjectId,
        string subjectUrl,
        ResolvedReviewTarget target,
        ReviewSubmitReceipt receipt,
        CancellationToken cancellationToken)
    {
        var policy = new ReviewSettlementPolicy();
        ReviewSettlementProbe? lastProbe = null;
        try
        {
            for (var attempt = 1; attempt <= 48; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delayMs = attempt <= 4 ? 120 : 225;
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(true);
                try
                {
                    var raw = await EvaluateAsync(DoubanWritePostSubmitProbeScript).ConfigureAwait(true);
                    OfficialReviewSnapshot? inline = null;
                    if (Boolean(raw, "subjectPage") &&
                        !Boolean(raw, "formOpen") &&
                        _reviewNavigationCompleted &&
                        string.Equals(String(raw, "readyState"), "complete", StringComparison.OrdinalIgnoreCase))
                    {
                        var fixedProfileId = _session.ProfileId;
                        if (!string.IsNullOrWhiteSpace(fixedProfileId))
                        {
                            try
                            {
                                var detail = await EvaluateAsync(
                                    DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(fixedProfileId)))
                                    .ConfigureAwait(true);
                                inline = MapInlineDetailSnapshot(detail);
                            }
                            catch (Exception inlineError)
                            {
                                DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 inline readback unavailable; Attempt={attempt}; Error={inlineError.Message}");
                            }
                        }
                    }

                    lastProbe = new ReviewSettlementProbe(
                        String(raw, "href", _worker.Source?.ToString() ?? subjectUrl),
                        String(raw, "readyState"),
                        _reviewNavigationStarted,
                        _reviewNavigationCompleted,
                        Boolean(raw, "formOpen"),
                        Boolean(raw, "captcha"),
                        Boolean(raw, "loginPage"),
                        inline,
                        new { raw, navigationUrl = _reviewNavigationUrl, navigationError = _reviewNavigationError });

                    var decision = policy.Observe(lastProbe, target);
                    DiagnosticLogger.Write(
                        $"WebView={_webViewRole}; Douban review v2 settlement; SubjectId={subjectId}; Attempt={attempt}; State={decision.State}; " +
                        $"StableSamples={decision.StableSamples}; Settled={decision.Settled}; Probe={lastProbe}");
                    if (decision.Settled || decision.TerminalFailure)
                        return new ReviewSettlementResult(
                            decision.Settled,
                            decision.TerminalFailure,
                            decision.State,
                            attempt,
                            decision.Error,
                            decision.Probe);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 settlement probe transient failure; SubjectId={subjectId}; Attempt={attempt}; Error={ex.Message}");
                }
            }

            var timeout = policy.Timeout(lastProbe);
            return new ReviewSettlementResult(false, false, timeout.State, 48, timeout.Error, timeout.Probe);
        }
        finally
        {
            _reviewWriteInProgress = false;
        }
    }

    private void ObserveReviewNavigationStarting(string? uri)
    {
        if (!_reviewWriteInProgress) return;
        _reviewNavigationStarted = true;
        _reviewNavigationCompleted = false;
        _reviewNavigationUrl = uri ?? "";
        _reviewNavigationError = "";
        DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review v2 navigation-starting; URL={_reviewNavigationUrl}");
    }

    private void ObserveReviewNavigationCompleted(CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!_reviewWriteInProgress) return;
        _reviewNavigationCompleted = true;
        _reviewNavigationUrl = _worker.Source?.ToString() ?? _reviewNavigationUrl;
        _reviewNavigationError = args.IsSuccess ? "" : args.WebErrorStatus.ToString();
        if (args.IsSuccess)
        {
            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Douban review v2 navigation-completed; Success=True; URL={_reviewNavigationUrl}");
        }
        else
        {
            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Douban review v2 navigation-completed; Success=False; Error={args.WebErrorStatus}; URL={_reviewNavigationUrl}");
        }
    }

    private string ValidateOfficialDetailSnapshot(JsonElement snapshot, string subjectId, string fixedProfileId)
    {
        if (Boolean(snapshot, "captcha"))
        {
            SetSessionState("captcha", "豆瓣要求人工验证");
            return "豆瓣要求人工验证，请完成验证后重试。";
        }
        if (Boolean(snapshot, "loginPage") || !Boolean(snapshot, "loggedIn"))
        {
            SetSessionState("not-logged-in", "豆瓣尚未登录");
            return "内置豆瓣 Profile 尚未登录，请先扫码登录。";
        }
        if (!Boolean(snapshot, "subjectPage") || !string.Equals(String(snapshot, "subjectId"), subjectId, StringComparison.Ordinal))
            return "当前页面不是请求的豆瓣影片详情页。";
        var detectedProfileId = String(snapshot, "detectedProfileId");
        if (!string.IsNullOrWhiteSpace(detectedProfileId) && !string.Equals(detectedProfileId, fixedProfileId, StringComparison.Ordinal))
            return "当前页面豆瓣用户与固定用户快照不一致。";
        return "";
    }

    private static OfficialReviewSnapshot? MapInlineDetailSnapshot(JsonElement detail)
    {
        var statusKnown = Boolean(detail, "statusKnown");
        var status = ReviewTargetResolver.NormalizeStatus(String(detail, "detectedStatus"));
        var exists = status is "wish" or "do" or "collect";
        if (!statusKnown) return null;

        var ratingKnown = Boolean(detail, "ratingKnown");
        int? rating = null;
        if (ratingKnown && detail.TryGetProperty("rating", out var ratingValue) && ratingValue.TryGetInt32(out var ratingNumber))
            rating = ratingNumber is >= 1 and <= 5 ? ratingNumber : null;
        var commentKnown = Boolean(detail, "commentKnown");
        var comment = commentKnown ? ReviewTargetResolver.NormalizeComment(String(detail, "comment")) : null;

        if (!exists)
        {
            ratingKnown = true;
            rating = null;
            commentKnown = true;
            comment = string.Empty;
        }

        return new OfficialReviewSnapshot(
            true,
            exists,
            true,
            exists ? status : null,
            ratingKnown,
            rating,
            commentKnown,
            comment,
            false,
            Array.Empty<string>(),
            false,
            rating is null,
            false,
            "detail-inline",
            null)
        {
            MarkedDateKnown = Boolean(detail, "markedDateKnown"),
            MarkedDate = String(detail, "markedDate"),
            OfficialTitle = String(detail, "officialTitle"),
            OfficialSubjectId = String(detail, "subjectId")
        };
    }

    private async Task EnsureReviewSubjectPageAsync(string subjectUrl, string subjectId, string stage)
    {
        var currentUrl = _worker.Source?.ToString() ?? string.Empty;
        var expectedToken = $"/subject/{subjectId}/";
        if (currentUrl.Contains(expectedToken, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var probe = await EvaluateAsync(DoubanWritePostSubmitProbeScript).ConfigureAwait(true);
                var reusable = Boolean(probe, "subjectPage") &&
                               !Boolean(probe, "formOpen") &&
                               !Boolean(probe, "captcha") &&
                               !Boolean(probe, "loginPage") &&
                               string.Equals(String(probe, "readyState"), "complete", StringComparison.OrdinalIgnoreCase);
                if (reusable)
                {
                    DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review performance reuse; Stage={stage}; SubjectId={subjectId}; URL={currentUrl}");
                    return;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review performance reuse probe failed; Stage={stage}; SubjectId={subjectId}; Error={ex.Message}");
            }
        }

        DiagnosticLogger.Write($"WebView={_webViewRole}; Douban review performance navigate; Stage={stage}; SubjectId={subjectId}; From={currentUrl}; To={subjectUrl}");
        await NavigateAsync(subjectUrl).ConfigureAwait(true);
    }

    private OfficialReviewSnapshot? TryConsumeFreshOfficialReview(string subjectId, string subjectUrl)
    {
        if (!_freshOfficialReviews.Remove(subjectId, out var entry)) return null;

        var age = DateTimeOffset.UtcNow - entry.CapturedAt;
        var currentUrl = _worker.Source?.ToString() ?? string.Empty;
        var sameSubjectPage = currentUrl.Contains($"/subject/{subjectId}/", StringComparison.OrdinalIgnoreCase);
        if (age < TimeSpan.Zero || age > FreshOfficialReviewTtl || !sameSubjectPage || !IsCompleteOfficialReview(entry.Snapshot))
        {
            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Douban review fresh snapshot rejected; SubjectId={subjectId}; AgeMs={age.TotalMilliseconds:F0}; " +
                $"SameSubjectPage={sameSubjectPage}; Complete={IsCompleteOfficialReview(entry.Snapshot)}; Source={entry.Source}");
            return null;
        }

        DiagnosticLogger.Write(
            $"WebView={_webViewRole}; Douban review fresh snapshot reused; SubjectId={subjectId}; AgeMs={age.TotalMilliseconds:F0}; " +
            $"TTLms={FreshOfficialReviewTtl.TotalMilliseconds:F0}; Source={entry.Source}; URL={currentUrl}");
        return entry.Snapshot;
    }

    private void RememberFreshOfficialReview(string subjectId, OfficialReviewSnapshot snapshot, string source)
    {
        // There is only one hidden Douban WebView. Once it reads another subject, older subject
        // snapshots cannot pass the same-page guard and should not accumulate in memory.
        _freshOfficialReviews.Clear();
        if (!IsCompleteOfficialReview(snapshot) || snapshot.Error is not null) return;

        _freshOfficialReviews[subjectId] = new FreshOfficialReviewEntry(snapshot, DateTimeOffset.UtcNow, source);
    }

    private static bool IsCompleteOfficialReview(OfficialReviewSnapshot snapshot) =>
        snapshot.ExistsKnown &&
        (!snapshot.Exists || (snapshot.StatusKnown && snapshot.RatingKnown && snapshot.CommentKnown));

    private static ReviewSubmitReceipt FailedReceipt(string error, object? diagnostic = null) => new(
        false,
        false,
        "",
        null,
        error,
        diagnostic);

    private static string SerializeDiagnostic(object? value)
    {
        if (value is null) return "null";
        try { return JsonSerializer.Serialize(value); }
        catch { return value.ToString() ?? ""; }
    }

    private sealed record FreshOfficialReviewEntry(
        OfficialReviewSnapshot Snapshot,
        DateTimeOffset CapturedAt,
        string Source);

    private sealed class DeferredCacheWriter : IOfficialReviewCacheWriter
    {
        public Task<bool> OverwriteFromOfficialAsync(
            string subjectId,
            string subjectUrl,
            OfficialReviewSnapshot official,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // HtmlMediaLibraryForm owns the live in-memory mirror. It applies the returned authoritative
            // snapshot after the connector releases the navigation pipeline.
            return Task.FromResult(false);
        }
    }
}
