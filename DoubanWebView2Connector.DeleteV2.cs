using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed partial class DoubanWebView2Connector
{
    private bool _deleteDialogSeen;
    private bool _deleteDialogAccepted;
    private bool _deleteNavigationStarted;
    private bool _deleteNavigationCompleted;
    private string _deleteNavigationUrl = "";
    private string _deleteRoute = "SubjectDetail";
    private string _deleteProfileId = "";

    public async Task<DeleteEntryResult> DeleteDoubanEntryAsync(string subjectUrl)
    {
        if (!IsAllowedSubjectUrl(subjectUrl))
            return DeleteEntryResult.Blocked("snapshot", null, "豆瓣影片地址无效。");
        if (_loginWindowActive)
            return DeleteEntryResult.Blocked("snapshot", null, "豆瓣登录窗口正在使用，请关闭后再删除。");

        await _navigationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            var subjectId = ExtractSubjectId(subjectUrl);
            var profileId = await EnsureFixedProfileCoreAsync("delete-v2").ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(profileId))
                return DeleteEntryResult.Blocked("snapshot", null, "无法确认当前豆瓣用户 Profile。");

            var before = await ((IDoubanOfficialReviewGateway)this)
                .ReadOfficialAsync(subjectId, subjectUrl, CancellationToken.None)
                .ConfigureAwait(true);

            if (before.Error is not null || !before.ExistsKnown)
                return DeleteEntryResult.Blocked("official-read", before, before.Error ?? "豆瓣官方当前评价未读取完整。");

            if (!before.Exists)
            {
                var listChecks = await CrossCheckDeletedFromHistoryAsync(profileId, subjectId, null, CancellationToken.None)
                    .ConfigureAwait(true);
                var doGhost = listChecks.FirstOrDefault(check =>
                    string.Equals(check.Status, "do", StringComparison.OrdinalIgnoreCase) && check.Ready && check.Contains);
                if (doGhost is not null)
                {
                    DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 route; SubjectId={subjectId}; Route=PersonalDoList; Reason=official-absent-but-do-list-present");
                    return await DeleteDoFromHistoryListAsync(
                            subjectUrl, subjectId, profileId, before, officialAlreadyAbsent: true, precheckedLists: listChecks, cancellationToken: CancellationToken.None)
                        .ConfigureAwait(true);
                }

                var preDeleteListWarning = BuildDeleteListAdvisory(listChecks);
                if (!string.IsNullOrWhiteSpace(preDeleteListWarning))
                    DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 list cross-check advisory; SubjectId={subjectId}; OfficialExists=False; Warning={preDeleteListWarning}");

                RememberFreshOfficialReview(subjectId, before, "delete-no-change");
                return new DeleteEntryResult(
                    DeleteWritePhase.NoChange, "no-change", true, false, true, true, false,
                    before, before, listChecks, null,
                    new { reason = "official-already-absent", listWarning = preDeleteListWarning });
            }

            var beforeStatus = ReviewTargetResolver.NormalizeStatus(before.Status);
            if (!before.StatusKnown || beforeStatus is not ("wish" or "do" or "collect"))
                return DeleteEntryResult.Blocked("official-read", before, "豆瓣官方当前状态未读取完整，已阻止删除。");

            if (string.Equals(beforeStatus, "do", StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 route; SubjectId={subjectId}; Route=PersonalDoList; Reason=official-status-do");
                return await DeleteDoFromHistoryListAsync(
                        subjectUrl, subjectId, profileId, before, officialAlreadyAbsent: false, precheckedLists: null, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(true);
            }

            DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 route; SubjectId={subjectId}; Route=SubjectDetail; BeforeStatus={beforeStatus}");
            await NavigateAsync(subjectUrl).ConfigureAwait(true);
            var deleteSnapshot = await EvaluateAsync(
                DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                .ConfigureAwait(true);

            var snapshotError = ValidateDeletePreflightSnapshot(deleteSnapshot, subjectId, profileId);
            if (!string.IsNullOrWhiteSpace(snapshotError))
                return DeleteEntryResult.Blocked("delete-control", before, snapshotError, deleteSnapshot.ToString());

            var prepared = await EvaluateAsync(DoubanDeletePrepareScript).ConfigureAwait(true);
            if (!Boolean(prepared, "prepared"))
                return DeleteEntryResult.Blocked("delete-control", before, String(prepared, "error", "豆瓣官方删除控件不可用。"), prepared.ToString());

            ResetDeleteObservation(subjectId, "SubjectDetail", profileId);
            JsonElement invoked = default;
            string invokeError = "";
            try
            {
                invoked = await EvaluateAsync(DoubanDeleteInvokeScript).ConfigureAwait(true);
                if (!Boolean(invoked, "invoked"))
                    invokeError = String(invoked, "error", "未能触发豆瓣官方删除控件。");
            }
            catch (Exception ex)
            {
                // A top-level navigation can invalidate ExecuteScriptAsync after the official click.
                // Continue into settlement/readback; click itself is never used as success evidence.
                invokeError = ex.Message;
                DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 invoke script interrupted; SubjectId={subjectId}; Error={ex.Message}");
            }

            var submissionObserved = Boolean(invoked, "invoked") || _deleteDialogSeen || _deleteNavigationStarted;
            var settlement = await WaitForDeleteSettlementAsync(subjectUrl, subjectId, profileId, CancellationToken.None)
                .ConfigureAwait(true);

            if (!settlement.Settled)
            {
                return new DeleteEntryResult(
                    settlement.TerminalFailure ? DeleteWritePhase.Failed : DeleteWritePhase.Uncertain,
                    "settlement", false, submissionObserved, false, false, false,
                    before, settlement.Official, Array.Empty<DeleteHistoryCheck>(),
                    settlement.Error ?? (string.IsNullOrWhiteSpace(invokeError) ? "豆瓣删除结果尚未确认。" : invokeError),
                    new
                    {
                        prepared,
                        invoked = invoked.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : invoked,
                        invokeError,
                        dialogSeen = _deleteDialogSeen,
                        dialogAccepted = _deleteDialogAccepted,
                        navigationStarted = _deleteNavigationStarted,
                        navigationCompleted = _deleteNavigationCompleted,
                        navigationUrl = _deleteNavigationUrl,
                        settlement.Diagnostic
                    });
            }

            var official = settlement.Official ?? OfficialReviewSnapshot.Unknown("delete-readback", "删除结算成功但缺少官方回读。");
            if (!official.ExistsKnown || official.Exists)
            {
                return new DeleteEntryResult(
                    DeleteWritePhase.Uncertain, "readback", true, submissionObserved, false, false, false,
                    before, official, Array.Empty<DeleteHistoryCheck>(),
                    "豆瓣删除提交已结算，但官方详情仍未明确确认评价已不存在。",
                    settlement.Diagnostic);
            }

            // The subject page is the authoritative per-subject source. Douban's personal wish/do/collect
            // pages can lag behind a successful remove for several seconds (or longer) because those lists are
            // independently cached. Keep list checks as diagnostics only after two stable subject-page samples
            // have already confirmed status=none and the delete control is gone.
            _deleteConfirmationPending = false;
            var checks = await CrossCheckDeletedFromHistoryAsync(profileId, subjectId, beforeStatus, CancellationToken.None)
                .ConfigureAwait(true);
            var listWarning = BuildDeleteListAdvisory(checks);
            if (!string.IsNullOrWhiteSpace(listWarning))
                DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 list cross-check advisory; SubjectId={subjectId}; OfficialExists=False; Warning={listWarning}");

            RememberFreshOfficialReview(subjectId, official, "delete-result");
            return new DeleteEntryResult(
                DeleteWritePhase.Confirmed, "readback", true, true, false, true, false,
                before, official, checks, null,
                new
                {
                    prepared,
                    invoked = invoked.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : invoked,
                    invokeError,
                    dialogSeen = _deleteDialogSeen,
                    dialogAccepted = _deleteDialogAccepted,
                    navigationStarted = _deleteNavigationStarted,
                    navigationCompleted = _deleteNavigationCompleted,
                    navigationUrl = _deleteNavigationUrl,
                    settlement = settlement.Diagnostic,
                    checks,
                    listWarning
                });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 failed; URL={subjectUrl}; Error={ex}");
            return DeleteEntryResult.Failed("exception", _deleteNavigationStarted || _deleteDialogAccepted, null, ex.Message, ex.ToString(), _deleteRoute);
        }
        finally
        {
            _deleteConfirmationPending = false;
            _deleteSubjectId = "";
            _deleteRoute = "SubjectDetail";
            _deleteProfileId = "";
            _navigationGate.Release();
        }
    }

    private async Task<DeleteEntryResult> DeleteDoFromHistoryListAsync(
        string subjectUrl,
        string subjectId,
        string profileId,
        OfficialReviewSnapshot before,
        bool officialAlreadyAbsent,
        IReadOnlyList<DeleteHistoryCheck>? precheckedLists,
        CancellationToken cancellationToken)
    {
        const string route = "PersonalDoList";
        var located = await LocateDoHistoryDeleteTargetAsync(profileId, subjectId, cancellationToken).ConfigureAwait(true);
        if (!located.Ready)
            return DeleteEntryResult.Blocked("do-list-locate", before,
                located.Error ?? "豆瓣在看列表未能稳定读取，已阻止删除。", located.Diagnostic, route);

        if (!located.Found)
        {
            if (officialAlreadyAbsent)
            {
                var checks = precheckedLists ?? await CrossCheckDeletedFromHistoryAsync(profileId, subjectId, null, cancellationToken).ConfigureAwait(true);
                RememberFreshOfficialReview(subjectId, before, "delete-do-list-already-absent");
                return new DeleteEntryResult(
                    DeleteWritePhase.NoChange, "do-list-already-absent", true, false, true, true, false,
                    before, before, checks, null,
                    new { deleteRoute = route, located = located.Diagnostic, reason = "detail-and-do-list-already-absent" },
                    route);
            }

            return new DeleteEntryResult(
                DeleteWritePhase.Uncertain, "do-list-locate", false, false, false, false, false,
                before, null, precheckedLists ?? Array.Empty<DeleteHistoryCheck>(),
                "豆瓣官方状态为在看，但个人在看列表中没有找到该影片；为避免误删，未改走详情页删除。",
                new { deleteRoute = route, located = located.Diagnostic },
                route);
        }

        var clickPasses = new List<object>();
        var submissionObserved = false;
        var invokeError = "";
        JsonElement prepared = default;
        JsonElement invoked = default;
        DoListSettlementResult? listSettlement = null;
        var activeLocated = located;

        // Douban can expose a two-stage delete for /do rows that currently contain a short comment:
        // the first trusted click may clear the comment/rating payload while leaving the /do row,
        // and the same official "删除" control then removes the remaining do record on the next click.
        // Keep this as one user transaction, but cap the internal trusted-click passes at two.
        for (var deletePass = 1; deletePass <= 2; deletePass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (deletePass > 1)
            {
                // WaitForDoHistoryRemovalAsync has already performed a fresh reload and
                // confirmed the target is still present. Reuse that current page instead
                // of navigating to /do a third time before the second trusted click.
                activeLocated = await InspectCurrentDoHistoryDeleteTargetAsync(
                        activeLocated.PageUrl, profileId, subjectId, cancellationToken)
                    .ConfigureAwait(true);
                if (!activeLocated.Ready)
                {
                    return new DeleteEntryResult(
                        DeleteWritePhase.Uncertain, "do-list-second-pass-locate", true, submissionObserved, false, false, false,
                        before, null, precheckedLists ?? Array.Empty<DeleteHistoryCheck>(),
                        activeLocated.Error ?? "第一次删除后重新定位豆瓣在看记录失败，本地记录保持不变。",
                        new
                        {
                            deleteRoute = route,
                            initialLocated = located.Diagnostic,
                            clickPasses,
                            secondPassLocated = activeLocated.Diagnostic
                        },
                        route);
                }

                if (!activeLocated.Found)
                {
                    listSettlement = new DoListSettlementResult(
                        Settled: true,
                        TerminalFailure: false,
                        TargetStillPresent: false,
                        Error: null,
                        Diagnostic: new
                        {
                            mode = "second-pass-current-page-already-absent",
                            initialLocated = located.Diagnostic,
                            clickPasses,
                            secondPassLocated = activeLocated.Diagnostic
                        });
                    break;
                }
            }

            if (activeLocated.DeleteCandidateCount != 1)
            {
                return DeleteEntryResult.Blocked(
                    deletePass == 1 ? "do-list-delete-control" : "do-list-second-pass-delete-control",
                    before,
                    activeLocated.DeleteCandidateCount == 0
                        ? "在看列表目标影片的官方删除控件缺失。"
                        : "在看列表目标影片的官方删除控件不唯一。",
                    new { deletePass, activeLocated = activeLocated.Diagnostic, clickPasses },
                    route);
            }

            prepared = await EvaluateAsync(
                    DoubanDoListDeletePrepareScript
                        .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                        .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                .ConfigureAwait(true);
            if (!Boolean(prepared, "prepared"))
            {
                return DeleteEntryResult.Blocked(
                    deletePass == 1 ? "do-list-delete-control" : "do-list-second-pass-delete-control",
                    before,
                    String(prepared, "error", "在看列表官方删除控件不可用。"),
                    new { deletePass, activeLocated = activeLocated.Diagnostic, prepared = prepared.ToString(), clickPasses },
                    route);
            }

            DiagnosticLogger.Write(
                $"WebView={_webViewRole}; Delete v2 do-list control prepared; SubjectId={subjectId}; Pass={deletePass}; " +
                $"Url={_worker.Source}; Prepared={prepared}");

            ResetDeleteObservation(subjectId, route, profileId);
            invoked = default;
            invokeError = "";
            try
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(true);
                invoked = await DispatchTrustedDoListDeleteClickAsync(profileId, subjectId, cancellationToken)
                    .ConfigureAwait(true);
                if (!Boolean(invoked, "invoked"))
                    invokeError = String(invoked, "error", "未能在豆瓣在看列表实际点击删除控件。");
            }
            catch (Exception ex)
            {
                invokeError = ex.Message;
                DiagnosticLogger.Write(
                    $"WebView={_webViewRole}; Delete v2 do-list trusted-click interrupted; SubjectId={subjectId}; " +
                    $"Pass={deletePass}; PageUrl={activeLocated.PageUrl}; Error={ex.Message}");
            }

            var passSubmissionObserved = Boolean(invoked, "invoked") || _deleteDialogSeen || _deleteNavigationStarted;
            submissionObserved |= passSubmissionObserved;

            listSettlement = await WaitForDoHistoryRemovalAsync(
                    activeLocated.PageUrl, profileId, subjectId, cancellationToken)
                .ConfigureAwait(true);

            clickPasses.Add(new
            {
                pass = deletePass,
                located = activeLocated.Diagnostic,
                prepared = prepared.ToString(),
                invoked = invoked.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : invoked,
                invokeError,
                passSubmissionObserved,
                dialogSeen = _deleteDialogSeen,
                dialogAccepted = _deleteDialogAccepted,
                navigationStarted = _deleteNavigationStarted,
                navigationCompleted = _deleteNavigationCompleted,
                navigationUrl = _deleteNavigationUrl,
                settlement = listSettlement.Diagnostic
            });

            if (listSettlement.Settled)
                break;

            if (listSettlement.TerminalFailure)
            {
                return new DeleteEntryResult(
                    DeleteWritePhase.Failed, "do-list-settlement", false, submissionObserved, false, false, false,
                    before, null, precheckedLists ?? Array.Empty<DeleteHistoryCheck>(),
                    listSettlement.Error ?? (string.IsNullOrWhiteSpace(invokeError) ? "豆瓣在看列表删除结果尚未确认。" : invokeError),
                    new { deleteRoute = route, initialLocated = located.Diagnostic, clickPasses },
                    route);
            }

            if (deletePass == 1 && listSettlement.TargetStillPresent)
            {
                DiagnosticLogger.Write(
                    $"WebView={_webViewRole}; Delete v2 do-list first pass left target present; " +
                    $"SubjectId={subjectId}; AutoSecondPass=True; PageUrl={activeLocated.PageUrl}");
                continue;
            }

            return new DeleteEntryResult(
                DeleteWritePhase.Uncertain,
                deletePass == 1 ? "do-list-settlement" : "do-list-second-pass-settlement",
                false, submissionObserved, false, false, false,
                before, null, precheckedLists ?? Array.Empty<DeleteHistoryCheck>(),
                listSettlement.Error ?? (string.IsNullOrWhiteSpace(invokeError) ? "豆瓣在看列表删除结果尚未确认。" : invokeError),
                new { deleteRoute = route, initialLocated = located.Diagnostic, clickPasses },
                route);
        }

        if (listSettlement is null || !listSettlement.Settled)
        {
            return new DeleteEntryResult(
                DeleteWritePhase.Uncertain, "do-list-settlement", false, submissionObserved, false, false, false,
                before, null, precheckedLists ?? Array.Empty<DeleteHistoryCheck>(),
                "豆瓣在看列表删除结果尚未确认。",
                new { deleteRoute = route, initialLocated = located.Diagnostic, clickPasses },
                route);
        }

        _deleteConfirmationPending = false;

        // The /do fresh-reload settlement already provides the hard list proof. The old
        // path then re-opened wish/do/collect and ran the full edit-form reader twice,
        // adding several seconds without increasing certainty. R8 uses one subject-page
        // navigation plus two lightweight DOM samples instead.
        var official = await ReadDeletedDetailSnapshotAsync(subjectId, subjectUrl, profileId, cancellationToken)
            .ConfigureAwait(true);

        var checksAfter = new[]
        {
            new DeleteHistoryCheck("do", true, false, 1, false, "fresh-do-list-delete-settlement")
        };

        if (!OfficialDeleteReadbackIsCompleteAbsent(official))
        {
            return new DeleteEntryResult(
                DeleteWritePhase.Uncertain, "do-list-detail-readback", true, submissionObserved, false, false, false,
                before, official, checksAfter,
                "在看列表已确认移除该影片，但豆瓣影片详情尚未稳定确认状态、评分和短评均已清空，本地记录保持不变。",
                new
                {
                    deleteRoute = route,
                    initialLocated = located.Diagnostic,
                    clickPasses,
                    listSettlement = listSettlement.Diagnostic,
                    official = SerializeDiagnostic(official),
                    checksAfter
                },
                route);
        }

        RememberFreshOfficialReview(subjectId, official, "delete-do-list-result");
        return new DeleteEntryResult(
            DeleteWritePhase.Confirmed, "do-list-readback", true, submissionObserved, false, true, false,
            before, official, checksAfter, null,
            new
            {
                deleteRoute = route,
                initialLocated = located.Diagnostic,
                clickPasses,
                listSettlement = listSettlement.Diagnostic,
                official = SerializeDiagnostic(official),
                checksAfter
            },
            route);
    }

    private async Task<JsonElement> DispatchTrustedDoListDeleteClickAsync(
        string profileId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hit = await EvaluateAsync(
                DoubanDoListDeleteHitTestScript
                    .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                    .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
            .ConfigureAwait(true);

        if (!Boolean(hit, "ready"))
            return hit;

        if (!hit.TryGetProperty("centerX", out var xNode) || !xNode.TryGetDouble(out var x) ||
            !hit.TryGetProperty("centerY", out var yNode) || !yNode.TryGetDouble(out var y))
        {
            return JsonSerializer.SerializeToElement(new
            {
                invoked = false,
                error = "在看列表删除按钮坐标读取失败。",
                hit = hit.ToString()
            });
        }

        if (x < 0 || y < 0 || x > Math.Max(1, _worker.Width) || y > Math.Max(1, _worker.Height))
        {
            return JsonSerializer.SerializeToElement(new
            {
                invoked = false,
                error = "在看列表删除按钮不在 WorkerWebView 可点击视口内。",
                centerX = x,
                centerY = y,
                viewportWidth = _worker.Width,
                viewportHeight = _worker.Height,
                hit = hit.ToString()
            });
        }

        var core = _worker.CoreWebView2;
        if (core is null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                invoked = false,
                error = "WorkerWebView 尚未初始化。"
            });
        }

        // CDP Input.dispatchMouseEvent goes through Chromium's input pipeline, matching a real page
        // click much more closely than HTMLElement.click(). Coordinates are CSS viewport pixels.
        await core.CallDevToolsProtocolMethodAsync(
            "Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new
            {
                type = "mouseMoved",
                x,
                y,
                button = "none"
            })).ConfigureAwait(true);

        await core.CallDevToolsProtocolMethodAsync(
            "Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new
            {
                type = "mousePressed",
                x,
                y,
                button = "left",
                buttons = 1,
                clickCount = 1
            })).ConfigureAwait(true);

        await core.CallDevToolsProtocolMethodAsync(
            "Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new
            {
                type = "mouseReleased",
                x,
                y,
                button = "left",
                buttons = 0,
                clickCount = 1
            })).ConfigureAwait(true);

        DiagnosticLogger.Write(
            $"WebView={_webViewRole}; Delete v2 do-list trusted click dispatched; " +
            $"SubjectId={subjectId}; Url={_worker.Source}; X={x:F1}; Y={y:F1}; " +
            $"Tag={String(hit, "tag")}; Class={String(hit, "className")}; " +
            $"FormActionPath={String(hit, "formActionPath")}; HrefPath={String(hit, "hrefPath")}");

        return JsonSerializer.SerializeToElement(new
        {
            invoked = true,
            route = "PersonalDoList",
            matchedSubjectId = subjectId,
            clickMode = "ChromiumInput.dispatchMouseEvent",
            centerX = x,
            centerY = y,
            tag = String(hit, "tag"),
            className = String(hit, "className"),
            hrefPath = String(hit, "hrefPath"),
            formActionPath = String(hit, "formActionPath"),
            formMethod = String(hit, "formMethod")
        });
    }

    private async Task<DoListTargetResult> LocateDoHistoryDeleteTargetAsync(
        string profileId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        const int maxPages = 60;
        var pages = new List<object>();
        for (var page = 0; page < maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageUrl = page == 0
                ? $"https://movie.douban.com/people/{profileId}/do"
                : $"https://movie.douban.com/people/{profileId}/do?start={page * 15}";
            try
            {
                await NavigateAsync(pageUrl, cancellationToken).ConfigureAwait(true);
                JsonElement probe = default;
                var ready = false;
                for (var attempt = 1; attempt <= 12; attempt++)
                {
                    probe = await EvaluateAsync(
                            DoubanDoListDeleteProbeScript
                                .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                                .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                        .ConfigureAwait(true);
                    if (Boolean(probe, "captcha"))
                        return new(false, false, true, pageUrl, page, 0, false, "豆瓣要求人工验证。", new { pages, probe = probe.ToString() });
                    if (Boolean(probe, "loginPage"))
                        return new(false, false, true, pageUrl, page, 0, false, "豆瓣登录已失效。", new { pages, probe = probe.ToString() });
                    if (Boolean(probe, "ready"))
                    {
                        ready = true;
                        break;
                    }
                    await Task.Delay(250, cancellationToken).ConfigureAwait(true);
                }

                if (!ready)
                    return new(false, false, false, pageUrl, page, 0, false, "豆瓣在看列表未在等待时间内稳定。", new { pages });

                var found = Boolean(probe, "found");
                var candidateCount = Int(probe, "deleteCandidateCount") ?? 0;
                var hasMore = Boolean(probe, "hasMore");
                pages.Add(new { page, pageUrl, found, candidateCount, hasMore, probe = probe.ToString() });
                DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 do-list locate; SubjectId={subjectId}; Page={page}; Found={found}; DeleteCandidateCount={candidateCount}; HasMore={hasMore}; Url={pageUrl}; Probe={probe}");
                if (found)
                    return new(true, true, false, pageUrl, page, candidateCount, hasMore, null, new { pages, matchedProbe = probe.ToString() });
                if (!hasMore)
                    return new(false, true, false, pageUrl, page, 0, false, null, new { pages, reason = "end-of-list" });
            }
            catch (Exception ex)
            {
                return new(false, false, false, pageUrl, page, 0, false, ex.Message, new { pages, exception = ex.ToString() });
            }
        }

        return new(false, false, false, $"https://movie.douban.com/people/{profileId}/do", maxPages - 1, 0, true,
            $"在看列表超过 {maxPages} 页仍未找到目标影片，为避免无限导航已停止。", new { pages, maxPages });
    }

    private async Task<DoListTargetResult> InspectCurrentDoHistoryDeleteTargetAsync(
        string pageUrl,
        string profileId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        JsonElement probe = default;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            probe = await EvaluateAsync(
                    DoubanDoListDeleteProbeScript
                        .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                        .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                .ConfigureAwait(true);
            if (Boolean(probe, "captcha"))
                return new(false, false, true, pageUrl, 0, 0, false, "豆瓣要求人工验证。", new { attempt, probe = probe.ToString() });
            if (Boolean(probe, "loginPage"))
                return new(false, false, true, pageUrl, 0, 0, false, "豆瓣登录已失效。", new { attempt, probe = probe.ToString() });
            if (Boolean(probe, "ready"))
            {
                var found = Boolean(probe, "found");
                var candidateCount = Int(probe, "deleteCandidateCount") ?? 0;
                return new(found, true, false, pageUrl, 0, candidateCount, Boolean(probe, "hasMore"), null,
                    new { mode = "reuse-current-do-page", attempt, probe = probe.ToString() });
            }
            await Task.Delay(120, cancellationToken).ConfigureAwait(true);
        }
        return new(false, false, false, pageUrl, 0, 0, false, "当前豆瓣在看列表未稳定。", new { probe = probe.ToString() });
    }

    private async Task<DoListSettlementResult> WaitForDoHistoryRemovalAsync(
        string pageUrl,
        string profileId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        var samples = new List<object>();

        // A short grace period lets Douban process the trusted click. The subsequent fresh
        // navigation is the authoritative proof, so the old ~2 second passive polling loop
        // is unnecessary.
        await Task.Delay(550, cancellationToken).ConfigureAwait(true);
        try
        {
            await NavigateAsync(pageUrl, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new(false, false, false, "删除后重新打开豆瓣在看列表失败：" + ex.Message, new { samples });
        }

        var absentStable = 0;
        var presentStable = 0;
        for (var attempt = 1; attempt <= 7; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 1) await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            var probe = await EvaluateAsync(
                    DoubanDoListDeleteProbeScript
                        .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                        .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                .ConfigureAwait(true);
            samples.Add(new { attempt, phase = "fresh-do-list-fast", probe = probe.ToString() });
            if (Boolean(probe, "captcha"))
                return new(false, true, false, "豆瓣要求人工验证，在看列表删除结果未确认。", new { samples });
            if (Boolean(probe, "loginPage"))
                return new(false, true, false, "豆瓣登录已失效，在看列表删除结果未确认。", new { samples });

            if (!Boolean(probe, "ready"))
            {
                absentStable = 0;
                presentStable = 0;
                continue;
            }

            if (!Boolean(probe, "found"))
            {
                absentStable++;
                presentStable = 0;
                if (absentStable >= 2)
                    return new(true, false, false, null, new { mode = "fresh-do-list-fast", pageUrl, attempts = attempt, samples });
            }
            else
            {
                presentStable++;
                absentStable = 0;
                if (presentStable >= 2)
                    return new(false, false, true, "豆瓣在看列表重新加载后目标影片仍稳定存在。", new { mode = "fresh-do-list-fast-still-present", pageUrl, attempts = attempt, samples });
            }
        }

        return new(false, false, false, "豆瓣在看列表重新加载后未能稳定确认该影片是否已移除。", new { pageUrl, samples });
    }

    private async Task<OfficialReviewSnapshot> ReadDeletedDetailSnapshotAsync(
        string subjectId,
        string subjectUrl,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            await NavigateAsync(subjectUrl, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return OfficialReviewSnapshot.Unknown("delete-detail-fast-readback", "删除后重新打开豆瓣影片详情失败：" + ex.Message);
        }

        string previousSignature = "";
        var stable = 0;
        JsonElement latest = default;
        for (var attempt = 1; attempt <= 7; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 1) await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            latest = await EvaluateAsync(
                    DoubanWriteSnapshotScript.Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                .ConfigureAwait(true);

            var validationError = ValidateOfficialDetailSnapshot(latest, subjectId, profileId);
            if (!string.IsNullOrWhiteSpace(validationError))
                return OfficialReviewSnapshot.Unknown("delete-detail-fast-readback", validationError);
            if (Boolean(latest, "captcha"))
                return OfficialReviewSnapshot.Unknown("delete-detail-fast-readback", "豆瓣要求人工验证，请完成验证后重试。");
            if (Boolean(latest, "loginPage"))
                return OfficialReviewSnapshot.Unknown("delete-detail-fast-readback", "豆瓣登录已失效。");

            var detected = String(latest, "detectedStatus");
            var absent = Boolean(latest, "statusKnown") && string.Equals(detected, "none", StringComparison.OrdinalIgnoreCase) &&
                         (Int(latest, "deleteControlCount") ?? 0) == 0 && !Boolean(latest, "editControlFound") &&
                         Boolean(latest, "markedDateKnown") && string.IsNullOrWhiteSpace(String(latest, "markedDate"));
            var signature = $"{Boolean(latest, "ready")}|{Boolean(latest, "statusKnown")}|{detected}|{Int(latest, "deleteControlCount") ?? -1}|{Boolean(latest, "editControlFound")}|{String(latest, "markedDate")}";
            stable = absent && string.Equals(signature, previousSignature, StringComparison.Ordinal) ? stable + 1 : (absent ? 1 : 0);
            previousSignature = signature;

            DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 fast detail readback; SubjectId={subjectId}; Attempt={attempt}; Stable={stable}; Absent={absent}; Snapshot={latest}");
            if (stable < 2) continue;

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
                Source: "delete-detail-fast-readback",
                Error: null)
            {
                MarkedDateKnown = true,
                MarkedDate = string.Empty,
                OfficialTitle = String(latest, "officialTitle"),
                OfficialSubjectId = String(latest, "subjectId")
            };
        }

        return OfficialReviewSnapshot.Unknown("delete-detail-fast-readback", "删除后豆瓣影片详情未在等待时间内稳定确认评价已清空。");
    }

    private static bool OfficialDeleteReadbackIsCompleteAbsent(OfficialReviewSnapshot snapshot) =>
        snapshot.Error is null &&
        snapshot.ExistsKnown && !snapshot.Exists &&
        snapshot.StatusKnown && string.IsNullOrWhiteSpace(snapshot.Status) &&
        snapshot.RatingKnown && snapshot.Rating is null &&
        snapshot.CommentKnown && string.IsNullOrWhiteSpace(snapshot.Comment) &&
        snapshot.MarkedDateKnown && string.IsNullOrWhiteSpace(snapshot.MarkedDate);

    private bool IsExpectedDeleteDialogContext(string? dialogUri, string? extractedSubjectId)
    {
        if (!_deleteConfirmationPending) return false;
        if (string.Equals(extractedSubjectId, _deleteSubjectId, StringComparison.Ordinal)) return true;
        if (!string.Equals(_deleteRoute, "PersonalDoList", StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(_deleteProfileId)) return false;

        var candidates = new[] { dialogUri ?? "", _worker.Source?.ToString() ?? "" };
        foreach (var candidate in candidates)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) continue;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(uri.Host, "movie.douban.com", StringComparison.OrdinalIgnoreCase)) continue;
            var expectedPrefix = $"/people/{_deleteProfileId}/do";
            if (uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private sealed record DoListTargetResult(
        bool Found,
        bool Ready,
        bool TerminalFailure,
        string PageUrl,
        int PageIndex,
        int DeleteCandidateCount,
        bool HasMore,
        string? Error,
        object? Diagnostic);

    private sealed record DoListSettlementResult(
        bool Settled,
        bool TerminalFailure,
        bool TargetStillPresent,
        string? Error,
        object? Diagnostic);

    private void ResetDeleteObservation(string subjectId, string route, string profileId)
    {
        _deleteSubjectId = subjectId;
        _deleteRoute = string.IsNullOrWhiteSpace(route) ? "SubjectDetail" : route;
        _deleteProfileId = profileId ?? "";
        _deleteConfirmationPending = true;
        _deleteDialogSeen = false;
        _deleteDialogAccepted = false;
        _deleteNavigationStarted = false;
        _deleteNavigationCompleted = false;
        _deleteNavigationUrl = "";
    }

    private void ObserveDeleteNavigationStarting(string uri)
    {
        if (!_deleteConfirmationPending) return;
        _deleteNavigationStarted = true;
        _deleteNavigationUrl = uri ?? "";
        DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 navigation observed; Phase=starting; SubjectId={_deleteSubjectId}; Url={_deleteNavigationUrl}");
    }

    private void ObserveDeleteNavigationCompleted()
    {
        if (!_deleteConfirmationPending) return;
        _deleteNavigationCompleted = true;
        DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 navigation observed; Phase=completed; SubjectId={_deleteSubjectId}; Url={_worker.Source}");
    }

    private string ValidateDeletePreflightSnapshot(JsonElement snapshot, string subjectId, string profileId)
    {
        if (Boolean(snapshot, "captcha")) return "豆瓣要求人工验证，请完成验证后重试。";
        if (Boolean(snapshot, "loginPage") || !Boolean(snapshot, "loggedIn")) return "豆瓣登录已失效。";
        if (!string.Equals(String(snapshot, "subjectId"), subjectId, StringComparison.Ordinal)) return "删除前豆瓣影片 ID 与目标不一致。";
        if (!string.Equals(String(snapshot, "profileId"), profileId, StringComparison.Ordinal)) return "删除前豆瓣用户 Profile 与固定账号不一致。";
        var status = ReviewTargetResolver.NormalizeStatus(String(snapshot, "detectedStatus"));
        if (!Boolean(snapshot, "statusKnown") || status is not ("wish" or "do" or "collect")) return "删除前官方状态未稳定，已阻止删除。";
        if (!Boolean(snapshot, "deleteControlFound") || Int(snapshot, "deleteControlCount") != 1) return "豆瓣官方删除控件缺失或不唯一，已阻止删除。";
        var tag = String(snapshot, "deleteControlTag").ToUpperInvariant();
        if (tag is not ("A" or "BUTTON" or "INPUT")) return "豆瓣官方删除控件类型不受支持，已阻止删除。";
        return "";
    }

    private async Task<DeleteSettlementResult> WaitForDeleteSettlementAsync(
        string subjectUrl,
        string subjectId,
        string profileId,
        CancellationToken cancellationToken)
    {
        var samples = new List<object>();
        var stable = 0;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(true);
            try
            {
                var probe = await EvaluateAsync(
                    DoubanDeleteReadbackScript
                        .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                        .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                    .ConfigureAwait(true);
                samples.Add(new { attempt, phase = "passive", probe = probe.ToString() });
                if (Boolean(probe, "captcha"))
                    return new(false, true, null, "豆瓣要求人工验证，删除结果未确认。", new { samples });
                if (Boolean(probe, "loginPage"))
                    return new(false, true, null, "豆瓣登录已失效，删除结果未确认。", new { samples });
                if (DeleteReadbackShowsAbsent(probe, subjectId, profileId))
                {
                    stable++;
                    if (stable >= 2)
                    {
                        var official = OfficialAbsentSnapshot(probe, subjectId);
                        return new(true, false, official, null, new { mode = "passive", attempts = attempt, samples });
                    }
                }
                else stable = 0;
            }
            catch (Exception ex)
            {
                samples.Add(new { attempt, phase = "passive", error = ex.Message });
            }
        }

        // Only after giving the official click/navigation time to finish do one explicit fresh
        // subject navigation. The old implementation navigated away after a fixed 1.8 s and could
        // race the server request; this path never does that.
        try
        {
            await NavigateAsync(subjectUrl, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new(false, false, null, "删除后重新读取豆瓣详情失败：" + ex.Message, new { samples });
        }

        stable = 0;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(300, cancellationToken).ConfigureAwait(true);
            var probe = await EvaluateAsync(
                DoubanDeleteReadbackScript
                    .Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId))
                    .Replace("__PROFILE_ID__", JsonSerializer.Serialize(profileId)))
                .ConfigureAwait(true);
            samples.Add(new { attempt, phase = "fresh-readback", probe = probe.ToString() });
            if (Boolean(probe, "captcha"))
                return new(false, true, null, "豆瓣要求人工验证，删除结果未确认。", new { samples });
            if (Boolean(probe, "loginPage"))
                return new(false, true, null, "豆瓣登录已失效，删除结果未确认。", new { samples });
            if (DeleteReadbackShowsAbsent(probe, subjectId, profileId))
            {
                stable++;
                if (stable >= 2)
                {
                    var official = OfficialAbsentSnapshot(probe, subjectId);
                    return new(true, false, official, null, new { mode = "fresh-readback", attempts = attempt, samples });
                }
            }
            else stable = 0;
        }

        return new(false, false, null, "豆瓣官方详情在等待时间内没有连续确认评价已删除。", new { samples });
    }

    private static bool DeleteReadbackShowsAbsent(JsonElement probe, string subjectId, string profileId) =>
        Boolean(probe, "ready") &&
        !Boolean(probe, "captcha") &&
        !Boolean(probe, "loginPage") &&
        string.Equals(String(probe, "subjectId"), subjectId, StringComparison.Ordinal) &&
        string.Equals(String(probe, "profileId"), profileId, StringComparison.Ordinal) &&
        Boolean(probe, "statusKnown") &&
        string.Equals(String(probe, "detectedStatus"), "none", StringComparison.OrdinalIgnoreCase) &&
        Int(probe, "deleteControlCount") == 0;

    private static OfficialReviewSnapshot OfficialAbsentSnapshot(JsonElement probe, string subjectId) => new(
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
        Source: "delete-detail-readback",
        Error: null)
    {
        MarkedDateKnown = true,
        MarkedDate = string.Empty,
        OfficialTitle = String(probe, "officialTitle"),
        OfficialSubjectId = subjectId
    };

    private async Task<IReadOnlyList<DeleteHistoryCheck>> CrossCheckDeletedFromHistoryAsync(
        string profileId,
        string subjectId,
        string? previousStatus,
        CancellationToken cancellationToken)
    {
        var results = new List<DeleteHistoryCheck>();
        foreach (var status in new[] { "wish", "do", "collect" })
        {
            var prior = string.Equals(status, previousStatus, StringComparison.OrdinalIgnoreCase);
            var maxPages = prior ? 4 : 1;
            var pagesScanned = 0;
            var hasMore = false;
            var ready = true;
            var contains = false;
            var error = "";

            for (var page = 0; page < maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await NavigateAsync($"https://movie.douban.com/people/{profileId}/{status}?start={page * 15}", cancellationToken)
                        .ConfigureAwait(true);
                    JsonElement probe = default;
                    var pageReady = false;
                    for (var attempt = 0; attempt < 12; attempt++)
                    {
                        probe = await EvaluateAsync(
                            DoubanDeleteHistoryProbeScript.Replace("__SUBJECT_ID__", JsonSerializer.Serialize(subjectId)))
                            .ConfigureAwait(true);
                        if (Boolean(probe, "captcha"))
                        {
                            error = "豆瓣要求人工验证。";
                            break;
                        }
                        if (Boolean(probe, "loginPage"))
                        {
                            error = "豆瓣登录已失效。";
                            break;
                        }
                        if (Boolean(probe, "ready"))
                        {
                            pageReady = true;
                            break;
                        }
                        await Task.Delay(250, cancellationToken).ConfigureAwait(true);
                    }

                    if (!pageReady)
                    {
                        ready = false;
                        if (string.IsNullOrWhiteSpace(error)) error = "个人状态列表未在等待时间内稳定。";
                        break;
                    }

                    pagesScanned++;
                    contains = Boolean(probe, "contains");
                    hasMore = Boolean(probe, "hasMore");
                    DiagnosticLogger.Write($"WebView={_webViewRole}; Delete v2 list cross-check; SubjectId={subjectId}; Status={status}; Page={page}; Contains={contains}; HasMore={hasMore}");
                    if (contains || !hasMore) break;
                }
                catch (Exception ex)
                {
                    ready = false;
                    error = ex.Message;
                    break;
                }
            }

            results.Add(new DeleteHistoryCheck(
                status,
                ready,
                contains,
                pagesScanned,
                hasMore,
                prior ? "previous-status-up-to-4-pages" : "first-page-cross-check",
                error));
        }
        return results;
    }

    private static string BuildDeleteListAdvisory(IReadOnlyList<DeleteHistoryCheck> checks)
    {
        if (checks.Count != 3 || checks.Any(check => !check.Ready))
            return "豆瓣个人状态列表复核未完整完成；官方详情已连续确认评价不存在，列表结果仅作诊断参考。";
        var found = checks.FirstOrDefault(check => check.Contains);
        if (found is not null)
            return $"豆瓣 {found.Status} 列表仍暂时显示该影片，按列表传播/缓存延迟处理；官方详情已连续确认评价不存在。";
        return "";
    }

    private sealed record DeleteSettlementResult(
        bool Settled,
        bool TerminalFailure,
        OfficialReviewSnapshot? Official,
        string? Error,
        object? Diagnostic);

    internal const string DoubanDeletePrepareScript = """
(() => {
  const personal=[...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')]
    .find(x=>!x.closest('#interest_sectl,.rating_wrap'));
  const norm=v=>String(v||'').replace(/\s+/g,'').trim();
  const candidates=personal?[...personal.querySelectorAll('input,a,button')].filter(node=>
    norm(node.value||node.textContent)==='删除'&&/(^|\s)a_confirm_link(\s|$)/i.test(String(node.className||''))):[];
  if(candidates.length!==1)return {prepared:false,error:candidates.length===0?'官方删除控件缺失':'官方删除控件不唯一',candidateCount:candidates.length};
  const node=candidates[0];
  if(!['A','BUTTON','INPUT'].includes(node.tagName))return {prepared:false,error:'官方删除控件类型不受支持',tag:node.tagName};
  const form=node.closest('form');
  const safeUrl=value=>{if(!value)return true;try{const u=new URL(value,location.href);return u.protocol==='https:'&&(u.hostname==='douban.com'||u.hostname.endsWith('.douban.com'));}catch{return false;}};
  if(!safeUrl(node.getAttribute('href')||'')||!safeUrl(form?.getAttribute('action')||''))return {prepared:false,error:'官方删除目标地址异常'};
  window.__movieAssistantDeleteProbe={preparedAt:Date.now(),clickObserved:false,submitObserved:false,beforeUnloadObserved:false};
  if(!window.__movieAssistantDeleteProbeBound){
    document.addEventListener('click',event=>{const target=event.target?.closest?.('a,button,input');if(target&&norm(target.value||target.textContent)==='删除'&&/(^|\s)a_confirm_link(\s|$)/i.test(String(target.className||''))&&window.__movieAssistantDeleteProbe)window.__movieAssistantDeleteProbe.clickObserved=true;},true);
    document.addEventListener('submit',()=>{if(window.__movieAssistantDeleteProbe)window.__movieAssistantDeleteProbe.submitObserved=true;},true);
    addEventListener('beforeunload',()=>{if(window.__movieAssistantDeleteProbe)window.__movieAssistantDeleteProbe.beforeUnloadObserved=true;});
    window.__movieAssistantDeleteProbeBound=true;
  }
  let hrefPath='';let actionPath='';try{hrefPath=node.getAttribute('href')?new URL(node.getAttribute('href'),location.href).pathname:'';}catch{}try{actionPath=form?.getAttribute('action')?new URL(form.getAttribute('action'),location.href).pathname:'';}catch{}
  return {prepared:true,candidateCount:1,tag:node.tagName,className:String(node.className||''),hrefPath,formActionPath:actionPath,formMethod:String(form?.method||''),hasInlineHandler:!!node.getAttribute('onclick')};
})()
""";

    internal const string DoubanDeleteInvokeScript = """
(() => {
  const personal=[...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')]
    .find(x=>!x.closest('#interest_sectl,.rating_wrap'));
  const norm=v=>String(v||'').replace(/\s+/g,'').trim();
  const candidates=personal?[...personal.querySelectorAll('input,a,button')].filter(node=>
    norm(node.value||node.textContent)==='删除'&&/(^|\s)a_confirm_link(\s|$)/i.test(String(node.className||''))):[];
  if(candidates.length!==1)return {invoked:false,error:candidates.length===0?'官方删除控件缺失':'官方删除控件不唯一',candidateCount:candidates.length};
  const node=candidates[0];
  node.click();
  const probe=window.__movieAssistantDeleteProbe||{};
  return {invoked:true,clickObserved:probe.clickObserved===true,submitObserved:probe.submitObserved===true,beforeUnloadObserved:probe.beforeUnloadObserved===true};
})()
""";

    internal const string DoubanDeleteReadbackScript = """
(() => {
  const expectedSubjectId=__SUBJECT_ID__;
  const fixedProfileId=__PROFILE_ID__;
  const href=location.href||'';
  const body=document.body?.innerText||'';
  const subjectId=(href.match(/^https:\/\/movie\.douban\.com\/subject\/(\d+)\/?/)||[])[1]||'';
  const captcha=href.includes('/misc/sorry')||document.title.includes('禁止访问')||!!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image')||/异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
  const loginPage=href.includes('accounts.douban.com')||!!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
  const personal=[...document.querySelectorAll('#interest_sect_level,.interest_sect_level,#interest_sect,[class*="interest"]')].find(x=>!x.closest('#interest_sectl,.rating_wrap'));
  const norm=v=>String(v||'').replace(/\s+/g,'').trim();
  const deleteCandidates=personal?[...personal.querySelectorAll('input,a,button')].filter(node=>norm(node.value||node.textContent)==='删除'&&/(^|\s)a_confirm_link(\s|$)/i.test(String(node.className||''))):[];
  const personalText=norm(personal?.innerText||'');
  const currentStatus=/我想看|已想看|想看这部/.test(personalText)?'wish':/我在看|已在看|在看这部/.test(personalText)?'do':/我看过|已看过|看过这部/.test(personalText)?'collect':'';
  const controls=['wish','do','collect'].map(status=>document.querySelector(`[name="pbtn-${expectedSubjectId}-${status}"]`)).filter(Boolean);
  const edit=personal?.querySelector(`[name="pbtn-${expectedSubjectId}"]`);
  const statusKnown=!!personal&&(controls.length>=2||!!edit);
  const detectedStatus=currentStatus||(statusKnown&&deleteCandidates.length===0?'none':'');
  const profileLinks=[...document.querySelectorAll('#db-global-nav a[href*="/people/"],#global-nav a[href*="/people/"],.top-nav-info a[href*="/people/"]')];
  const detectedProfileId=profileLinks.map(x=>(x.href.match(/\/people\/([^/]+)\//)||[])[1]).find(Boolean)||'';
  const profileId=fixedProfileId||detectedProfileId;
  return {ready:document.readyState==='complete',href,subjectId,profileId,captcha,loginPage,statusKnown,detectedStatus,deleteControlCount:deleteCandidates.length,officialTitle:document.querySelector('h1 span[property="v:itemreviewed"],h1')?.textContent?.trim()||''};
})()
""";

    internal const string DoubanDoListDeleteProbeScript = """
(() => {
  const expectedSubjectId=__SUBJECT_ID__;
  const expectedProfileId=__PROFILE_ID__;
  const href=location.href||'';
  const body=document.body?.innerText||'';
  const route=href.match(/^https:\/\/movie\.douban\.com\/people\/([^/]+)\/do(?:[/?#]|$)/);
  const profileId=route?.[1]||'';
  const captcha=href.includes('/misc/sorry')||document.title.includes('禁止访问')||!!document.querySelector('input[name="captcha-solution"],.captcha_image,#captcha_image')||/异常请求|请输入验证码|访问过于频繁|像机器人程序|点击证明/.test(body);
  const loginPage=href.includes('accounts.douban.com')||!!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
  const items=[...document.querySelectorAll('.grid-view .item,.grid-view li.item')];
  const subjectIdOf=node=>[...node.querySelectorAll('a[href*="/subject/"]')].map(a=>(String(a.href||'').match(/\/subject\/(\d+)/)||[])[1]).find(Boolean)||'';
  const target=items.find(node=>subjectIdOf(node)===expectedSubjectId)||null;
  const norm=v=>String(v||'').replace(/\s+/g,'').trim();
  const visible=node=>{try{const style=getComputedStyle(node);return style.display!=='none'&&style.visibility!=='hidden'&&!node.disabled;}catch{return !node.disabled;}};
  const candidates=target?[...target.querySelectorAll('input,button,a')].filter(node=>visible(node)&&norm(node.value||node.textContent||node.title||node.getAttribute('aria-label'))==='删除'):[];
  const describe=node=>{const form=node.closest('form');const hrefValue=node.getAttribute('href')||'';const action=form?.getAttribute('action')||'';let hrefPath='',actionPath='';try{hrefPath=hrefValue&&!/^javascript:/i.test(hrefValue)?new URL(hrefValue,location.href).pathname:hrefValue;}catch{hrefPath='[invalid]';}try{actionPath=action?new URL(action,location.href).pathname:'';}catch{actionPath='[invalid]';}return {tag:node.tagName,className:String(node.className||''),type:String(node.type||''),hrefPath,formActionPath:actionPath,formMethod:String(form?.method||'')};};
  const shell=!!document.querySelector('.grid-view,.article');
  const noResults=/没有找到|暂无相关|没有收藏|没有看过|没有在看/.test(body);
  const hasMore=!!document.querySelector('.paginator .next a,.paginator a.next,.pagination .next a,span.next a,a.next');
  const ready=document.readyState==='complete'&&!!route&&profileId===expectedProfileId&&!captcha&&!loginPage&&(shell||items.length>0||noResults);
  return {ready,href,profileId,captcha,loginPage,found:!!target,matchedSubjectId:target?subjectIdOf(target):'',deleteCandidateCount:candidates.length,deleteCandidates:candidates.slice(0,4).map(describe),hasMore,itemCount:items.length,targetText:(target?.innerText||'').replace(/\s+/g,' ').trim().slice(0,300)};
})()
""";

    internal const string DoubanDoListDeletePrepareScript = """
(() => {
  const expectedSubjectId=__SUBJECT_ID__;
  const expectedProfileId=__PROFILE_ID__;
  const href=location.href||'';
  const route=href.match(/^https:\/\/movie\.douban\.com\/people\/([^/]+)\/do(?:[/?#]|$)/);
  if(!route||route[1]!==expectedProfileId)return {prepared:false,error:'当前页面不是目标用户的豆瓣在看列表'};
  const items=[...document.querySelectorAll('.grid-view .item,.grid-view li.item')];
  const subjectIdOf=node=>[...node.querySelectorAll('a[href*="/subject/"]')].map(a=>(String(a.href||'').match(/\/subject\/(\d+)/)||[])[1]).find(Boolean)||'';
  const target=items.find(node=>subjectIdOf(node)===expectedSubjectId)||null;
  if(!target)return {prepared:false,error:'在看列表目标影片不存在'};
  const norm=v=>String(v||'').replace(/\s+/g,'').trim();
  const visible=node=>{try{const style=getComputedStyle(node);return style.display!=='none'&&style.visibility!=='hidden'&&!node.disabled;}catch{return !node.disabled;}};
  const candidates=[...target.querySelectorAll('input,button,a')].filter(node=>visible(node)&&norm(node.value||node.textContent||node.title||node.getAttribute('aria-label'))==='删除');
  if(candidates.length!==1)return {prepared:false,error:candidates.length===0?'在看列表官方删除控件缺失':'在看列表官方删除控件不唯一',candidateCount:candidates.length};
  const node=candidates[0];
  if(!['A','BUTTON','INPUT'].includes(node.tagName))return {prepared:false,error:'在看列表官方删除控件类型不受支持',tag:node.tagName};
  const form=node.closest('form');
  const safeRef=value=>{if(!value||value==='#'||/^javascript:\s*(?:void\(0\)|;)?\s*$/i.test(value))return true;try{const u=new URL(value,location.href);return u.protocol==='https:'&&(u.hostname==='douban.com'||u.hostname.endsWith('.douban.com'));}catch{return false;}};
  if(!safeRef(node.getAttribute('href')||'')||!safeRef(form?.getAttribute('action')||''))return {prepared:false,error:'在看列表官方删除目标地址异常'};
  window.__movieAssistantDeleteProbe={preparedAt:Date.now(),route:'PersonalDoList',subjectId:expectedSubjectId,clickObserved:false,submitObserved:false,beforeUnloadObserved:false};
  if(!window.__movieAssistantDeleteProbeBound){
    document.addEventListener('click',event=>{const targetNode=event.target?.closest?.('a,button,input');if(targetNode&&window.__movieAssistantDeleteProbe)window.__movieAssistantDeleteProbe.clickObserved=true;},true);
    document.addEventListener('submit',()=>{if(window.__movieAssistantDeleteProbe)window.__movieAssistantDeleteProbe.submitObserved=true;},true);
    addEventListener('beforeunload',()=>{if(window.__movieAssistantDeleteProbe)window.__movieAssistantDeleteProbe.beforeUnloadObserved=true;});
    window.__movieAssistantDeleteProbeBound=true;
  }
  let hrefPath='',actionPath='';const hrefValue=node.getAttribute('href')||'',action=form?.getAttribute('action')||'';try{hrefPath=hrefValue&&!/^javascript:/i.test(hrefValue)?new URL(hrefValue,location.href).pathname:hrefValue;}catch{}try{actionPath=action?new URL(action,location.href).pathname:'';}catch{}
  return {prepared:true,route:'PersonalDoList',candidateCount:1,matchedSubjectId:expectedSubjectId,tag:node.tagName,className:String(node.className||''),hrefPath,formActionPath:actionPath,formMethod:String(form?.method||''),hasInlineHandler:!!node.getAttribute('onclick')};
})()
""";

    internal const string DoubanDoListDeleteHitTestScript = """
(() => {
  const expectedSubjectId=__SUBJECT_ID__;
  const expectedProfileId=__PROFILE_ID__;
  const href=location.href||'';
  const route=href.match(/^https:\/\/movie\.douban\.com\/people\/([^/]+)\/do(?:[/?#]|$)/);
  if(!route||route[1]!==expectedProfileId)return {ready:false,invoked:false,error:'当前页面不是目标用户的豆瓣在看列表'};
  const items=[...document.querySelectorAll('.grid-view .item,.grid-view li.item')];
  const subjectIdOf=node=>[...node.querySelectorAll('a[href*="/subject/"]')].map(a=>(String(a.href||'').match(/\/subject\/(\d+)/)||[])[1]).find(Boolean)||'';
  const target=items.find(node=>subjectIdOf(node)===expectedSubjectId)||null;
  if(!target)return {ready:false,invoked:false,error:'在看列表目标影片不存在'};
  const norm=v=>String(v||'').replace(/\s+/g,'').trim();
  const visible=node=>{try{const style=getComputedStyle(node);const r=node.getBoundingClientRect();return style.display!=='none'&&style.visibility!=='hidden'&&!node.disabled&&r.width>0&&r.height>0;}catch{return !node.disabled;}};
  const candidates=[...target.querySelectorAll('input,button,a')].filter(node=>visible(node)&&norm(node.value||node.textContent||node.title||node.getAttribute('aria-label'))==='删除');
  if(candidates.length!==1)return {ready:false,invoked:false,error:candidates.length===0?'在看列表官方删除控件缺失':'在看列表官方删除控件不唯一',candidateCount:candidates.length};
  const node=candidates[0];
  node.scrollIntoView({block:'center',inline:'center',behavior:'auto'});
  const r=node.getBoundingClientRect();
  const centerX=r.left+r.width/2;
  const centerY=r.top+r.height/2;
  const atPoint=document.elementFromPoint(centerX,centerY);
  const hitMatches=!!atPoint&&(atPoint===node||node.contains(atPoint)||atPoint.contains(node));
  const form=node.closest('form');
  const hrefValue=node.getAttribute('href')||'',action=form?.getAttribute('action')||'';
  let hrefPath='',actionPath='';
  try{hrefPath=hrefValue&&!/^javascript:/i.test(hrefValue)?new URL(hrefValue,location.href).pathname:hrefValue;}catch{hrefPath='[invalid]';}
  try{actionPath=action?new URL(action,location.href).pathname:'';}catch{actionPath='[invalid]';}
  if(!hitMatches)return {ready:false,invoked:false,error:'删除按钮中心点被其他页面元素遮挡',tag:node.tagName,className:String(node.className||''),centerX,centerY,hitTag:atPoint?.tagName||'',hitClass:String(atPoint?.className||'')};
  return {ready:true,invoked:false,matchedSubjectId:expectedSubjectId,tag:node.tagName,className:String(node.className||''),type:String(node.type||''),centerX,centerY,width:r.width,height:r.height,hrefPath,formActionPath:actionPath,formMethod:String(form?.method||''),hasInlineHandler:!!node.getAttribute('onclick')};
})()
""";


    internal const string DoubanDeleteHistoryProbeScript = """
(() => {
  const id=__SUBJECT_ID__;
  const href=location.href||'';
  const body=document.body?.innerText||'';
  const captcha=href.includes('/misc/sorry')||/验证码|访问过于频繁|像机器人程序/.test(body);
  const loginPage=href.includes('accounts.douban.com')||!!document.querySelector('form[action*="accounts/login"],form[action*="/passport/login"]');
  const links=[...document.querySelectorAll('a[href*="/subject/"]')];
  const contains=links.some(x=>(x.href.match(/\/subject\/(\d+)/)||[])[1]===id);
  const next=!!document.querySelector('.paginator .next a,span.next a,a.next');
  return {ready:document.readyState==='complete'&&!captcha&&!loginPage,contains,hasMore:next,captcha,loginPage};
})()
""";
}
