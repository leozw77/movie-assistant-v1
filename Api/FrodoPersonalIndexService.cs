using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed record FrodoPersonalFilterCriteria(
    string ContentType = "",
    int? MyRating = null,
    bool Unrated = false,
    string Year = "",
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
    private const int SchemaVersion = 1;
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
            lock (_stateGate) _statuses[status] = snapshot;
            await SaveCacheCoreAsync(cancellationToken).ConfigureAwait(false);
            DiagnosticLogger.Write($"Frodo personal index completed; ProfileId={profileId}; Status={status}; Items={snapshot.Items.Count}; Total={snapshot.Total}");
            return snapshot;
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
        if (criteria.Unrated)
            query = query.Where(item => item.MyRating is null);
        else if (criteria.MyRating is not null)
            query = query.Where(item => item.MyRating == criteria.MyRating);
        if (!string.IsNullOrWhiteSpace(criteria.Year))
            query = query.Where(item => item.Year.Equals(criteria.Year, StringComparison.OrdinalIgnoreCase));
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
