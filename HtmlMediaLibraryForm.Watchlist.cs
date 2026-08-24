using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QbPotDoubanAi;

internal sealed partial class HtmlMediaLibraryForm
{
    private async Task HandleWatchlistMessageAsync(JsonElement root, string source, WebView2 responseView)
    {
        var requestId = ReadString(root, "requestId");
        var type = ReadString(root, "type");
        try
        {
            switch (type)
            {
                case "doubanWatchlistListRequest":
                    if (!IsAllowedWatchlistListSource(source)) throw new InvalidDataException("待看列表来源无效。");
                    if (IsShellMessageSource(source)) _activeShellViewKind = "watchlist";
                    PostWatchlistResponse(responseView, requestId, true, new { items = _watchlist.Snapshot().Select(WatchlistItemDto).ToList() });
                    return;

                case "doubanWatchlistStateRequest":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                    if (!IsAllowedWatchlistSubjectSource(source, subjectUrl)) throw new InvalidDataException("待看状态请求来源无效。");
                    DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "watchlist state");
                    var stateItem = _watchlist.Find(subjectId);
                    PostWatchlistResponse(responseView, requestId, true, new { item = stateItem is null ? null : WatchlistItemDto(stateItem) });
                    return;
                }

                case "doubanWatchlistPtSearchRequest":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                    var clientTitle = ReadBoundedString(root, "title", 300);
                    var clientSource = ReadBoundedString(root, "source", 60);
                    DiagnosticLogger.Write($"WebView=DoubanPlus; PtContextSearchRequest; ClientTitle={clientTitle}; ClientSource={clientSource}; SubjectId={subjectId}; SubjectUrl={subjectUrl}; PageSource={source}");
                    if (!IsAllowedWatchlistSubjectSource(source, subjectUrl)) throw new InvalidDataException("PT 搜索请求来源无效。");
                    DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "PT search");

                    var record = FindOrCreateRecord(subjectId, subjectUrl);
                    var imdbId = (record.ImdbId ?? "").Trim();
                    var metadataRead = false;
                    if (!BrowserCdpService.IsValidImdbId(imdbId))
                    {
                        var metadata = await _detailConnector.ReadMetadataAsync(subjectUrl, probeStatusCapabilities: false);
                        if (metadata.Captcha) throw new InvalidOperationException("豆瓣要求验证码，暂时无法读取 IMDb 编号。");
                        if (!metadata.LoggedIn) throw new InvalidOperationException("内置豆瓣 Profile 尚未登录，请先扫码登录。");
                        if (!metadata.IsSuccess) throw new InvalidDataException(string.IsNullOrWhiteSpace(metadata.Error) ? "豆瓣没有返回有效详情。" : metadata.Error);
                        ApplyMetadata(record, metadata);
                        imdbId = (record.ImdbId ?? "").Trim();
                        metadataRead = true;
                    }

                    if (!BrowserCdpService.IsValidImdbId(imdbId))
                        throw new InvalidDataException("该影片没有读取到有效 IMDb 编号，无法进行 PT 搜索。");

                    await _cdp.EnsureBackgroundAsync(_preferredBrowser);
                    await _cdp.OpenPtDepilerSearchAsync(imdbId);
                    DiagnosticLogger.Write($"WebView=DoubanPlus; PtContextSearch; SubjectId={subjectId}; ImdbId={imdbId}; MetadataRead={metadataRead}; Source={source}");
                    PostWatchlistResponse(responseView, requestId, true, new { opened = true, imdbId });
                    return;
                }

                case "doubanWatchlistAdd":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
                    if (!IsAllowedWatchlistSubjectSource(source, subjectUrl)) throw new InvalidDataException("待看添加来源无效。");
                    DoubanSubjectIdentity.Validate(subjectId, subjectUrl, "watchlist add");
                    var existing = _watchlist.Find(subjectId);
                    var item = _watchlist.AddOrUpdate(new LocalWatchlistItem
                    {
                        SubjectId = subjectId,
                        SubjectUrl = subjectUrl,
                        Title = ReadBoundedString(root, "title", 300),
                        OriginalTitle = ReadBoundedString(root, "originalTitle", 300),
                        Year = ReadBoundedString(root, "year", 20),
                        Identity = ReadBoundedString(root, "identity", 300),
                        Genre = ReadBoundedString(root, "genre", 300),
                        Director = ReadBoundedString(root, "director", 300),
                        Cast = ReadBoundedString(root, "cast", 600),
                        Score = ReadBoundedString(root, "score", 100),
                        Comment = ReadBoundedString(root, "comment", 1200),
                        PosterSourceUrl = ValidatePosterSourceUrl(ReadBoundedString(root, "posterSourceUrl", 1200)),
                        Source = NormalizeWatchlistSource(ReadBoundedString(root, "source", 30))
                    });
                    var posterSaved = false;
                    var posterError = "";
                    if (!string.IsNullOrWhiteSpace(item.PosterSourceUrl))
                    {
                        (item, posterSaved, posterError) = await SaveWatchlistPosterAsync(item).ConfigureAwait(true);
                    }
                    DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistAdd; SubjectId={subjectId}; Duplicate={existing is not null}; PosterSaved={posterSaved}; PosterError={posterError}; Source={item.Source}; Url={subjectUrl}");
                    PostWatchlistResponse(responseView, requestId, true, new { item = WatchlistItemDto(item), duplicate = existing is not null, posterSaved, posterError });
                    return;
                }

                case "doubanWatchlistDelete":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    if (!IsAllowedWatchlistListSource(source)) throw new InvalidDataException("待看删除来源无效。");
                    var removed = _watchlist.Remove(subjectId);
                    DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistDelete; SubjectId={subjectId}; Removed={removed}");
                    PostWatchlistResponse(responseView, requestId, true, new { removed, items = _watchlist.Snapshot().Select(WatchlistItemDto).ToList() });
                    return;
                }

                default:
                    throw new InvalidDataException("未知的待看消息。");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"WebView=DoubanPlus; WatchlistMessageFailed; Type={type}; RequestId={requestId}; Error={ex.Message}");
            PostWatchlistResponse(responseView, requestId, false, new { error = ex.Message });
        }
        await Task.CompletedTask;
    }

    private void PostWatchlistResponse(WebView2 responseView, string requestId, bool ok, object payload)
    {
        if (responseView.CoreWebView2 is null) return;
        responseView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "doubanWatchlistResponse",
            requestId,
            ok,
            payload
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private object WatchlistItemDto(LocalWatchlistItem item)
    {
        var posterUrl = _watchlist.HasPoster(item) && !string.IsNullOrWhiteSpace(item.PosterPath)
            ? $"https://{WatchlistPosterHost}/{Uri.EscapeDataString(item.PosterPath)}"
            : "";
        return new
        {
            item.SubjectId,
            item.SubjectUrl,
            item.Title,
            item.OriginalTitle,
            item.Year,
            item.Identity,
            item.Genre,
            item.Director,
            item.Cast,
            item.Score,
            item.Comment,
            item.PosterPath,
            item.PosterSourceUrl,
            item.AddedAt,
            item.UpdatedAt,
            item.Note,
            item.Source,
            PosterUrl = posterUrl
        };
    }

    private async Task<(LocalWatchlistItem Item, bool Saved, string Error)> SaveWatchlistPosterAsync(LocalWatchlistItem item)
    {
        if (_watchlist.HasPoster(item)) return (item, true, "");
        if (string.IsNullOrWhiteSpace(item.PosterSourceUrl)) return (item, false, "海报地址为空。");

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, item.PosterSourceUrl);
            request.Headers.Referrer = new Uri("https://movie.douban.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36");
            using var response = await DoubanPlusHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 8_000_000)
                throw new InvalidDataException("海报文件超过 8 MB，已跳过本地保存。");

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(true);
            if (bytes.Length == 0 || bytes.Length > 8_000_000) throw new InvalidDataException("海报文件为空或过大。");
            var extension = PosterExtension(response.Content.Headers.ContentType?.MediaType, item.PosterSourceUrl);
            if (extension.Length == 0) throw new InvalidDataException("海报格式不受支持。");

            Directory.CreateDirectory(_watchlist.PostersDirectory);
            var fileName = item.SubjectId + extension;
            var targetPath = Path.Combine(_watchlist.PostersDirectory, fileName);
            var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, timeout.Token).ConfigureAwait(true);
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            var saved = _watchlist.SetPosterPath(item.SubjectId, fileName);
            return (saved, true, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (item, false, ex.Message);
        }
    }

    private static string PosterExtension(string? mediaType, string sourceUrl)
    {
        var fromType = mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ""
        };
        if (fromType.Length > 0) return fromType;
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg") return ".jpg";
            if (extension is ".png" or ".webp" or ".gif") return extension;
        }
        return "";
    }

    private bool IsAllowedWatchlistListSource(string? source) =>
        IsShellMessageSource(source) || IsAllowedDoubanPersonalUrl(source);

    private bool IsAllowedWatchlistSubjectSource(string? source, string subjectUrl)
    {
        var shellSource = IsShellMessageSource(source);
        if (!shellSource && !IsAllowedDoubanPlusMessageSource(source)) return false;
        if (!DoubanWebView2Connector.IsAllowedSubjectUrl(subjectUrl)) return false;
        if (shellSource) return true;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)) return false;
        return IsAllowedDoubanPersonalUrl(source) || IsAllowedDoubanExploreOrTvUrl(source) || IsDoubanSearchPageUrl(source) || IsDoubanSubjectPageUrl(sourceUri.AbsoluteUri);
    }

    private static string ReadBoundedString(JsonElement value, string name, int maximum)
    {
        var result = ReadString(value, name).Trim();
        return result.Length <= maximum ? result : result[..maximum];
    }

    private static string NormalizeWatchlistSource(string source) =>
        source is "personal" or "explore" or "search" or "detail" or "watchlist" or "shell" ? source : "unknown";

    private static string ValidatePosterSourceUrl(string source)
    {
        if (source.Length == 0) return "";
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "";
        var host = uri.Host;
        return host.EndsWith(".doubanio.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("doubanio.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".douban.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("douban.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : "";
    }

    internal static bool IsAllowedDoubanPlusMessageSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("search.douban.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("www.douban.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("accounts.douban.com", StringComparison.OrdinalIgnoreCase));

    // 保留给评价管线自检；旧本地 HTML bridge 已移除，实际写入仍由显式操作分支控制。
    internal static bool IsAllowedOperation(string? operation) =>
        operation is "saveDoubanEntry" or "deleteEntry";

    internal static bool IsAllowedDoubanPlusRatingUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsKnownRatingHost(uri.Host, "imdb.com") ||
               IsKnownRatingHost(uri.Host, "metacritic.com") ||
               IsKnownRatingHost(uri.Host, "rottentomatoes.com");
    }

    private static bool IsKnownRatingHost(string host, string baseDomain) =>
        host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + baseDomain, StringComparison.OrdinalIgnoreCase);

}
