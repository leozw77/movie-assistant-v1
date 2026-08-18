using System.Text.Json;

namespace QbPotDoubanAi;

public sealed class ForeignMetadataService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    public ForeignMetadataService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("QbPotDoubanAi/1.0");

    public async Task<MovieIdentity?> TryResolveAsync(string title, int? year)
    {
        var input = MovieTitle.FromTitle(title, year);
        return (await FindCandidatesAsync(input)).FirstOrDefault(x => x.MatchScore >= 80);
    }

    public async Task<IReadOnlyList<MovieIdentity>> FindCandidatesAsync(MovieTitleParts input)
    {
        var candidates = new Dictionary<string, MovieIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in new[] { "zh.wikipedia.org", "en.wikipedia.org" })
        {
            foreach (var alias in input.Aliases.Take(4))
            {
                try
                {
                    var languageHint = host.StartsWith("zh", StringComparison.Ordinal) ? " 电影" : " film";
                    var query = alias + (input.Year is null ? "" : " " + input.Year) + languageHint;
                    var searchUrl = $"https://{host}/w/api.php?action=query&list=search&srnamespace=0&format=json&utf8=1&srlimit=5&srsearch=" + Uri.EscapeDataString(query);
                    using var search = JsonDocument.Parse(await _http.GetStringAsync(searchUrl));
                    foreach (var hit in search.RootElement.GetProperty("query").GetProperty("search").EnumerateArray())
                    {
                        var pageTitle = hit.GetProperty("title").GetString();
                        if (string.IsNullOrWhiteSpace(pageTitle)) continue;
                        var candidateKey = host + "|" + pageTitle;
                        if (candidates.ContainsKey(candidateKey)) continue;

                        var pageUrl = $"https://{host}/w/api.php?action=query&prop=extracts|pageprops&explaintext=1&format=json&utf8=1&redirects=1&titles=" + Uri.EscapeDataString(pageTitle);
                        using var pageDoc = JsonDocument.Parse(await _http.GetStringAsync(pageUrl));
                        var page = pageDoc.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject().First().Value;
                        var extract = page.TryGetProperty("extract", out var ex) ? ex.GetString() ?? "" : "";
                        if (extract.Length < 120) continue;

                        var years = MovieTitle.YearsFromText(extract);
                        if (input.Year is not null && years.Count > 0 && !years.Contains(input.Year.Value)) continue;
                        if (!page.TryGetProperty("pageprops", out var props) || !props.TryGetProperty("wikibase_item", out var item)) continue;
                        var qid = item.GetString();
                        if (string.IsNullOrWhiteSpace(qid)) continue;

                        var entityUrl = "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&ids=" + qid;
                        using var entityDoc = JsonDocument.Parse(await _http.GetStringAsync(entityUrl));
                        var claims = entityDoc.RootElement.GetProperty("entities").GetProperty(qid).GetProperty("claims");
                        var imdb = ReadClaim(claims, "P345");
                        if (string.IsNullOrWhiteSpace(imdb) || !System.Text.RegularExpressions.Regex.IsMatch(imdb, @"^tt\d{5,10}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;

                        int? candidateYear = null;
                        if (input.Year is not null && years.Contains(input.Year.Value)) candidateYear = input.Year;
                        else if (years.Count > 0) candidateYear = years[0];
                        var evaluation = RecognitionMatcher.Evaluate(input, pageTitle, null, candidateYear);
                        if (!evaluation.HasTitleMatch) continue;
                        if (extract.Length > 14000) extract = extract[..14000];
                        candidates[candidateKey] = new MovieIdentity
                        {
                            Title = pageTitle,
                            Year = candidateYear,
                            ImdbId = imdb,
                            Confidence = evaluation.IsStrongMatch ? "联网核验" : "候选",
                            Summary = extract,
                            Evidence = $"Wikipedia页面标题与输入别名匹配；Wikidata {qid} 的IMDb属性已交叉确认",
                            SourceUrl = $"https://{host}/wiki/" + Uri.EscapeDataString(pageTitle.Replace(' ', '_')),
                            CacheVersion = RecognitionCache.AutoBindingVersion,
                            RecognitionSource = "Wikipedia/Wikidata",
                            InputFileName = input.SourceFileName,
                            InputAliases = input.Aliases.ToList(),
                            MatchScore = evaluation.Score,
                            MatchEvidence = evaluation.Evidence
                        };
                    }
                }
                catch
                {
                    // One host/alias failure must not turn another valid candidate into an automatic match.
                }
            }
        }
        return candidates.Values.OrderByDescending(x => x.MatchScore).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }

    private static string ReadClaim(JsonElement claims, string property)
    {
        try { return claims.GetProperty(property)[0].GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value").GetString() ?? ""; }
        catch { return ""; }
    }
}
