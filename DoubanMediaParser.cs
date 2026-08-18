using System.Net;
using System.Text.RegularExpressions;

namespace QbPotDoubanAi;

internal static class DoubanMediaParser
{
    internal static bool IsScorePending(DoubanHistoryRecord record) =>
        !HasCompleteDetailMetadata(record) && Uri.TryCreate(record.SubjectUrl, UriKind.Absolute, out _);

    internal static bool HasCompleteDetailMetadata(DoubanHistoryRecord record) =>
        IsValidDoubanTitle(record.Title) &&
        !string.IsNullOrWhiteSpace(record.Summary) &&
        !string.IsNullOrWhiteSpace(record.Directors);

    internal static bool IsValidDoubanTitle(string? title)
    {
        var value = (title ?? "").Trim();
        return value.Length > 0 && !Regex.IsMatch(value, "^豆瓣(?:电影)?$", RegexOptions.IgnoreCase);
    }

    internal static string ExtractDoubanTitle(string html)
    {
        var patterns = new[]
        {
            "<meta[^>]*property=[\"']og:title[\"'][^>]*content=[\"']([^\"']+)",
            "<meta[^>]*content=[\"']([^\"']+)[\"'][^>]*property=[\"']og:title[\"']",
            "<span[^>]*property=[\"']v:itemreviewed[\"'][^>]*>([^<]+)",
            "<h1[^>]*>\\s*<span[^>]*>([^<]+)",
            "<title[^>]*>([^<]+)</title>"
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) continue;
            var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", "")).Trim();
            title = Regex.Replace(title, "\\s*[（(]豆瓣[）)]\\s*$", "");
            if (IsValidDoubanTitle(title)) return title;
        }
        return "";
    }

}
