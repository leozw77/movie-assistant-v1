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

    internal async Task LoadCacheAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit)) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_cachePath))
            {
                ResetProfile(profileId);
                return;
            }

            FrodoPersonalIndexCache? cache;
            await using (var stream = File.OpenRead(_cachePath))
                cache = await JsonSerializer.DeserializeAsync<FrodoPersonalIndexCache>(stream, Json, cancellationToken).ConfigureAwait(false);

            if (cache is null || cache.SchemaVersion != SchemaVersion ||
                !cache.ProfileId.Equals(profileId, StringComparison.Ordinal))
            {
                ResetProfile(profileId);
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
            ResetProfile(profileId);
            DiagnosticLogger.Write($"Frodo personal index cache load failed; ProfileId={profileId}; Error={ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<FrodoPersonalIndexStatus> BuildStatusAsync(
        string profileId,
        string status,
        IProgress<FrodoPersonalIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId) || !profileId.All(char.IsDigit))
            throw new InvalidDataException("Frodo 索引用户 ID 无效。");
        if (status is not ("collect" or "wish" or "do"))
            throw new InvalidDataException("Frodo 索引个人状态无效。");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CurrentProfileId.Equals(profileId, StringComparison.Ordinal)) ResetProfile(profileId);

            var items = new List<FrodoPersonalItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var nextStart = 0;
            var total = 0;
            var requestCount = 0;
            var complete = false;

            while (requestCount < MaxRequestsPerStatus)
            {
                cancellationToken.ThrowIfCancellationRequested();
                requestCount++;
                var requestedStart = nextStart;
                var raw = await _client.GetInterestsAsync(profileId, status, requestedStart, _options.PageSize, cancellationToken).ConfigureAwait(false);
                var page = FrodoPersonalMapper.Map(raw, status);

                foreach (var item in page.Items)
                {
                    if (seen.Add(item.SubjectId)) items.Add(item);
                }

                total = Math.Max(total, page.Total);
                total = Math.Max(total, items.Count);
                progress?.Report(new FrodoPersonalIndexProgress(profileId, status, requestedStart, items.Count, total));

                var cursorAdvance = page.Count > 0 ? page.Count : _options.PageSize;
                var responseStart = Math.Max(page.Start, requestedStart);
                nextStart = checked(responseStart + cursorAdvance);

                DiagnosticLogger.Write(
                    $"Frodo personal index page; Status={status}; RequestedStart={requestedStart}; ResponseStart={page.Start}; ApiCount={page.Count}; Raw={page.RawCount}; Mapped={page.Items.Count}; Skipped={page.Skipped.Count}; Loaded={items.Count}; Total={total}; CursorAdvance={cursorAdvance}; NextStart={nextStart}");

                if (page.Total <= 0 && page.RawCount == 0) { complete = true; break; }
                if (total > 0 && nextStart >= total) { complete = true; break; }
            }

            if (!complete)
                throw new InvalidDataException("Frodo 个人库索引请求次数超过保护上限。");

            var snapshot = BuildSnapshot(status, total, items);
            lock (_stateGate)
            {
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
            DiagnosticLogger.Write($"Frodo personal index completed; ProfileId={profileId}; Status={status}; Items={snapshot.Items.Count}; Total={snapshot.Total}; Playable={snapshot.Items.Count(item => item.Playable)}");
            return snapshot;
        }
        finally
        {
            _gate.Release();
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
        var cloudTotal = Math.Max(observedPage.Total, observedPage.Items.Count);
        var knownTargetIds = current.Items.Select(item => item.SubjectId).ToHashSet(StringComparer.Ordinal);
        var discoveredIds = observedPage.Items
            .Where(item => !knownTargetIds.Contains(item.SubjectId))
            .Select(item => item.SubjectId)
            .ToHashSet(StringComparer.Ordinal);
        var requiredAdds = Math.Max(0, cloudTotal - previousTotal);

        if (cloudTotal < previousTotal)
            return await InvalidateStatusForRebuildAsync(profileId, status,
                $"total-decrease:{previousTotal}->{cloudTotal}", cancellationToken).ConfigureAwait(false);
        if (cloudTotal == previousTotal && discoveredIds.Count > 0)
            return await InvalidateStatusForRebuildAsync(profileId, status,
                $"same-total-new-items:{discoveredIds.Count}", cancellationToken).ConfigureAwait(false);
        if (discoveredIds.Count > requiredAdds)
            return await InvalidateStatusForRebuildAsync(profileId, status,
                $"delta-mismatch:required={requiredAdds},head-new={discoveredIds.Count}", cancellationToken).ConfigureAwait(false);

        var pages = new List<FrodoPersonalPage> { observedPage };
        var responseStart = Math.Max(0, observedPage.Start);
        var cursorAdvance = observedPage.Count > 0 ? observedPage.Count : _options.PageSize;
        var nextStart = checked(responseStart + cursorAdvance);
        var extraRequests = 0;

        while (discoveredIds.Count < requiredAdds && nextStart < cloudTotal && extraRequests < MaxRequestsPerStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extraRequests++;
            var requestedStart = nextStart;
            var raw = await _client.GetInterestsAsync(profileId, status, requestedStart, _options.PageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, status);
            if (page.Total != observedPage.Total)
                return await InvalidateStatusForRebuildAsync(profileId, status,
                    $"cloud-total-changed:{observedPage.Total}->{page.Total}", cancellationToken).ConfigureAwait(false);

            pages.Add(page);
            foreach (var item in page.Items)
            {
                if (!knownTargetIds.Contains(item.SubjectId)) discoveredIds.Add(item.SubjectId);
            }
            if (discoveredIds.Count > requiredAdds)
                return await InvalidateStatusForRebuildAsync(profileId, status,
                    $"delta-mismatch:required={requiredAdds},found={discoveredIds.Count}", cancellationToken).ConfigureAwait(false);

            responseStart = Math.Max(page.Start, requestedStart);
            cursorAdvance = page.Count > 0 ? page.Count : _options.PageSize;
            nextStart = checked(responseStart + cursorAdvance);
            if (page.RawCount == 0) break;
        }

        if (discoveredIds.Count < requiredAdds)
            return await InvalidateStatusForRebuildAsync(profileId, status,
                $"delta-not-found:required={requiredAdds},found={discoveredIds.Count}", cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FrodoPersonalIndexStatus? result = null;
            var changedStatuses = new HashSet<string>(StringComparer.Ordinal);
            lock (_stateGate)
            {
                if (!_profileId.Equals(profileId, StringComparison.Ordinal) ||
                    !_statuses.TryGetValue(status, out var latest) || !latest.Complete) return null;
                if (latest.Total != previousTotal)
                {
                    DiagnosticLogger.Write(
                        $"Frodo personal store reconcile skipped; ProfileId={profileId}; Status={status}; Cause=StoreChangedDuringScan; ExpectedTotal={previousTotal}; ActualTotal={latest.Total}");
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
                var remoteItems = pages.SelectMany(page => page.Items)
                    .GroupBy(item => item.SubjectId, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();

                foreach (var remoteItem in remoteItems)
                {
                    foreach (var otherStatus in workingItems.Keys.Where(key => !key.Equals(status, StringComparison.Ordinal)).ToList())
                    {
                        var list = workingItems[otherStatus];
                        var removed = list.RemoveAll(item => item.SubjectId.Equals(remoteItem.SubjectId, StringComparison.Ordinal));
                        if (removed <= 0) continue;
                        workingTotals[otherStatus] = Math.Max(0, workingTotals[otherStatus] - removed);
                        changedStatuses.Add(otherStatus);
                    }

                    var targetItems = workingItems[status];
                    var existingIndex = targetItems.FindIndex(item => item.SubjectId.Equals(remoteItem.SubjectId, StringComparison.Ordinal));
                    if (existingIndex >= 0) targetItems[existingIndex] = remoteItem;
                    else targetItems.Add(remoteItem);
                    changedStatuses.Add(status);
                }

                workingTotals[status] = cloudTotal;
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
            DiagnosticLogger.Write(
                $"Frodo personal store reconciled; ProfileId={profileId}; Status={status}; PreviousTotal={previousTotal}; CloudTotal={cloudTotal}; RequiredAdds={requiredAdds}; Discovered={discoveredIds.Count}; Requests={pages.Count}; StoreItems={result!.Items.Count}; InterestIds={pages.SelectMany(page => page.Items).Count(item => !string.IsNullOrWhiteSpace(item.InterestId))}");
            return result;
        }
        finally
        {
            _gate.Release();
        }
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

    private async Task<FrodoPersonalIndexStatus?> InvalidateStatusForRebuildAsync(
        string profileId,
        string status,
        string reason,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = false;
            lock (_stateGate)
            {
                if (_profileId.Equals(profileId, StringComparison.Ordinal))
                    removed = _statuses.Remove(status);
            }
            if (removed) await SaveCacheCoreAsync(cancellationToken).ConfigureAwait(false);
            DiagnosticLogger.Write(
                $"Frodo personal store marked for full reconcile; ProfileId={profileId}; Status={status}; Reason={reason}; Removed={removed}");
            return null;
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
