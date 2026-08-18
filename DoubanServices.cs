using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace QbPotDoubanAi;

public sealed partial class DoubanPageService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public DoubanPageService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
    }

    public async Task<MovieIdentity> ReadAsync(string url)
    {
        url = url.Trim();
        var match = DoubanSubjectUrl().Match(url);
        if (!match.Success) throw new InvalidOperationException("这不是有效的豆瓣电影条目网址，应类似 https://movie.douban.com/subject/1295644/");
        var subjectId = match.Groups[1].Value;
        var apiUrl = "https://movie.douban.com/j/subject_abstract?subject_id=" + subjectId;
        using var apiResponse = await _http.GetAsync(apiUrl);
        if (!apiResponse.IsSuccessStatusCode) throw new InvalidOperationException($"豆瓣影片摘要读取失败（{(int)apiResponse.StatusCode}）。可以稍后重试。 ");
        using var json = JsonDocument.Parse(await apiResponse.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("subject", out var subject)) throw new InvalidOperationException("豆瓣没有返回这个影片条目的资料。 ");
        var title = subject.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        int? year = subject.TryGetProperty("release_year", out var yr) && yr.ValueKind == JsonValueKind.Number ? yr.GetInt32() : null;
        var directors = ReadArray(subject, "directors"); var actors = ReadArray(subject, "actors"); var types = ReadArray(subject, "types");
        var mobileUrl = "https://m.douban.com/movie/subject/" + subjectId + "/";
        var summary = "";
        try
        {
            var html = await _http.GetStringAsync(mobileUrl);
            summary = Decode(MetaDescription().Match(html).Groups[1].Value);
            var intro = summary.IndexOf("简介：", StringComparison.Ordinal);
            if (intro >= 0) summary = summary[(intro + 3)..].Trim();
        }
        catch { }
        var info = $"导演：{directors}；演员：{actors}；类型：{types}；豆瓣条目ID：{subjectId}";
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("无法从豆瓣页面读取片名。 ");
        return new MovieIdentity
        {
            Title = title, Year = year, ImdbId = "", Confidence = "人工确认",
            Summary = summary, Evidence = "用户确认的豆瓣条目；页面资料：" + info,
            SourceUrl = url
        };
    }

    private static string ReadArray(JsonElement subject, string name) => subject.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? string.Join("、", value.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))) : "";

    private static string Decode(string s) => WebUtility.HtmlDecode(s).Trim();
    [GeneratedRegex(@"https?://(?:www\.)?movie\.douban\.com/subject/(\d+)/?", RegexOptions.IgnoreCase)] private static partial Regex DoubanSubjectUrl();
    [GeneratedRegex("<meta[^>]+(?:name|property)=[\"']description[\"'][^>]+content=[\"'](.*?)[\"'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex MetaDescription();
}
