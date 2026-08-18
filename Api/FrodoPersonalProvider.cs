using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed class FrodoPersonalProvider
{
    private static readonly JsonSerializerOptions ShellJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly FrodoClient _client;
    private readonly FrodoOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FrodoPersonalItem> _items = [];
    private string _profileId = "";
    private string _status = "";
    private string _canonicalUrl = "";
    private int _nextStart;
    private int _total;
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
            var raw = await _client.GetInterestsAsync(profileId, shellStatus, 0, _options.PageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, shellStatus);
            _profileId = profileId;
            _status = shellStatus;
            _canonicalUrl = BasePersonalUrl(profileId, shellStatus);
            _items.Clear();
            _items.AddRange(page.Items.GroupBy(item => item.SubjectId, StringComparer.Ordinal).Select(group => group.First()));
            _total = Math.Max(page.Total, _items.Count);
            _nextStart = Math.Max(page.Start + Math.Max(page.Count, page.Items.Count), _items.Count);
            _hasMore = page.Items.Count > 0 && _nextStart < _total;
            return BuildPayload(requestId, generation);
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

            var raw = await _client.GetInterestsAsync(_profileId, _status, _nextStart, _options.PageSize, cancellationToken).ConfigureAwait(false);
            var page = FrodoPersonalMapper.Map(raw, _status);
            var known = new HashSet<string>(_items.Select(item => item.SubjectId), StringComparer.Ordinal);
            foreach (var item in page.Items)
                if (known.Add(item.SubjectId)) _items.Add(item);
            _total = Math.Max(page.Total, _items.Count);
            var advance = Math.Max(page.Count, page.Items.Count);
            _nextStart = Math.Max(_nextStart + Math.Max(advance, 1), page.Start + Math.Max(advance, 1));
            _hasMore = page.Items.Count > 0 && _nextStart < _total;
            return BuildPayload(requestId, generation);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void Reset()
    {
        _profileId = "";
        _status = "";
        _canonicalUrl = "";
        _items.Clear();
        _nextStart = 0;
        _total = 0;
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
