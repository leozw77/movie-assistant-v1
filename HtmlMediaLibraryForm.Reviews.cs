using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private static object SessionStatusDto(DoubanSessionStatus status) => new { state = status.State, text = status.Text, profileId = status.ProfileId, verifiedAt = status.VerifiedAt, error = status.Error, loggedIn = status.IsLoggedIn };

    internal void ShowDoubanLogin()
    {
        if (_closing) return;
        _doubanAccountBar.Visible = false;
        _doubanLoginPanel.Visible = true;
        _doubanLoginPanel.BringToFront();
        _doubanLoginStatus.Text = "正在打开豆瓣扫码登录…";
        _doubanLoginVerifyButton.Enabled = false;
        _ = OpenInlineDoubanLoginAsync();
    }

    private async Task OpenInlineDoubanLoginAsync()
    {
        try
        {
            await EnsureDoubanLoginViewAsync().ConfigureAwait(true);
            if (_closing || !_doubanLoginPanel.Visible) return;
            _workerConnector.SetLoginWindowActive(true);
            _doubanLoginVerifyButton.Enabled = true;
            _doubanLoginStatus.Text = "请使用豆瓣 App 扫码，完成后点击“验证登录”。";
            _doubanLoginView.CoreWebView2!.Navigate("https://accounts.douban.com/passport/login?source=movie");
            DiagnosticLogger.Write("WebView=DoubanLoginInline; LoginPanelOpened=True; PopupForm=False");
        }
        catch (Exception ex)
        {
            _doubanLoginVerifyButton.Enabled = false;
            _doubanLoginStatus.Text = "扫码页面打开失败：" + ex.Message;
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; LoginPanelOpenFailed=True; Error={ex}");
        }
    }

    private async Task VerifyInlineDoubanLoginAsync()
    {
        if (_closing || !_doubanLoginPanel.Visible) return;
        _doubanLoginVerifyButton.Enabled = false;
        _doubanLoginStatus.Text = "正在验证豆瓣登录状态，请稍候…";
        try
        {
            var status = await _workerConnector.VerifySessionAsync().ConfigureAwait(true);
            if (!status.IsLoggedIn)
            {
                _doubanLoginStatus.Text = string.IsNullOrWhiteSpace(status.Error)
                    ? "尚未确认登录，请扫码后再试。"
                    : status.Text + "，请扫码后再试。";
                _doubanLoginVerifyButton.Enabled = true;
                return;
            }

            _doubanLoginStatus.Text = "豆瓣登录已确认，正在返回主界面…";
            HideInlineDoubanLogin();
            await NavigateInitialDoubanPageAsync(status).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _doubanLoginStatus.Text = "登录验证失败：" + ex.Message;
            _doubanLoginVerifyButton.Enabled = true;
            DiagnosticLogger.Write($"WebView=DoubanLoginInline; LoginVerifyFailed=True; Error={ex}");
        }
    }

    private void HideInlineDoubanLogin()
    {
        _doubanLoginPanel.Visible = false;
        _doubanLoginVerifyButton.Enabled = true;
        _doubanLoginStatus.Text = "请使用豆瓣 App 扫码，完成后点击“验证登录”。";
        _workerConnector.SetLoginWindowActive(false);
        if (!_closing)
        {
            _doubanAccountBar.Visible = true;
            _doubanAccountBar.BringToFront();
            DiagnosticLogger.Write("WebView=DoubanLoginInline; LoginPanelClosed=True; PopupForm=False");
        }
    }

    private void BringDoubanAccountBarToFront()
    {
        if (_closing || _doubanLoginPanel.Visible) return;
        _doubanAccountBar.Visible = true;
        _doubanAccountBar.BringToFront();
    }

    private object CardDto(DoubanHistoryRecord record) => new
    {
        subjectId = record.SubjectId, subjectUrl = record.SubjectUrl, title = string.IsNullOrWhiteSpace(record.Title) ? $"豆瓣条目 {record.SubjectId}" : record.Title, posterUrl = record.PosterUrl,
        subtitle = record.Status switch { "collect" => "豆瓣 · 看过", "wish" => "豆瓣 · 想看", "do" => "豆瓣 · 在看", _ => "豆瓣" },
        meta = string.Join(" · ", new[] { record.MarkedDate, record.Tags, record.Comment }.Where(x => !string.IsNullOrWhiteSpace(x))),
        score = record.DoubanScore, myRating = record.Rating, statusOptions = record.DoubanStatusOptions.Select(StatusDto).ToList()
    };

    private object SearchCandidateDto(DoubanSearchCandidate item) => new { subjectId = item.SubjectId, subjectUrl = item.SubjectUrl, posterUrl = item.PosterUrl, visibleText = item.VisibleText, statusOptions = (item.StatusOptions ?? []).Select(StatusDto).ToList() };

    private object DetailDto(DoubanHistoryRecord record, string connectorSource) => new
    {
        subjectId = record.SubjectId, subjectUrl = record.SubjectUrl, title = string.IsNullOrWhiteSpace(record.Title) ? $"豆瓣条目 {record.SubjectId}" : record.Title,
        year = record.Year, genres = record.Genres, directors = record.Directors, imdbId = record.ImdbId, runtime = record.Runtime, countries = record.Countries,
        summary = record.Summary, doubanScore = record.DoubanScore, rating = record.Rating, posterUrl = record.PosterUrl, markedDate = record.MarkedDate, tags = record.Tags, comment = record.Comment, tombstoned = record.Tombstoned,
        statusOptions = record.DoubanStatusOptions.Select(StatusDto).ToList(),
        statusCapabilitiesKnown = record.DoubanStatusCapabilitiesKnown,
        statusCapabilitySource = record.DoubanStatusCapabilitySource,
        statusCapabilityError = record.DoubanStatusCapabilityError,
        connectorSource
    };

    private object WriteEnvelopeV2(DoubanHistoryRecord record, ReviewWriteResultV2 result, string connectorSource, string jobId = "", string requestId = "")
    {
        var phase = result.Phase switch
        {
            ReviewWritePhase.Confirmed => "confirmed",
            ReviewWritePhase.NoChange => "no-change",
            ReviewWritePhase.Blocked => "blocked",
            ReviewWritePhase.Uncertain => "unconfirmed",
            _ => "failed"
        };
        var official = SelectAuthoritativeSnapshot(result) ?? result.Official ?? result.Before;
        return new
        {
            detail = DetailDto(record, connectorSource),
            write = new
            {
                phase,
                stage = result.Stage,
                operation = "save",
                requested = new
                {
                    status = result.Requested.Status,
                    ratingAction = FieldActionText(result.Requested.RatingAction),
                    rating = result.Requested.Rating,
                    commentAction = FieldActionText(result.Requested.CommentAction),
                    comment = result.Requested.Comment
                },
                official = new
                {
                    existsKnown = official?.ExistsKnown == true,
                    exists = official?.ExistsKnown == true ? official.Exists : (bool?)null,
                    statusKnown = official?.StatusKnown == true,
                    status = official?.Status,
                    ratingKnown = official?.RatingKnown == true,
                    rating = official?.Rating,
                    commentKnown = official?.CommentKnown == true,
                    comment = official?.Comment,
                    markedDateKnown = official?.MarkedDateKnown == true,
                    markedDate = official?.MarkedDate,
                    title = official?.OfficialTitle ?? "",
                    subjectId = official?.OfficialSubjectId ?? "",
                    source = official?.Source ?? ""
                },
                settled = result.Settled,
                submitted = result.Submitted,
                noChange = result.NoChange,
                changed = result.Changed,
                submitEventObserved = result.SubmitEventObserved,
                officialConfirmed = result.OfficialConfirmed,
                localUpdated = result.LocalUpdated,
                cacheUpdate = result.OfficialConfirmed
                    ? (result.LocalUpdated ? "completed" : "deferred")
                    : (result.LocalUpdated ? "synchronized" : "not-confirmed"),
                webView = "Worker",
                jobId,
                requestId,
                error = result.Error ?? ""
            }
        };
    }

    private static OfficialReviewSnapshot? SelectAuthoritativeSnapshot(ReviewWriteResultV2 result)
    {
        if (IsCompleteOfficialSnapshot(result.Official)) return result.Official;
        if (IsCompleteOfficialSnapshot(result.Before)) return result.Before;
        return null;
    }

    private static bool IsCompleteOfficialSnapshot(OfficialReviewSnapshot? snapshot) =>
        snapshot is not null && snapshot.ExistsKnown &&
        (!snapshot.Exists || (snapshot.StatusKnown && snapshot.RatingKnown && snapshot.CommentKnown));

    private bool ApplyAuthoritativeReview(DoubanHistoryRecord record, OfficialReviewSnapshot official)
    {
        if (!IsCompleteOfficialSnapshot(official)) return false;
        if (!string.IsNullOrWhiteSpace(official.OfficialSubjectId) && !string.Equals(official.OfficialSubjectId, record.SubjectId, StringComparison.Ordinal))
            throw new InvalidDataException("豆瓣官方页面影片 ID 与本地记录不一致，已阻止缓存更新。");

        if (!official.Exists)
        {
            var wasHistoryRecord = _history.Items.ContainsKey(record.SubjectId);
            record.Rating = null;
            record.Comment = "";
            record.Tags = "";
            record.MarkedDate = "";
            record.DoubanStatusOptions = [];
            record.DoubanStatusCapabilitiesKnown = official.CapabilitiesKnown;
            record.DoubanStatusCapabilitySource = official.Source;
            record.DoubanStatusCapabilityError = official.Error ?? "";

            if (wasHistoryRecord || record.Tombstoned)
            {
                record.Status = "deleted";
                record.Tombstoned = true;
                record.TombstonedAt ??= DateTime.Now;
                if (string.IsNullOrWhiteSpace(record.TombstoneReason)) record.TombstoneReason = "豆瓣官方已无评价同步";
                _history.Items[record.SubjectId] = record;
                _searchCache.Items.Remove(record.SubjectId);
            }
            else
            {
                record.Status = "search";
                _searchCache.Items[record.SubjectId] = record;
            }
            DiagnosticLogger.Write($"HTML authoritative cache overwrite; SubjectId={record.SubjectId}; Exists=false; WasHistory={wasHistoryRecord}; Tombstoned={record.Tombstoned}; Source={official.Source}");
            return true;
        }

        var status = ReviewTargetResolver.NormalizeStatus(official.Status);
        if (status is not ("wish" or "do" or "collect")) return false;

        var previousMarkedDate = record.MarkedDate;
        if (official.MarkedDateKnown && !string.IsNullOrWhiteSpace(official.MarkedDate))
            record.MarkedDate = official.MarkedDate.Trim();
        record.Status = status;
        record.Rating = official.Rating;
        record.Comment = ReviewTargetResolver.NormalizeComment(official.Comment);
        record.Tombstoned = false;
        record.TombstonedAt = null;
        record.TombstoneReason = "";
        record.DoubanStatusCapabilitiesKnown = official.CapabilitiesKnown;
        record.DoubanStatusCapabilitySource = official.Source;
        record.DoubanStatusCapabilityError = official.Error ?? "";

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wish"] = "想看", ["do"] = "在看", ["collect"] = "看过"
        };
        var supported = official.SupportedStatuses
            .Select(ReviewTargetResolver.NormalizeStatus)
            .Where(labels.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!supported.Contains(status, StringComparer.OrdinalIgnoreCase)) supported.Add(status);
        record.DoubanStatusOptions = supported
            .Select(value => new DoubanStatusOption(labels[value], string.Equals(value, status, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PromoteToHistory(record);
        DiagnosticLogger.Write($"HTML authoritative cache overwrite; SubjectId={record.SubjectId}; Exists=true; Status={status}; Rating={official.Rating?.ToString() ?? "null"}; PreviousMarkedDate={previousMarkedDate}; OfficialMarkedDate={official.MarkedDate}; MarkedDateKnown={official.MarkedDateKnown}; StoredMarkedDate={record.MarkedDate}; CommentLength={record.Comment.Length}; Source={official.Source}");
        return true;
    }

    private object DeleteEnvelope(DoubanHistoryRecord record, DeleteEntryResult result, string connectorSource, string jobId, string requestId)
    {
        var phase = result.Phase switch
        {
            DeleteWritePhase.Confirmed => "confirmed",
            DeleteWritePhase.NoChange => "no-change",
            DeleteWritePhase.Blocked => "blocked",
            DeleteWritePhase.Uncertain => "unconfirmed",
            _ => "failed"
        };
        var official = result.Official ?? result.Before;
        return new
        {
            detail = DetailDto(record, connectorSource),
            write = new
            {
                phase,
                stage = result.Stage,
                operation = "delete",
                deleteRoute = result.Route,
                requested = new { delete = true },
                official = new
                {
                    existsKnown = official?.ExistsKnown == true,
                    exists = official?.ExistsKnown == true ? official.Exists : (bool?)null,
                    statusKnown = official?.StatusKnown == true,
                    status = official?.Status,
                    ratingKnown = official?.RatingKnown == true,
                    rating = official?.Rating,
                    commentKnown = official?.CommentKnown == true,
                    comment = official?.Comment,
                    markedDateKnown = official?.MarkedDateKnown == true,
                    markedDate = official?.MarkedDate,
                    title = official?.OfficialTitle ?? "",
                    subjectId = official?.OfficialSubjectId ?? "",
                    source = official?.Source ?? ""
                },
                settled = result.Settled,
                submitted = result.Submitted,
                noChange = result.NoChange,
                changed = result.Submitted,
                submitEventObserved = result.Submitted,
                officialConfirmed = result.OfficialConfirmed,
                localUpdated = result.LocalUpdated,
                cacheUpdate = result.LocalUpdated ? "completed" : (result.OfficialConfirmed ? "deferred" : "not-confirmed"),
                listChecks = result.ListChecks.Select(check => new
                {
                    status = check.Status,
                    ready = check.Ready,
                    contains = check.Contains,
                    pagesScanned = check.PagesScanned,
                    hasMore = check.HasMore,
                    scope = check.Scope,
                    error = check.Error
                }).ToList(),
                webView = "Worker",
                jobId,
                requestId,
                error = result.Error ?? ""
            }
        };
    }

    private bool ApplyConfirmedDeletion(DoubanHistoryRecord record, string reason)
    {
        record.Status = "deleted";
        record.Rating = null;
        record.Comment = "";
        record.Tags = "";
        record.MarkedDate = "";
        record.DoubanStatusOptions = [];
        record.DoubanStatusCapabilitiesKnown = false;
        record.DoubanStatusCapabilitySource = "delete-readback";
        record.DoubanStatusCapabilityError = "";
        record.Tombstoned = true;
        record.TombstonedAt = DateTime.Now;
        record.TombstoneReason = reason;
        _history.Items[record.SubjectId] = record;
        _searchCache.Items.Remove(record.SubjectId);
        DiagnosticLogger.Write($"HTML delete tombstone applied; SubjectId={record.SubjectId}; TombstonedAt={record.TombstonedAt:O}; Reason={reason}; DetailMetadataPreserved={record.DetailMetadataFetched}");
        return true;
    }

    private static string FieldActionText(ReviewFieldAction action) => action switch
    {
        ReviewFieldAction.Keep => "keep",
        ReviewFieldAction.Set => "set",
        ReviewFieldAction.Clear => "clear",
        _ => ""
    };

    private object WriteEnvelope(DoubanHistoryRecord record, DoubanWriteResult result, string connectorSource, bool localUpdated)
    {
        var phase = !string.IsNullOrWhiteSpace(result.Phase)
            ? result.Phase
            : result.Success ? "confirmed" : (result.Error.Contains("回读", StringComparison.Ordinal) || result.Error.Contains("快照", StringComparison.Ordinal) ? "unconfirmed" : "failed");
        var requestedStatus = string.IsNullOrWhiteSpace(result.RequestedStatus)
            ? result.Action == "status" ? result.Status : ""
            : result.RequestedStatus;
        var requestedRating = result.RequestedRating ?? (result.Action == "rating" ? result.Rating : null);
        var requestedCommentLength = result.RequestedComment
            ? result.RequestedCommentLength
            : result.Action == "review" ? result.Review.Length : (int?)null;
        return new
        {
            detail = DetailDto(record, connectorSource),
            write = new
            {
                phase,
                stage = string.IsNullOrWhiteSpace(result.Stage) ? (result.Success ? "readback" : "") : result.Stage,
                operation = result.Action,
                requested = new { status = requestedStatus, rating = requestedRating, commentLength = requestedCommentLength },
                official = new { status = result.Status, rating = result.Rating, comment = result.Review },
                settled = result.Settled,
                localUpdated = localUpdated || result.LocalUpdated,
                error = result.Error
            }
        };
    }

    private static void ApplyConfirmedDoubanEntry(DoubanHistoryRecord record, DoubanWriteResult result, DoubanEntryWriteRequest request)
    {
        var status = result.Status is "wish" or "do" or "collect" ? result.Status : request.Status;
        record.Status = status;
        record.Tombstoned = false;
        record.TombstonedAt = null;
        record.TombstoneReason = "";

        record.Rating = status == "wish" ? null : result.Rating;
        if (request.SetComment) record.Comment = result.Review;
        else if (!string.IsNullOrWhiteSpace(result.Review)) record.Comment = result.Review;

        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wish"] = "想看",
            ["do"] = "在看",
            ["collect"] = "看过"
        };
        var options = (result.StatusOptions is { Count: > 0 } ? result.StatusOptions : record.DoubanStatusOptions)
            .Where(option => labels.Values.Contains(option.Text, StringComparer.Ordinal))
            .ToList();
        if (!options.Any(option => string.Equals(option.Text, labels[status], StringComparison.Ordinal)))
            options.Add(new DoubanStatusOption(labels[status], false));
        record.DoubanStatusOptions = options
            .Select(option => option with { Selected = string.Equals(option.Text, labels[status], StringComparison.Ordinal) })
            .ToList();
        if (result.StatusOptions is { Count: > 0 })
        {
            record.DoubanStatusCapabilitiesKnown = true;
            record.DoubanStatusCapabilitySource = "confirmed-write-form";
            record.DoubanStatusCapabilityError = "";
        }
    }

    private static object StatusDto(DoubanStatusOption status) => new { text = status.Text, selected = status.Selected };

    private DoubanHistoryRecord FindOrCreateRecord(string subjectId, string subjectUrl)
    {
        DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML FindOrCreate request");
        if (_history.Items.TryGetValue(subjectId, out var historyRecord))
        {
            DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML FindOrCreate history", historyRecord);
            return historyRecord;
        }
        if (!_searchCache.Items.TryGetValue(subjectId, out var record))
        {
            record = new DoubanHistoryRecord { SubjectId = subjectId, SubjectUrl = subjectUrl, Status = "search", ImportedAt = DateTime.Now };
            _searchCache.Items[subjectId] = record;
        }
        DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "HTML FindOrCreate search-cache", record);
        record.SubjectId = subjectId;
        record.SubjectUrl = subjectUrl;
        return record;
    }

    private static void ApplyMetadata(DoubanHistoryRecord record, DoubanSubjectMetadataResult metadata)
    {
        record.Title = metadata.Title.Trim();
        if (metadata.Score is not null) record.DoubanScore = metadata.Score;
        Copy(metadata.Poster, value => record.PosterUrl = value); Copy(metadata.Year, value => record.Year = value); Copy(metadata.Genres, value => record.Genres = value);
        Copy(metadata.Directors, value => record.Directors = value); Copy(metadata.Runtime, value => record.Runtime = value);
        Copy(metadata.Countries, value => record.Countries = value); Copy(metadata.ImdbId, value => record.ImdbId = value); Copy(metadata.Summary, value => record.Summary = value);
        if (metadata.StatusCapabilitiesKnown)
        {
            if (metadata.StatusOptions.Count > 0) record.DoubanStatusOptions = metadata.StatusOptions;
            record.DoubanStatusCapabilitiesKnown = true;
            record.DoubanStatusCapabilitySource = metadata.StatusCapabilitySource;
            record.DoubanStatusCapabilityError = metadata.StatusCapabilityError;
        }
        else if (record.DoubanStatusOptions.Count == 0 && metadata.StatusOptions.Count > 0)
        {
            // Detail-page chips are useful as a display fallback, but must not downgrade a previously
            // confirmed official-form capability snapshot.
            record.DoubanStatusOptions = metadata.StatusOptions;
        }
        record.DetailMetadataFetched = DoubanMediaParser.HasCompleteDetailMetadata(record);
        record.FullDetailsFetchedAt = DateTime.Now;
        record.FullDetailsLastError = "";
    }

}
