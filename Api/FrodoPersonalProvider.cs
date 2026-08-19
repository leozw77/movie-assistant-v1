using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed class FrodoPersonalProvider
{
    private const int MaxInternalRequestsPerVisibleBatch = 10;
    private static readonly JsonSerializerOptions ShellJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly FrodoClient _client;
    private readonly FrodoOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FrodoPersonalItem> _items = [];
    private readonly Queue<FrodoPersonalItem> _pendingItems = new();
    private readonly HashSet<string> _seenSubjectIds = new(StringComparer.Ordinal);
    private string _profileId = "";
    private string _status = "";
    private string _canonicalUrl = "";
    private int _nextStart;
    private int _total;
    private bool _apiHasMore;
    private bool _hasMore;

    internal FrodoPersonalProvider(FrodoOptions options)
    {
        _options = options;
        _client = new FrodoClient(options);
    }

    internal string CurrentProfileId => _profileId;
    internal string CurrentStatus => _status;
    internal string CurrentUrl => _canonicalUrl;

    internal bool IsActiveFor(string? url) =>
        _profileId.Length > 0 && _status.Length > 0 && IsDefaultPersonalUrl(url, _profileId, _status);

    internal static bool TryReadScope(string? url, out string profileId, out string status)
    {
        var ok = TryParsePersonalUrl(url, out profileId, out status, out _);
        return ok;
    }

    internal static bool IsDefaultPersonalUrl(string? url, string profileId, string status)
    {
        if (!TryParsePersonalUrl(url, out var parsedProfileId, out var parsedStatus, out var uri) ||
            !parsedProfileId.Equals(profileId, StringComparison.Ordinal) || !parsedStatus.Equals(status, StringComparison.Ordinal))
            return false;

        var query = ParseQuery(uri.Query);
        foreach (var pair in query)
        {
            var supported = pair.Key switch
            {
                "start" => pair.Value is "" or "0",
                "sort" => pair.Value is "" or "time",
                "type" => pair.Value is "" or "all",
                "filter" => pair.Value is "" or "all",
                "mode" => pair.Value is "" or "grid",
                "tags_sort" => pair.Value is "" or "count",
                _ => false
            };
            if (!supported) return false;
        }
        return true;
    }

    internal async Task<JsonElement> LoadInitialAsync(
        string profileId,
        string shellStatus,
        string canonicalUrl,
        string requestId,
        int generation,
        CancellationToken cancellationToken = default)
    {
        if (!IsDefaultPersonalUrl(canonicalUrl, profileId, shellStatus))
            throw new InvalidDataException("Frodo 只接管默认个人页 URL。");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResetCore();
            _profileId = profileId;
            _status = shellStatus;
            _canonicalUrl = BasePersonalUrl(profileId, shellStatus);
            _nextStart = 0;
            _apiHasMore = true;

            try
            {
                await FillVisibleBatchAsync("initial", cancellationToken).ConfigureAwait(false);
                return BuildPayload(requestId, generation);
            }
            catch
            {
                ResetCore();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<JsonElement> LoadMoreAsync(string requestId, int generation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_profileId.Length == 0 || _status.Length == 0)
                throw new InvalidOperationException("Frodo 个人页尚未初始化。");
            if (!_hasMore) return BuildPayload(requestId, generation);

            await FillVisibleBatchAsync("load-more", cancellationToken).ConfigureAwait(false);
            return BuildPayload(requestId, generation);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<FrodoPersonalItem?> ApplyConfirmedReviewAsync(
        string subjectId,
        string beforeStatus,
        string targetStatus,
        int? myRating,
        string comment,
        string markedDate,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_profileId.Length == 0 || _status.Length == 0 ||
                (_status != beforeStatus && _status != targetStatus)) return null;

            FrodoPersonalItem? source = _items.FirstOrDefault(item => item.SubjectId.Equals(subjectId, StringComparison.Ordinal));
            source ??= _pendingItems.FirstOrDefault(item => item.SubjectId.Equals(subjectId, StringComparison.Ordinal));
            if (source is null) return null;

            var updated = source with
            {
                Status = targetStatus,
                StatusLabel = targetStatus switch
                {
                    "collect" => "看过",
                    "wish" => "想看",
                    "do" => "在看",
                    _ => source.StatusLabel
                },
                MyRating = targetStatus == "wish" ? null : myRating,
                Comment = comment ?? "",
                MarkedDate = markedDate ?? ""
            };

            var keepInCurrent = _status.Equals(targetStatus, StringComparison.Ordinal);
            for (var index = _items.Count - 1; index >= 0; index--)
            {
                if (!_items[index].SubjectId.Equals(subjectId, StringComparison.Ordinal)) continue;
                if (keepInCurrent) _items[index] = updated;
                else _items.RemoveAt(index);
            }

            if (_pendingItems.Count > 0)
            {
                var pending = _pendingItems.ToArray();
                _pendingItems.Clear();
                foreach (var item in pending)
                {
                    if (!item.SubjectId.Equals(subjectId, StringComparison.Ordinal)) _pendingItems.Enqueue(item);
                    else if (keepInCurrent) _pendingItems.Enqueue(updated);
                }
            }

            if (!keepInCurrent) _total = Math.Max(0, _total - 1);
            _apiHasMore = _nextStart < _total;
            _hasMore = _pendingItems.Count > 0 || _apiHasMore;
            DiagnosticLogger.Write($"Frodo personal provider authoritative review applied; SubjectId={subjectId}; BeforeStatus={beforeStatus}; TargetStatus={targetStatus}; CurrentStatus={_status}; Keep={keepInCurrent}; ShellItems={_items.Count}; Pending={_pendingItems.Count}");
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<bool> ApplyConfirmedDeleteAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_profileId.Length == 0 || _status.Length == 0) return false;
            var removed = _items.RemoveAll(item => item.SubjectId.Equals(subjectId, StringComparison.Ordinal)) > 0;
            if (_pendingItems.Count > 0)
            {
                var pending = _pendingItems.ToArray();
                _pendingItems.Clear();
                foreach (var item in pending)
                {
                    if (!item.SubjectId.Equals(subjectId, StringComparison.Ordinal)) _pendingItems.Enqueue(item);
                    else removed = true;
                }
            }
            if (removed) _total = Math.Max(0, _total - 1);
            _apiHasMore = _nextStart < _total;
            _hasMore = _pendingItems.Count > 0 || _apiHasMore;
            if (removed)
                DiagnosticLogger.Write($"Frodo personal provider authoritative delete applied; SubjectId={subjectId}; CurrentStatus={_status}; ShellItems={_items.Count}; Pending={_pendingItems.Count}");
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }
    internal void Reset() => ResetCore();

    private async Task FillVisibleBatchAsync(string operation, CancellationToken cancellationToken)
    {
        // Shell is a five-column grid. Keep the existing Frodo request size (20),
        // but publish a full 20-card user-visible batch whenever possible. Frodo
        // may return fewer interests than count when delisted subjects are omitted.
        var targetVisibleCount = checked(_items.Count + _options.PageSize);
        var publishedFromPending = PublishPending(targetVisibleCount);
        if (publishedFromPending > 0)
        {
            DiagnosticLogger.Write(
                $"Frodo personal pending published; Operation={operation}; Published={publishedFromPending}; ShellItems={_items.Count}; Pending={_pendingItems.Count}; NextStart={_nextStart}; ApiHasMore={_apiHasMore}");
        }

        var internalRequest = 0;
        while (_items.Count < targetVisibleCount && _apiHasMore && internalRequest < MaxInternalRequestsPerVisibleBatch)
        {
            internalRequest++;
            var requestedStart = _nextStart;
            var raw = await _client.GetInterestsAsync(
                _profileId,
                _status,
                requestedStart,
                _options.PageSize,
                cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, _status);

            var duplicateIds = new List<string>();
            var buffered = 0;
            foreach (var item in page.Items)
            {
                if (_seenSubjectIds.Add(item.SubjectId))
                {
                    _pendingItems.Enqueue(item);
                    buffered++;
                }
                else
                {
                    duplicateIds.Add(item.SubjectId);
                }
            }
            _total = Math.Max(page.Total, _items.Count + _pendingItems.Count);

            // Frodo paginates over fixed source slots. The response may contain
            // fewer visible interests than count because unavailable/delisted
            // subjects are omitted, but the next source window still advances by
            // the API count (for example start 0 -> 20 even when Raw=16).
            var responseStart = Math.Max(page.Start, requestedStart);
            var cursorAdvance = page.Count > 0 ? page.Count : _options.PageSize;
            _nextStart = checked(responseStart + cursorAdvance);
            _apiHasMore = _nextStart < _total;

            var published = PublishPending(targetVisibleCount);
            _hasMore = _pendingItems.Count > 0 || _apiHasMore;
            LogPageDiagnostics(operation, internalRequest, requestedStart, page, duplicateIds, buffered, published, cursorAdvance);
        }

        _hasMore = _pendingItems.Count > 0 || _apiHasMore;
        if (_items.Count < targetVisibleCount && _apiHasMore)
        {
            DiagnosticLogger.Write(
                $"Frodo personal visible batch guard reached; Operation={operation}; Target={targetVisibleCount}; ShellItems={_items.Count}; Pending={_pendingItems.Count}; NextStart={_nextStart}; Requests={MaxInternalRequestsPerVisibleBatch}");
        }

        if (_items.Count == 0 && !_hasMore)
            throw new InvalidDataException("Frodo interests 没有返回可显示的个人页记录；已交给 DOM fallback。");
    }

    private int PublishPending(int targetVisibleCount)
    {
        var published = 0;
        while (_items.Count < targetVisibleCount && _pendingItems.Count > 0)
        {
            _items.Add(_pendingItems.Dequeue());
            published++;
        }
        return published;
    }

    private void LogPageDiagnostics(
        string operation,
        int internalRequest,
        int requestedStart,
        FrodoPersonalPage page,
        IReadOnlyList<string> duplicateIds,
        int buffered,
        int published,
        int cursorAdvance)
    {
        DiagnosticLogger.Write(
            $"Frodo personal page mapped; Operation={operation}; InternalRequest={internalRequest}; Status={_status}; RequestedStart={requestedStart}; ResponseStart={page.Start}; ApiCount={page.Count}; Raw={page.RawCount}; Mapped={page.Items.Count}; Skipped={page.Skipped.Count}; Duplicates={duplicateIds.Count}; Buffered={buffered}; Published={published}; Pending={_pendingItems.Count}; ShellItems={_items.Count}; Total={_total}; CursorAdvance={cursorAdvance}; NextStart={_nextStart}; ApiHasMore={_apiHasMore}; HasMore={_hasMore}");

        foreach (var skip in page.Skipped)
        {
            DiagnosticLogger.Write(
                $"Frodo personal row skipped; Operation={operation}; Start={page.Start}; Index={skip.Index}; SubjectId={skip.SubjectId}; Status={skip.FrodoStatus}; Reason={skip.Reason}");
        }

        foreach (var subjectId in duplicateIds.Distinct(StringComparer.Ordinal))
        {
            DiagnosticLogger.Write(
                $"Frodo personal duplicate skipped; Operation={operation}; Start={page.Start}; SubjectId={subjectId}");
        }
    }

    private void ResetCore()
    {
        _profileId = "";
        _status = "";
        _canonicalUrl = "";
        _items.Clear();
        _pendingItems.Clear();
        _seenSubjectIds.Clear();
        _nextStart = 0;
        _total = 0;
        _apiHasMore = false;
        _hasMore = false;
    }

    private JsonElement BuildPayload(string requestId, int generation)
    {
        var filters = BuildPersonalFilters(_profileId, _status);
        return JsonSerializer.SerializeToElement(new
        {
            requestId,
            mode = $"personal-{_status}",
            generation,
            url = _canonicalUrl,
            contentType = "personal",
            personalStatus = _status,
            profileId = _profileId,
            pageReady = true,
            items = _items,
            paging = new { hasMore = _hasMore, label = "加载更多" },
            filters,
            signature = string.Join("|", _items.Select(item => $"{item.SubjectId}:{item.Title}")),
            dom = new { gridItemCount = 0, paginator = _hasMore, ready = true, source = "frodo-api", total = _total, nextStart = _nextStart },
            error = ""
        }, ShellJson);
    }

    private static object BuildPersonalFilters(string profileId, string status)
    {
        var baseUrl = BasePersonalUrl(profileId, status);
        static object Option(string label, string url, bool selected) => new { label, url, selected };
        return new
        {
            groups = new object[]
            {
                new
                {
                    title = "筛选影片",
                    value = "全部",
                    selected = true,
                    options = new object[]
                    {
                        Option("全部", WithQuery(baseUrl, "filter=all&start=0&mode=grid"), true),
                        Option("可播放", WithQuery(baseUrl, "filter=schedule&start=0&mode=grid"), false),
                        Option("有视频", WithQuery(baseUrl, "filter=video&start=0&mode=grid"), false)
                    }
                },
                new
                {
                    title = "影片类型",
                    value = "全部",
                    selected = true,
                    options = new object[]
                    {
                        Option("全部", WithQuery(baseUrl, "type=all&start=0&mode=grid"), true),
                        Option("电影", WithQuery(baseUrl, "type=movie&start=0&mode=grid"), false),
                        Option("电视", WithQuery(baseUrl, "type=tv&start=0&mode=grid"), false)
                    }
                },
                new
                {
                    title = "排序",
                    value = "按时间排序",
                    selected = true,
                    options = new object[]
                    {
                        Option("按时间排序", WithQuery(baseUrl, "sort=time&start=0&mode=grid"), true),
                        Option("按评价排序", WithQuery(baseUrl, "sort=rating&start=0&mode=grid"), false),
                        Option("按标题排序", WithQuery(baseUrl, "sort=title&start=0&mode=grid"), false)
                    }
                }
            }
        };
    }

    private static string BasePersonalUrl(string profileId, string status) =>
        $"https://movie.douban.com/people/{profileId}/{status}";

    private static string WithQuery(string baseUrl, string query) => $"{baseUrl}?{query}";

    private static bool TryParsePersonalUrl(string? url, out string profileId, out string status, out Uri uri)
    {
        profileId = "";
        status = "";
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            !parsed.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = parsed.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3 || !parts[0].Equals("people", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].All(char.IsDigit) || parts[2] is not ("collect" or "wish" or "do")) return false;
        profileId = parts[1];
        status = parts[2];
        uri = parsed;
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = token.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]).Trim();
            if (key.Length == 0) continue;
            result[key] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]).Trim() : "";
        }
        return result;
    }
}
