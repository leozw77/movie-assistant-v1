using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed record FrodoPersonalFilterCriteria(
    string ContentType = "",
    bool PlayableOnly = false,
    double? ScoreMin = null,
    double? ScoreMax = null,
    int? MyRating = null,
    bool Unrated = false,
    string Period = "",
    string Genre = "",
    string Country = "",
    string Sort = "marked-desc");

internal sealed record FrodoPersonalIndexProgress(
    string ProfileId,
    string Status,
    int RequestedStart,
    int Loaded,
    int Total);

internal sealed record FrodoPersonalIndexStatus(
    string Status,
    bool Complete,
    int Total,
    DateTimeOffset BuiltAtUtc,
    List<FrodoPersonalItem> Items,
    List<string> Years,
    List<string> Genres,
    List<string> Countries);

internal sealed record FrodoPersonalIndexCache(
    int SchemaVersion,
    string ProfileId,
    DateTimeOffset UpdatedAtUtc,
    Dictionary<string, FrodoPersonalIndexStatus> Statuses);

internal sealed class FrodoPersonalIndexService
{
    private const int SchemaVersion = 4;
    private const int MaxRequestsPerStatus = 20_000;
    private const int FullSnapshotPageSize = 100;
    private const int MaxBoundedReconcilePages = 5;
    internal static int BoundedReconcilePageLimit => MaxBoundedReconcilePages;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly FrodoClient _client;
    private readonly FrodoOptions _options;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly object _maintenanceGate = new();
    private readonly HashSet<string> _headSyncRunning = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletionReconcileRunning = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrodoPersonalIndexStatus> _statuses = new(StringComparer.Ordinal);
    private string _profileId = "";

    internal FrodoPersonalIndexService(FrodoOptions options, string dataDirectory)
    {
        _options = options;
        _client = new FrodoClient(options);
        _cachePath = Path.Combine(dataDirectory, "frodo-personal-index-v1.json");
    }

    internal string CurrentProfileId
    {
        get { lock (_stateGate) return _profileId; }
    }

    internal bool TryGetStatus(string profileId, string status, out FrodoPersonalIndexStatus snapshot)
    {
        lock (_stateGate)
        {
            snapshot = null!;
            return profileId.Equals(_profileId, StringComparison.Ordinal) &&
                   _statuses.TryGetValue(status, out snapshot!) &&
                   snapshot.Complete;
        }
    }

    internal bool TryGetCachedPublicRating(
        string profileId,
        string subjectId,
        out double? score,
        out int? ratingCount)
    {
        score = null;
        ratingCount = null;
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(subjectId)) return false;

        lock (_stateGate)
        {
            if (!_profileId.Equals(profileId, StringComparison.Ordinal)) return false;

            foreach (var snapshot in _statuses.Values)
            {
                foreach (var item in snapshot.Items)
                {
                    if (!item.SubjectId.Equals(subjectId, StringComparison.Ordinal)) continue;
                    if (score is null or <= 0 && item.Score is > 0) score = item.Score;
                    if (ratingCount is null or <= 0 && item.RatingCount is > 0) ratingCount = item.RatingCount;
                    if (score is > 0 && ratingCount is > 0) return true;
                }
            }
        }

        return score is > 0 || ratingCount is > 0;
    }
    internal async Task LoadCacheAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit)) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool HasUsableInMemoryStore()
            {
                lock (_stateGate)
                {
                    return _profileId.Equals(profileId, StringComparison.Ordinal) &&
                           _statuses.Values.Any(snapshot => snapshot.Complete);
                }
            }

            if (!File.Exists(_cachePath))
            {
                if (!HasUsableInMemoryStore()) ResetProfile(profileId);
                else DiagnosticLogger.Write($"Frodo personal cache missing; ProfileId={profileId}; StorePreserved=True");
                return;
            }

            FrodoPersonalIndexCache? cache;
            await using (var stream = File.OpenRead(_cachePath))
                cache = await JsonSerializer.DeserializeAsync<FrodoPersonalIndexCache>(stream, Json, cancellationToken).ConfigureAwait(false);

            if (cache is null || cache.SchemaVersion != SchemaVersion ||
                !cache.ProfileId.Equals(profileId, StringComparison.Ordinal))
            {
                if (!HasUsableInMemoryStore()) ResetProfile(profileId);
                else DiagnosticLogger.Write($"Frodo personal cache rejected; ProfileId={profileId}; StorePreserved=True");
                return;
            }

            int statusCount;
            int itemCount;
            lock (_stateGate)
            {
                _profileId = profileId;
                _statuses.Clear();
                foreach (var pair in cache.Statuses)
                {
                    if (pair.Key is not ("collect" or "wish" or "do") || !pair.Value.Complete) continue;
                    _statuses[pair.Key] = pair.Value;
                }
                statusCount = _statuses.Count;
                itemCount = _statuses.Values.Sum(value => value.Items.Count);
            }

            DiagnosticLogger.Write($"Frodo personal index cache loaded; ProfileId={profileId}; Statuses={statusCount}; Items={itemCount}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var preserved = false;
            lock (_stateGate)
            {
                preserved = _profileId.Equals(profileId, StringComparison.Ordinal) &&
                            _statuses.Values.Any(snapshot => snapshot.Complete);
            }
            if (!preserved) ResetProfile(profileId);
            DiagnosticLogger.Write($"Frodo personal index cache load failed; ProfileId={profileId}; StorePreserved={preserved}; Error={ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }
    internal Task<FrodoPersonalIndexStatus> BootstrapStatusAsync(
        string profileId,
        string status,
        IProgress<FrodoPersonalIndexProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildStatusCoreAsync(profileId, status, "bootstrap", progress, cancellationToken);

    internal Task<FrodoPersonalIndexStatus> ForceFullReconcileAsync(
        string profileId,
        string status,
        IProgress<FrodoPersonalIndexProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildStatusCoreAsync(profileId, status, "force-full", progress, cancellationToken);

    private async Task<FrodoPersonalIndexStatus> BuildStatusCoreAsync(
        string profileId,
        string status,
        string buildReason,
        IProgress<FrodoPersonalIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit))
            throw new InvalidDataException("Frodo 索引用户 ID 无效。");
        if (status is not ("collect" or "wish" or "do"))
            throw new InvalidDataException("Frodo 索引个人状态无效。");

        FrodoPersonalIndexStatus? beforeSnapshot;
        lock (_stateGate)
        {
            if (!_profileId.Equals(profileId, StringComparison.Ordinal))
            {
                _profileId = profileId;
                _statuses.Clear();
            }
            _statuses.TryGetValue(status, out beforeSnapshot);
        }
        var beforeBuiltAt = beforeSnapshot?.BuiltAtUtc;

        var items = new List<FrodoPersonalItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nextStart = 0;
        var total = 0;
        int? stableCloudTotal = null;
        var requestCount = 0;
        var complete = false;

        while (requestCount < MaxRequestsPerStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestCount++;
            var requestedStart = nextStart;
            var raw = await _client.GetInterestsAsync(profileId, status, requestedStart, FullSnapshotPageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, status);
            if (page.Skipped.Count > 0)
                throw new InvalidDataException($"Frodo 完整快照存在无法映射记录；Status={status}; Start={requestedStart}; Skipped={page.Skipped.Count}。");
            if (stableCloudTotal is null) stableCloudTotal = page.Total;
            else if (page.Total != stableCloudTotal.Value)
                throw new InvalidDataException($"Frodo 完整快照扫描期间 total 发生变化；Status={status}; Before={stableCloudTotal.Value}; Now={page.Total}。");

            var beforePageUnique = items.Count;
            foreach (var item in page.Items)
            {
                if (seen.Add(item.SubjectId)) items.Add(item);
            }
            var addedUnique = items.Count - beforePageUnique;

            total = Math.Max(stableCloudTotal.Value, items.Count);
            progress?.Report(new FrodoPersonalIndexProgress(profileId, status, requestedStart, items.Count, total));

            // IMPORTANT: the Frodo response can advertise count=50 while the
            // actual interests payload contains only 46/48/49 records.
            // Advancing by the advertised Count creates permanent holes.
            // Full-cache construction must advance by the payload actually
            // received. Visible/provider pagination keeps its old fixed slots.
            var responseStart = Math.Max(page.Start, requestedStart);
            var cursorAdvance = page.RawCount;
            if (cursorAdvance <= 0)
            {
                if (page.Total <= 0 && items.Count == 0)
                {
                    complete = true;
                    DiagnosticLogger.Write(
                        $"Frodo personal full snapshot empty complete; Reason={buildReason}; Status={status}; RequestedStart={requestedStart}; Total={page.Total}");
                    break;
                }

                throw new InvalidDataException(
                    $"Frodo 完整快照在到达 total 前返回空页；Status={status}; Start={requestedStart}; Loaded={items.Count}; Total={total}。旧缓存保持不变。");
            }

            nextStart = checked(responseStart + cursorAdvance);
            var shortPayload = page.Count > 0 && page.RawCount < page.Count;

            DiagnosticLogger.Write(
                $"Frodo personal full snapshot page; Reason={buildReason}; Status={status}; RequestedStart={requestedStart}; ResponseStart={page.Start}; ApiCount={page.Count}; Raw={page.RawCount}; Mapped={page.Items.Count}; AddedUnique={addedUnique}; Skipped={page.Skipped.Count}; Loaded={items.Count}; Total={total}; CursorAdvance={cursorAdvance}; NextStart={nextStart}; ShortPayload={shortPayload}");

            if (shortPayload)
            {
                DiagnosticLogger.Write(
                    $"Frodo personal full snapshot gap recovery; Status={status}; RequestedStart={requestedStart}; ApiCount={page.Count}; Raw={page.RawCount}; RecoveryNextStart={nextStart}");
            }

            // A snapshot is complete only when the number of UNIQUE mapped
            // subjects reaches the stable cloud total. Cursor position alone
            // is never sufficient evidence of completeness.
            if (total > 0 && items.Count == total)
            {
                complete = true;
                break;
            }

            if (total > 0 && items.Count > total)
                throw new InvalidDataException(
                    $"Frodo 完整快照唯一条目数超过 total；Status={status}; Loaded={items.Count}; Total={total}。拒绝提交。");

            if (nextStart >= total && items.Count < total)
                throw new InvalidDataException(
                    $"Frodo 完整快照游标到达末尾但仍缺条目；Status={status}; Loaded={items.Count}; Total={total}; Missing={total - items.Count}。拒绝提交旧缓存保持不变。");
        }

        if (!complete)
            throw new InvalidDataException("Frodo 个人库完整快照请求次数超过保护上限。");

        var expectedTotal = stableCloudTotal ?? 0;
        if (items.Count != expectedTotal)
            throw new InvalidDataException(
                $"Frodo 完整快照完整性校验失败；Status={status}; Loaded={items.Count}; Total={expectedTotal}; Missing={Math.Max(0, expectedTotal - items.Count)}。拒绝提交。");

        DiagnosticLogger.Write(
            $"Frodo personal full snapshot integrity passed; Reason={buildReason}; Status={status}; UniqueItems={items.Count}; Total={expectedTotal}; ExactMatch=True");

        // Invariant: an already usable Store is never cleared before a full
        // snapshot is complete. Network scanning happens without the commit gate;
        // only the final validated snapshot swap is serialized.
        var snapshot = BuildSnapshot(status, total, items);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (!_profileId.Equals(profileId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Frodo 完整快照构建期间用户范围已切换，拒绝提交旧快照。");

                if (beforeSnapshot is null)
                {
                    if (_statuses.ContainsKey(status))
                        throw new InvalidOperationException("Frodo 完整快照构建期间 Store 已建立，拒绝覆盖更新结果。");
                }
                else if (!_statuses.TryGetValue(status, out var latestBeforeCommit) ||
                         latestBeforeCommit.BuiltAtUtc != beforeBuiltAt)
                {
                    throw new InvalidOperationException("Frodo 完整快照构建期间 Store 已变化，拒绝覆盖更新结果。");
                }

                var targetIds = snapshot.Items.Select(item => item.SubjectId).ToHashSet(StringComparer.Ordinal);
                foreach (var otherStatus in _statuses.Keys.Where(key => !key.Equals(status, StringComparison.Ordinal)).ToList())
                {
                    var other = _statuses[otherStatus];
                    var remaining = other.Items.Where(item => !targetIds.Contains(item.SubjectId)).ToList();
                    var removed = other.Items.Count - remaining.Count;
                    if (removed > 0)
                        _statuses[otherStatus] = BuildSnapshot(otherStatus, Math.Max(0, other.Total - removed), remaining);
                }
                _statuses[status] = snapshot;
            }
            await SaveCacheCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        DiagnosticLogger.Write($"Frodo personal full snapshot committed; Reason={buildReason}; ProfileId={profileId}; Status={status}; Items={snapshot.Items.Count}; Total={snapshot.Total}; Playable={snapshot.Items.Count(item => item.Playable)}");
        return snapshot;
    }
    internal async Task<FrodoPersonalIndexStatus?> SyncStatusHeadAsync(
        string profileId,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
            status is not ("collect" or "wish" or "do")) return null;

        var key = $"{profileId}:{status}";
        lock (_maintenanceGate)
        {
            if (!_headSyncRunning.Add(key))
            {
                lock (_stateGate)
                {
                    return _profileId.Equals(profileId, StringComparison.Ordinal) &&
                           _statuses.TryGetValue(status, out var existing) && existing.Complete
                        ? existing
                        : null;
                }
            }
        }

        try
        {
            var raw = await _client.GetInterestsAsync(profileId, status, 0, _options.PageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, status);
            return await ReconcileRemotePageAsync(profileId, status, page, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_maintenanceGate) _headSyncRunning.Remove(key);
        }
    }

    internal async Task<FrodoPersonalIndexStatus?> ReconcileRemotePageAsync(
        string profileId,
        string status,
        FrodoPersonalPage observedPage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
            status is not ("collect" or "wish" or "do")) return null;

        FrodoPersonalIndexStatus current;
        lock (_stateGate)
        {
            if (!_profileId.Equals(profileId, StringComparison.Ordinal) ||
                !_statuses.TryGetValue(status, out current!) || !current.Complete) return null;
        }

        var previousTotal = current.Total;
        var previousBuiltAt = current.BuiltAtUtc;
        var cloudTotal = Math.Max(observedPage.Total, observedPage.Items.Count);
        var knownTargetIds = current.Items.Select(item => item.SubjectId).ToHashSet(StringComparer.Ordinal);
        var pages = new List<FrodoPersonalPage> { observedPage };
        var discoveredIds = observedPage.Items
            .Where(item => !knownTargetIds.Contains(item.SubjectId))
            .Select(item => item.SubjectId)
            .ToHashSet(StringComparer.Ordinal);
        var requiredAdds = Math.Max(0, cloudTotal - previousTotal);

        if (cloudTotal < previousTotal)
        {
            var preserved = await ApplyRemotePagesAsync(
                profileId, status, previousTotal, previousBuiltAt, previousTotal, pages, allowNewItems: false, cancellationToken).ConfigureAwait(false);
            ScheduleDeletionReconcile(profileId, status, cloudTotal);
            DiagnosticLogger.Write(
                $"Frodo personal bounded sync; ProfileId={profileId}; Status={status}; Health=NeedsDeletionReconcile; LocalTotal={previousTotal}; CloudTotal={cloudTotal}; Pages=1; StorePreserved=True");
            return preserved;
        }

        if (requiredAdds == 0 && discoveredIds.Count > 0)
        {
            var preserved = await ApplyRemotePagesAsync(
                profileId, status, previousTotal, previousBuiltAt, previousTotal, pages, allowNewItems: false, cancellationToken).ConfigureAwait(false);
            DiagnosticLogger.Write(
                $"Frodo personal bounded sync; ProfileId={profileId}; Status={status}; Health=NeedsDeepReconcile; Reason=same-total-new-items; LocalTotal={previousTotal}; CloudTotal={cloudTotal}; HeadNew={discoveredIds.Count}; StorePreserved=True");
            return preserved;
        }

        if (discoveredIds.Count > requiredAdds)
        {
            var preserved = await ApplyRemotePagesAsync(
                profileId, status, previousTotal, previousBuiltAt, previousTotal, pages, allowNewItems: false, cancellationToken).ConfigureAwait(false);
            DiagnosticLogger.Write(
                $"Frodo personal bounded sync; ProfileId={profileId}; Status={status}; Health=NeedsDeepReconcile; Reason=head-delta-mismatch; RequiredAdds={requiredAdds}; HeadNew={discoveredIds.Count}; StorePreserved=True");
            return preserved;
        }

        var responseStart = Math.Max(0, observedPage.Start);
        var cursorAdvance = observedPage.Count > 0 ? observedPage.Count : _options.PageSize;
        var nextStart = checked(responseStart + cursorAdvance);

        while (discoveredIds.Count < requiredAdds &&
               nextStart < cloudTotal &&
               pages.Count < MaxBoundedReconcilePages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedStart = nextStart;
            var raw = await _client.GetInterestsAsync(profileId, status, requestedStart, _options.PageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, status);
            if (page.Total != observedPage.Total)
            {
                var preserved = await ApplyRemotePagesAsync(
                    profileId, status, previousTotal, previousBuiltAt, previousTotal, pages, allowNewItems: false, cancellationToken).ConfigureAwait(false);
                DiagnosticLogger.Write(
                    $"Frodo personal bounded sync; ProfileId={profileId}; Status={status}; Health=NeedsDeepReconcile; Reason=cloud-total-changed; FirstTotal={observedPage.Total}; LaterTotal={page.Total}; Pages={pages.Count}; StorePreserved=True");
                return preserved;
            }

            pages.Add(page);
            foreach (var item in page.Items)
            {
                if (!knownTargetIds.Contains(item.SubjectId)) discoveredIds.Add(item.SubjectId);
            }
            if (discoveredIds.Count > requiredAdds)
            {
                var preserved = await ApplyRemotePagesAsync(
                    profileId, status, previousTotal, previousBuiltAt, previousTotal, pages, allowNewItems: false, cancellationToken).ConfigureAwait(false);
                DiagnosticLogger.Write(
                    $"Frodo personal bounded sync; ProfileId={profileId}; Status={status}; Health=NeedsDeepReconcile; Reason=delta-mismatch; RequiredAdds={requiredAdds}; Found={discoveredIds.Count}; Pages={pages.Count}; StorePreserved=True");
                return preserved;
            }

            responseStart = Math.Max(page.Start, requestedStart);
            cursorAdvance = page.Count > 0 ? page.Count : _options.PageSize;
            nextStart = checked(responseStart + cursorAdvance);
            if (page.RawCount == 0) break;
        }

        var deltaComplete = discoveredIds.Count == requiredAdds;
        // If the bounded budget cannot prove the whole delta, do not partially
        // advance Store total or add only some new rows. Partial progress would
        // make the next bounded scan start with a misleading checkpoint and can
        // permanently hide a still-missing row beyond the page budget.
        var targetTotal = deltaComplete ? cloudTotal : previousTotal;
        var result = await ApplyRemotePagesAsync(
            profileId,
            status,
            previousTotal,
            previousBuiltAt,
            targetTotal,
            pages,
            allowNewItems: deltaComplete && requiredAdds > 0,
            cancellationToken).ConfigureAwait(false);

        DiagnosticLogger.Write(
            $"Frodo personal bounded sync; ProfileId={profileId}; Status={status}; Health={(deltaComplete ? "Ready" : "NeedsDeepReconcile")}; LocalTotal={previousTotal}; CloudTotal={cloudTotal}; RequiredAdds={requiredAdds}; Discovered={discoveredIds.Count}; Pages={pages.Count}; PageLimit={MaxBoundedReconcilePages}; StoreTotal={result?.Total.ToString() ?? "unchanged"}; StorePreserved=True");
        return result;
    }
    internal async Task<FrodoPersonalItem?> FetchRecentItemAsync(
        string profileId,
        string status,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
            string.IsNullOrWhiteSpace(subjectId) || !subjectId.All(char.IsDigit) ||
            status is not ("collect" or "wish" or "do")) return null;

        var delays = new[] { 0, 350, 850 };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delays[attempt] > 0) await Task.Delay(delays[attempt], cancellationToken).ConfigureAwait(false);

            var raw = await _client.GetInterestsAsync(profileId, status, 0, _options.PageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, status);
            var item = page.Items.FirstOrDefault(candidate => candidate.SubjectId.Equals(subjectId, StringComparison.Ordinal));
            if (item is not null)
            {
                DiagnosticLogger.Write($"Frodo personal recent readback found; ProfileId={profileId}; Status={status}; SubjectId={subjectId}; Attempt={attempt + 1}; Raw={page.RawCount}; Mapped={page.Items.Count}");
                return item;
            }
        }

        DiagnosticLogger.Write($"Frodo personal recent readback missing; ProfileId={profileId}; Status={status}; SubjectId={subjectId}; Attempts={delays.Length}");
        return null;
    }

    internal async Task<FrodoPersonalItem?> ApplyConfirmedReviewAsync(
        string profileId,
        string subjectId,
        string targetStatus,
        int? myRating,
        string comment,
        string markedDate,
        FrodoPersonalItem? authoritativeItem = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
            string.IsNullOrWhiteSpace(subjectId) || !subjectId.All(char.IsDigit) ||
            targetStatus is not ("collect" or "wish" or "do")) return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FrodoPersonalItem? source = null;
            var existedBefore = false;
            var hasAuthoritative = authoritativeItem is not null &&
                                   authoritativeItem.SubjectId.Equals(subjectId, StringComparison.Ordinal) &&
                                   authoritativeItem.Status.Equals(targetStatus, StringComparison.Ordinal);
            lock (_stateGate)
            {
                if (!_profileId.Equals(profileId, StringComparison.Ordinal)) return null;
                foreach (var snapshot in _statuses.Values)
                {
                    source = snapshot.Items.FirstOrDefault(item => item.SubjectId.Equals(subjectId, StringComparison.Ordinal));
                    if (source is null) continue;
                    existedBefore = true;
                    break;
                }

                if (hasAuthoritative)
                {
                    // Never fabricate a "complete" target snapshot from one row.
                    // UPSERT is allowed only when that status already has a complete store snapshot.
                    if (!_statuses.ContainsKey(targetStatus)) return null;
                    source = authoritativeItem;
                }
                if (source is null) return null;

                var statusLabel = targetStatus switch
                {
                    "collect" => "看过",
                    "wish" => "想看",
                    "do" => "在看",
                    _ => source.StatusLabel
                };
                var updated = hasAuthoritative
                    ? source with { Status = targetStatus, StatusLabel = statusLabel }
                    : source with
                    {
                        Status = targetStatus,
                        StatusLabel = statusLabel,
                        MyRating = targetStatus == "wish" ? null : myRating,
                        Comment = comment ?? "",
                        MarkedDate = markedDate ?? ""
                    };

                foreach (var key in _statuses.Keys.ToList())
                {
                    var snapshot = _statuses[key];
                    var items = snapshot.Items.Where(item => !item.SubjectId.Equals(subjectId, StringComparison.Ordinal)).ToList();
                    var removed = items.Count != snapshot.Items.Count;
                    var addToTarget = key.Equals(targetStatus, StringComparison.Ordinal);
                    if (!removed && !addToTarget) continue;
                    if (addToTarget) items.Insert(0, updated);
                    var delta = (addToTarget ? 1 : 0) - (removed ? 1 : 0);
                    _statuses[key] = BuildSnapshot(key, Math.Max(0, snapshot.Total + delta), items);
                }
                source = updated;
            }

            await SaveCacheCoreAsync(cancellationToken).ConfigureAwait(false);
            DiagnosticLogger.Write($"Frodo personal store authoritative review applied; ProfileId={profileId}; SubjectId={subjectId}; TargetStatus={targetStatus}; Authority={(hasAuthoritative ? "Frodo" : "Local")}; InterestId={source?.InterestId ?? ""}; LocalRating={myRating?.ToString() ?? "null"}; AppliedRating={source?.MyRating?.ToString() ?? "null"}; Mode={(existedBefore ? "update" : "insert")}");
            return source;
        }
        finally
        {
            _gate.Release();
        }
    }
    internal async Task<bool> ApplyConfirmedDeleteAsync(
        string profileId,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit) ||
            string.IsNullOrWhiteSpace(subjectId) || !subjectId.All(char.IsDigit)) return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = false;
            lock (_stateGate)
            {
                if (!_profileId.Equals(profileId, StringComparison.Ordinal)) return false;
                foreach (var key in _statuses.Keys.ToList())
                {
                    var snapshot = _statuses[key];
                    var items = snapshot.Items.Where(item => !item.SubjectId.Equals(subjectId, StringComparison.Ordinal)).ToList();
                    if (items.Count == snapshot.Items.Count) continue;
                    changed = true;
                    _statuses[key] = BuildSnapshot(key, Math.Max(0, snapshot.Total - 1), items);
                }
            }
            if (!changed) return false;
            await SaveCacheCoreAsync(cancellationToken).ConfigureAwait(false);
            DiagnosticLogger.Write($"Frodo personal index authoritative delete applied; ProfileId={profileId}; SubjectId={subjectId}");
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
    internal IReadOnlyList<FrodoPersonalItem> Query(
        string profileId,
        string status,
        FrodoPersonalFilterCriteria criteria)
    {
        if (!TryGetStatus(profileId, status, out var snapshot))
            throw new InvalidOperationException("当前个人状态的完整 Frodo 索引尚未建立。");

        IEnumerable<FrodoPersonalItem> query = snapshot.Items;
        if (!string.IsNullOrWhiteSpace(criteria.ContentType))
            query = query.Where(item => item.ContentType.Equals(criteria.ContentType, StringComparison.OrdinalIgnoreCase));
        if (criteria.PlayableOnly)
            query = query.Where(item => item.Playable);
        if (criteria.ScoreMin is not null)
            query = query.Where(item => item.Score is not null && item.Score.Value >= criteria.ScoreMin.Value);
        if (criteria.ScoreMax is not null)
            query = query.Where(item => item.Score is not null && item.Score.Value <= criteria.ScoreMax.Value);
        if (criteria.Unrated)
            query = query.Where(item => item.MyRating is null);
        else if (criteria.MyRating is not null)
            query = query.Where(item => item.MyRating == criteria.MyRating);

        if (!string.IsNullOrWhiteSpace(criteria.Period))
        {
            var parts = criteria.Period.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var periodValue))
            {
                if (parts[0].Equals("year", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(item => int.TryParse(item.Year, out var year) && year == periodValue);
                else if (parts[0].Equals("decade", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(item => int.TryParse(item.Year, out var year) && year >= periodValue && year < periodValue + 10);
            }
        }

        if (!string.IsNullOrWhiteSpace(criteria.Genre))
            query = query.Where(item => item.Genres.Contains(criteria.Genre, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(criteria.Country))
            query = query.Where(item => item.Countries.Contains(criteria.Country, StringComparer.OrdinalIgnoreCase));

        query = criteria.Sort switch
        {
            "my-rating-desc" => query.OrderByDescending(item => item.MyRating ?? 0).ThenByDescending(item => SortMarkedDate(item.MarkedDate)).ThenBy(item => item.SubjectId, StringComparer.Ordinal),
            "douban-score-desc" => query.OrderByDescending(item => item.Score ?? -1d).ThenByDescending(item => SortMarkedDate(item.MarkedDate)).ThenBy(item => item.SubjectId, StringComparer.Ordinal),
            "year-desc" => query.OrderByDescending(item => SortYear(item.Year)).ThenByDescending(item => SortMarkedDate(item.MarkedDate)).ThenBy(item => item.SubjectId, StringComparer.Ordinal),
            "title-asc" => query.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(item => SortMarkedDate(item.MarkedDate)).ThenBy(item => item.SubjectId, StringComparer.Ordinal),
            _ => query.OrderByDescending(item => SortMarkedDate(item.MarkedDate)).ThenBy(item => item.SubjectId, StringComparer.Ordinal)
        };

        return query.ToList();
    }
    private static FrodoPersonalIndexStatus BuildSnapshot(string status, int total, IReadOnlyList<FrodoPersonalItem> items)
    {
        static List<string> DistinctSorted(IEnumerable<string> values) =>
            values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var years = DistinctSorted(items.Select(item => item.Year))
            .OrderByDescending(SortYear)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var genres = DistinctSorted(items.SelectMany(item => item.Genres));
        var countries = DistinctSorted(items.SelectMany(item => item.Countries));
        return new FrodoPersonalIndexStatus(status, true, Math.Max(total, items.Count), DateTimeOffset.UtcNow, items.ToList(), years, genres, countries);
    }

    private async Task<FrodoPersonalIndexStatus?> ApplyRemotePagesAsync(
        string profileId,
        string status,
        int expectedPreviousTotal,
        DateTimeOffset expectedPreviousBuiltAt,
        int targetTotal,
        IReadOnlyList<FrodoPersonalPage> pages,
        bool allowNewItems,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FrodoPersonalIndexStatus? result;
            lock (_stateGate)
            {
                if (!_profileId.Equals(profileId, StringComparison.Ordinal) ||
                    !_statuses.TryGetValue(status, out var latest) || !latest.Complete) return null;
                if (latest.Total != expectedPreviousTotal || latest.BuiltAtUtc != expectedPreviousBuiltAt)
                {
                    DiagnosticLogger.Write(
                        $"Frodo personal bounded sync commit skipped; ProfileId={profileId}; Status={status}; Cause=StoreChangedDuringScan; ExpectedTotal={expectedPreviousTotal}; ActualTotal={latest.Total}; ExpectedBuiltAt={expectedPreviousBuiltAt:O}; ActualBuiltAt={latest.BuiltAtUtc:O}");
                    return latest;
                }

                var workingItems = _statuses.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Items.ToList(),
                    StringComparer.Ordinal);
                var workingTotals = _statuses.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Total,
                    StringComparer.Ordinal);
                var changedStatuses = new HashSet<string>(StringComparer.Ordinal);
                var remoteItems = pages.SelectMany(page => page.Items)
                    .GroupBy(item => item.SubjectId, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();

                foreach (var remoteItem in remoteItems)
                {
                    var targetItems = workingItems[status];
                    var existingIndex = targetItems.FindIndex(item => item.SubjectId.Equals(remoteItem.SubjectId, StringComparison.Ordinal));
                    if (existingIndex < 0 && !allowNewItems) continue;

                    var applied = existingIndex >= 0
                        ? MergeRemoteItem(targetItems[existingIndex], remoteItem)
                        : remoteItem;

                    foreach (var otherStatus in workingItems.Keys.Where(key => !key.Equals(status, StringComparison.Ordinal)).ToList())
                    {
                        var otherItems = workingItems[otherStatus];
                        var removed = otherItems.RemoveAll(item => item.SubjectId.Equals(remoteItem.SubjectId, StringComparison.Ordinal));
                        if (removed <= 0) continue;
                        workingTotals[otherStatus] = Math.Max(0, workingTotals[otherStatus] - removed);
                        changedStatuses.Add(otherStatus);
                    }

                    if (existingIndex >= 0) targetItems[existingIndex] = applied;
                    else targetItems.Add(applied);
                    changedStatuses.Add(status);
                }

                workingTotals[status] = Math.Max(targetTotal, workingItems[status].Count);
                changedStatuses.Add(status);
                foreach (var changedStatus in changedStatuses)
                {
                    _statuses[changedStatus] = BuildSnapshot(
                        changedStatus,
                        workingTotals[changedStatus],
                        workingItems[changedStatus]);
                }
                result = _statuses[status];
            }

            await SaveCacheCoreAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static FrodoPersonalItem MergeRemoteItem(FrodoPersonalItem existing, FrodoPersonalItem remote) =>
        string.IsNullOrWhiteSpace(remote.InterestId) && !string.IsNullOrWhiteSpace(existing.InterestId)
            ? remote with { InterestId = existing.InterestId }
            : remote;

    private void ScheduleDeletionReconcile(string profileId, string status, int expectedCloudTotal)
    {
        var key = $"{profileId}:{status}";
        lock (_maintenanceGate)
        {
            if (!_deletionReconcileRunning.Add(key)) return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ReconcileDeletionCoreAsync(profileId, status, expectedCloudTotal).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
            {
                DiagnosticLogger.Write(
                    $"Frodo personal deletion reconcile deferred; ProfileId={profileId}; Status={status}; ExpectedCloudTotal={expectedCloudTotal}; Error={ex.Message}; StorePreserved=True");
            }
            finally
            {
                lock (_maintenanceGate) _deletionReconcileRunning.Remove(key);
            }
        });
    }

    private async Task ReconcileDeletionCoreAsync(string profileId, string status, int expectedCloudTotal)
    {
        FrodoPersonalIndexStatus before;
        lock (_stateGate)
        {
            if (!_profileId.Equals(profileId, StringComparison.Ordinal) ||
                !_statuses.TryGetValue(status, out before!) || !before.Complete) return;
        }

        var cloudIds = new HashSet<string>(StringComparer.Ordinal);
        var nextStart = 0;
        var requestCount = 0;
        var complete = false;
        while (requestCount < MaxRequestsPerStatus)
        {
            requestCount++;
            var requestedStart = nextStart;
            var raw = await _client.GetInterestsAsync(profileId, status, requestedStart, _options.PageSize, CancellationToken.None).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, status);
            if (page.Total != expectedCloudTotal)
            {
                DiagnosticLogger.Write(
                    $"Frodo personal deletion reconcile aborted; ProfileId={profileId}; Status={status}; Reason=CloudTotalChanged; Expected={expectedCloudTotal}; Actual={page.Total}; StorePreserved=True");
                return;
            }
            if (page.Skipped.Count > 0)
            {
                DiagnosticLogger.Write(
                    $"Frodo personal deletion reconcile aborted; ProfileId={profileId}; Status={status}; Reason=MappedSkips; Skipped={page.Skipped.Count}; Start={requestedStart}; StorePreserved=True");
                return;
            }

            foreach (var item in page.Items) cloudIds.Add(item.SubjectId);
            var responseStart = Math.Max(page.Start, requestedStart);
            var cursorAdvance = page.Count > 0 ? page.Count : _options.PageSize;
            nextStart = checked(responseStart + cursorAdvance);
            if (page.Total <= 0 && page.RawCount == 0) { complete = true; break; }
            if (expectedCloudTotal > 0 && nextStart >= expectedCloudTotal) { complete = true; break; }
        }

        if (!complete)
        {
            DiagnosticLogger.Write(
                $"Frodo personal deletion reconcile aborted; ProfileId={profileId}; Status={status}; Reason=RequestGuard; Requests={requestCount}; StorePreserved=True");
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<string> removedIds;
            lock (_stateGate)
            {
                if (!_profileId.Equals(profileId, StringComparison.Ordinal) ||
                    !_statuses.TryGetValue(status, out var latest) || !latest.Complete ||
                    latest.Total != before.Total || latest.BuiltAtUtc != before.BuiltAtUtc)
                {
                    DiagnosticLogger.Write(
                        $"Frodo personal deletion reconcile commit skipped; ProfileId={profileId}; Status={status}; Reason=StoreChangedDuringScan; StorePreserved=True");
                    return;
                }

                removedIds = latest.Items
                    .Where(item => !cloudIds.Contains(item.SubjectId))
                    .Select(item => item.SubjectId)
                    .ToList();
                var remaining = latest.Items
                    .Where(item => cloudIds.Contains(item.SubjectId))
                    .ToList();
                _statuses[status] = BuildSnapshot(status, expectedCloudTotal, remaining);
            }

            await SaveCacheCoreAsync(CancellationToken.None).ConfigureAwait(false);
            DiagnosticLogger.Write(
                $"Frodo personal deletion reconcile completed; ProfileId={profileId}; Status={status}; CloudTotal={expectedCloudTotal}; Removed={removedIds.Count}; Requests={requestCount}; StorePreservedUntilCommit=True");
        }
        finally
        {
            _gate.Release();
        }
    }
    private async Task SaveCacheCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = _cachePath + ".tmp";
        FrodoPersonalIndexCache cache;
        lock (_stateGate)
        {
            cache = new FrodoPersonalIndexCache(SchemaVersion, _profileId, DateTimeOffset.UtcNow,
                new Dictionary<string, FrodoPersonalIndexStatus>(_statuses, StringComparer.Ordinal));
        }

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, cache, Json, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempPath, _cachePath, overwrite: true);
    }

    private void ResetProfile(string profileId)
    {
        lock (_stateGate)
        {
            _profileId = profileId;
            _statuses.Clear();
        }
    }

    private static long SortMarkedDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUnixTimeSeconds() : long.MinValue;

    private static int SortYear(string value) => int.TryParse(value, out var year) ? year : int.MinValue;
}
