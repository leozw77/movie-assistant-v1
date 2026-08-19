using System.Net;
using System.Net.Sockets;

namespace QbPotDoubanAi;

public static class SelfTest
{
    public static string Run()
    {
        var lines = new List<string>(); var passed = 0; var total = 0;
        void Check(string name, bool ok) { total++; if (ok) passed++; lines.Add($"{(ok ? "通过" : "失败")}：{name}"); }
        var browser = BrowserCdpService.FindInstalledBrowserName("Chrome"); Check("发现 Chrome 或 Edge（优先 Chrome）", browser is not null); if (browser is not null) lines.Add("  将启动：" + browser);
        var port = BrowserCdpService.ProbeFreeLoopbackPort();
        try { var listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); Check("动态端口可绑定到 127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Address.Equals(IPAddress.Loopback)); listener.Stop(); } catch { Check("动态端口可绑定到 127.0.0.1", false); }
        var a = Snap(10, false); Check("正常播放会累计墙钟时间", WatchTimeRules.CountableSeconds(a, Snap(12, false), 2) == 2);
        Check("暂停不累计", WatchTimeRules.CountableSeconds(a, Snap(10, true), 2) == 0);
        Check("向前拖动不累计", WatchTimeRules.CountableSeconds(a, Snap(500, false), 2) == 0);
        Check("向后拖动不累计", WatchTimeRules.CountableSeconds(a, Snap(2, false), 2) == 0);
        Check("切换标签页不串计时", WatchTimeRules.CountableSeconds(a, Snap(12, false, "other"), 2) == 0);
        var potPath = @"D:\video\好孩子.2025.1080p.mkv";
        Check("PotPlayer沿用稳定版文件名识别", MovieTitle.FromPath(potPath) == "好孩子" && MovieTitle.YearFromPath(potPath) == 2025);
        var dualTitlePath = @"D:\video\[长夜将尽] Wild.Nights.Tamed.Beasts.2025.1080p.WEB-DL.x265.mkv";
        var titleParts = MovieTitle.ParsePath(dualTitlePath);
        Check("双语文件名保留方括号中文别名", titleParts.ChineseTitle == "长夜将尽" && titleParts.EnglishTitle == "Wild Nights Tamed Beasts" && titleParts.Year == 2025 && titleParts.Aliases.Contains("长夜将尽") && titleParts.Aliases.Contains("Wild Nights Tamed Beasts"));
        var wrongCandidate = RecognitionMatcher.Evaluate(titleParts, "Five Nights at Freddy's 2 (film)", null, 2025);
        var rightCandidate = RecognitionMatcher.Evaluate(titleParts, "Wild Nights, Tamed Beasts (film)", null, 2025);
        var conflictingCandidate = RecognitionMatcher.Evaluate(titleParts, "Wild Nights, Tamed Beasts (film)", null, 2024);
        Check("候选标题无相似度时禁止自动接受", !wrongCandidate.HasTitleMatch && !wrongCandidate.IsStrongMatch);
        Check("候选标题与英文别名强匹配才允许自动接受", rightCandidate.IsStrongMatch && rightCandidate.Score >= 80);
        Check("候选年份冲突时禁止自动接受", !conflictingCandidate.IsStrongMatch && conflictingCandidate.Score < 80);
        Check("自动绑定缓存版本已升级", RecognitionCache.AutoBindingVersion == "auto-binding-v3" && RecognitionCache.ConfirmedBindingVersion == "confirmed-binding-v1");
        Check("PotPlayer与浏览器记录键相互隔离", ("pot:" + potPath) != a.Key);
        Check("快捷测试未满足30秒时不触发", !WatchTimeRules.IsQuickCompletionReady(29.9, 0.9));
        Check("快捷测试满足30秒和70%时触发", WatchTimeRules.IsQuickCompletionReady(30, 0.70));
        var pendingScore = new DoubanHistoryRecord { SubjectUrl = "https://movie.douban.com/subject/1/", ScoreFetchStatus = "failed", ScoreFetchAttempts = 2 };
        Check("评分失败记录可从断点重试", DoubanMediaParser.IsScorePending(pendingScore));
        pendingScore.Title = "好孩子"; pendingScore.DoubanScore = 8.2; pendingScore.ScoreFetchStatus = "success"; pendingScore.DetailMetadataFetched = true;
        pendingScore.Summary = "测试简介"; pendingScore.Directors = "测试导演";
        Check("已完整缓存片名和评分不会重复请求", !DoubanMediaParser.IsScorePending(pendingScore));
        pendingScore.Summary = "";
        Check("旧记录标记完成但简介为空时仍会补全", DoubanMediaParser.IsScorePending(pendingScore));
        pendingScore.Summary = "测试简介";
        var metaA = "<meta content=\"好孩子 (豆瓣)\" property=\"og:title\">";
        var metaB = "<meta property=\"og:title\" content=\"好孩子（豆瓣）\">";
        Check("详情片名解析兼容属性前后顺序", DoubanMediaParser.ExtractDoubanTitle(metaA) == "好孩子" && DoubanMediaParser.ExtractDoubanTitle(metaB) == "好孩子");
        pendingScore.Title = "";
        Check("只有评分但片名为空会继续补全", DoubanMediaParser.IsScorePending(pendingScore));
        pendingScore.Title = "豆瓣";
        Check("通用网页标题豆瓣不会被当成电影名", !DoubanMediaParser.IsValidDoubanTitle(pendingScore.Title) && DoubanMediaParser.IsScorePending(pendingScore));
        Check("只清理程序空白页而不清理真实豆瓣页面", BrowserCdpService.IsDisposableWorkerUrl("about:blank") && !BrowserCdpService.IsDisposableWorkerUrl("https://movie.douban.com/subject/36173819/"));
        Check("豆瓣机器人验证页会被识别为风控", DoubanPageRules.IsRiskControl("禁止访问", "https://www.douban.com/misc/sorry?original-url=x", "点击证明"));
        Check("豆瓣目标页必须严格匹配路径", DoubanPageRules.IsExpectedPage("https://movie.douban.com/subject/123/", "https://movie.douban.com/subject/123") && !DoubanPageRules.IsExpectedPage("https://movie.douban.com/subject/123/celebrities", "https://movie.douban.com/subject/123"));
        var rawSearchText = "蜘蛛侠 Spider-Man (2002) [可播放]\n8.1 (424156人评价)\n美国 / 动作 / 科幻 / 121分钟\n想看   看过";
        var searchCandidate = new DoubanSearchCandidate("1292052", "https://movie.douban.com/subject/1292052/", "https://img.example/spider.jpg", rawSearchText);
        Check("在线搜索候选保留豆瓣原始可见文本", searchCandidate.VisibleText == rawSearchText && searchCandidate.SubjectId == "1292052");
        Check("在线搜索页码严格映射豆瓣 start 参数", DoubanSearchPaging.StartForPage(0) == 0 && DoubanSearchPaging.StartForPage(1) == 15 && DoubanSearchPaging.StartForPage(4) == 60);
        Check("PT-Depiler 仅接受合法 IMDb 编号", BrowserCdpService.IsValidImdbId("tt0145487") && !BrowserCdpService.IsValidImdbId("Spider-Man (2002)"));
        var ptSearchUrl = BrowserCdpService.BuildPtDepilerSearchUrl("iloddidemhbedaopmipajgclofjocogb", "tt0145487");
        Check("PT-Depiler 使用扩展搜索页和 imdb 查询前缀", ptSearchUrl.StartsWith("chrome-extension://iloddidemhbedaopmipajgclofjocogb/", StringComparison.Ordinal) && ptSearchUrl.Contains("#/search-entity?", StringComparison.Ordinal) && ptSearchUrl.Contains("search=imdb%7Ctt0145487", StringComparison.Ordinal) && !ptSearchUrl.Contains("google", StringComparison.OrdinalIgnoreCase));
        var webViewRuntime = WebView2EnvironmentProvider.ProbeRuntime();
        Check("检测 WebView2 Evergreen Runtime", webViewRuntime.Available && !string.IsNullOrWhiteSpace(webViewRuntime.Version));
        var doubanPlusAssets = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        Check("Douban Plus 脚本资源完整", DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "system.min.js")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "named-register.min.js")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "douban-plus.user.js")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "douban-watchlist.js")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "douban-watchlist.css")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "douban-country-labels.js")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "douban-explore-page.js")) && DoubanPlusAssetStore.Exists(Path.Combine(doubanPlusAssets, "douban-explore-page.css")));
        var embeddedReadOk = false;
        Check("个人筛选地址严格绑定当前状态范围", HtmlMediaLibraryForm.IsAllowedDoubanPersonalUrl("https://movie.douban.com/people/196650036/collect?filter=video&type=tv") && HtmlMediaLibraryForm.IsSameDoubanPersonalScope("https://movie.douban.com/people/196650036/collect", "https://movie.douban.com/people/196650036/collect?filter=schedule") && !HtmlMediaLibraryForm.IsSameDoubanPersonalScope("https://movie.douban.com/people/196650036/collect", "https://movie.douban.com/people/196650036/wish?filter=schedule") && !HtmlMediaLibraryForm.IsSameDoubanPersonalScope("https://movie.douban.com/people/196650036/collect", "https://example.com/people/196650036/collect"));
        try
        {
            var cardScript = DoubanPlusAssetStore.ReadText(Path.Combine(doubanPlusAssets, "douban-card.js"), "统一卡片资源缺失。");
            var shellScript = DoubanPlusAssetStore.ReadText(Path.Combine(doubanPlusAssets, "douban-shell.js"), "统一 Shell 资源缺失。");
            var personalSourceBridge = DoubanPlusAssetStore.ReadText(Path.Combine(doubanPlusAssets, "douban-personal-source-bridge.js"), "个人 Source Bridge 资源缺失。");
            embeddedReadOk = cardScript.Contains("QbDoubanCard", StringComparison.Ordinal) && cardScript.Contains("qb-media-card-my-rating", StringComparison.Ordinal) && shellScript.Contains("qb-douban-shell-root", StringComparison.Ordinal) && personalSourceBridge.Contains("readPersonalFilters", StringComparison.Ordinal) && shellScript.Contains("doubanShellApplyLocalPersonalFilter", StringComparison.Ordinal) && shellScript.Contains("doubanShellPersonalItemMutation", StringComparison.Ordinal) && shellScript.Contains("qb-shell-personal-primary-row", StringComparison.Ordinal) && shellScript.Contains("qb-shell-score-popover", StringComparison.Ordinal) && shellScript.Contains("personalAdvancedFilterSection", StringComparison.Ordinal) && shellScript.Contains("frodo-local-index", StringComparison.Ordinal);
        }
        catch { }
        Check("Douban Plus UI 资源可从单 EXE 读取", embeddedReadOk);
        Check("原本地 HTML 影视库资源已删除", !Directory.Exists(Path.Combine(AppContext.BaseDirectory, "WebAssets", "MediaLibrary")));
        Check("豆瓣远程导航限制为 HTTPS 豆瓣域名", DoubanWebView2Connector.IsAllowedDoubanTopLevel("https://accounts.douban.com/passport/login") && !DoubanWebView2Connector.IsAllowedDoubanTopLevel("file:///C:/Windows/win.ini") && !DoubanWebView2Connector.IsAllowedDoubanTopLevel("https://example.com/"));
        Check("豆瓣影片地址严格限制 subject 数字路径", DoubanWebView2Connector.IsAllowedSubjectUrl("https://movie.douban.com/subject/36173819/") && !DoubanWebView2Connector.IsAllowedSubjectUrl("https://movie.douban.com/mine"));
        Check("豆瓣探索页严格限制 explore 与 tv 路径", HtmlMediaLibraryForm.IsAllowedDoubanExploreUrl("https://movie.douban.com/explore") && HtmlMediaLibraryForm.IsAllowedDoubanExploreUrl("https://movie.douban.com/explore?foo=bar") && HtmlMediaLibraryForm.IsAllowedDoubanTvUrl("https://movie.douban.com/tv/") && !HtmlMediaLibraryForm.IsAllowedDoubanExploreOrTvUrl("https://movie.douban.com/explore/other") && !HtmlMediaLibraryForm.IsAllowedDoubanExploreOrTvUrl("https://movie.douban.com/tv/other"));
        Check("豆瓣原生搜索页严格限制主机与路径", HtmlMediaLibraryForm.IsDoubanSearchPageUrl("https://search.douban.com/movie/subject_search?search_text=%E8%9C%98%E8%9B%9B%E4%BE%A0") && HtmlMediaLibraryForm.IsAllowedDoubanSourceUrl("https://search.douban.com/movie/subject_search?search_text=x") && !HtmlMediaLibraryForm.IsDoubanSearchPageUrl("https://search.douban.com/movie/other?search_text=x") && !HtmlMediaLibraryForm.IsDoubanSearchPageUrl("https://example.com/movie/subject_search?search_text=x"));
        Check("详情页不再由 C# 读取演职员数据", !BrowserCdpService.DoubanRenderedSubjectScript.Contains("celebrity", StringComparison.OrdinalIgnoreCase) && !BrowserCdpService.DoubanRenderedSubjectScript.Contains("starring", StringComparison.OrdinalIgnoreCase));
        Check("详情页元数据脚本覆盖 JSON-LD 与简介回退", BrowserCdpService.DoubanRenderedSubjectScript.Contains("application/ld+json", StringComparison.Ordinal) && BrowserCdpService.DoubanRenderedSubjectScript.Contains("og:description", StringComparison.Ordinal));
        Check("详情页元数据脚本不包含人物链接字段", !BrowserCdpService.DoubanRenderedSubjectScript.Contains("profileUrl", StringComparison.Ordinal) && !BrowserCdpService.DoubanRenderedSubjectScript.Contains("celebrityId", StringComparison.Ordinal));
        Check("官方编辑表单要求唯一状态才算语义完整", DoubanWebView2Connector.DoubanWriteFormProbeScript.Contains("selectedInterestCount===1", StringComparison.Ordinal) && DoubanWebView2Connector.DoubanWriteFormProbeScript.Contains("semanticComplete", StringComparison.Ordinal));
        var cacheProbe = new DoubanHistoryRecord { Title = "缓存影片", Summary = "简介", Directors = "导演", FullDetailsFetchedAt = DateTime.Now };
        Check("详情缓存 TTL 仅针对基础资料生效", DetailCachePolicy.HasFreshBasicMetadata(cacheProbe, DateTime.Now));
        var basicOnlyCacheProbe = new DoubanHistoryRecord { Title = "仅基础资料", Summary = "简介", Directors = "导演", FullDetailsFetchedAt = DateTime.Now };
        Check("基础资料 TTL 只由详情时间决定", DetailCachePolicy.HasFreshBasicMetadata(basicOnlyCacheProbe, DateTime.Now));
        Check("SubjectId 与 URL 轻量一致性可提取", DoubanSubjectIdentity.ExtractSubjectId("https://movie.douban.com/subject/36173819/") == "36173819");
        var profileProvider = new WebView2EnvironmentProvider(Path.Combine(Path.GetTempPath(), "movie-assistant-profile-test"));
        Check("本地 HTML 与豆瓣登录 Profile 路径分离且固定", profileProvider.LocalProfileDirectory != profileProvider.DoubanProfileDirectory && profileProvider.DoubanProfileDirectory.EndsWith(Path.Combine("WebView2", "DoubanProfile"), StringComparison.OrdinalIgnoreCase));
        var profileWriteOk = false;
        try { Directory.CreateDirectory(profileProvider.DoubanProfileDirectory); var probeFile = Path.Combine(profileProvider.DoubanProfileDirectory, ".write-test"); File.WriteAllText(probeFile, "ok"); profileWriteOk = File.ReadAllText(probeFile) == "ok"; Directory.Delete(Path.GetDirectoryName(profileProvider.LocalProfileDirectory)!, true); } catch { }
        Check("豆瓣专用 Profile 路径可写", profileWriteOk);
        var watchlistTestDirectory = Path.Combine(Path.GetTempPath(), "movie-assistant-watchlist-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var watchlist = new LocalWatchlistStore(watchlistTestDirectory);
            var addedAt = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Local);
            var first = watchlist.AddOrUpdate(new LocalWatchlistItem
            {
                SubjectId = "36173819",
                SubjectUrl = "https://movie.douban.com/subject/36173819/",
                Title = "来福大酒店",
                Year = "2024",
                PosterSourceUrl = "https://img9.doubanio.com/view/photo/s_ratio_poster/public/p123.jpg",
                AddedAt = addedAt,
                Source = "search"
            });
            var duplicate = watchlist.AddOrUpdate(new LocalWatchlistItem
            {
                SubjectId = "36173819",
                SubjectUrl = "https://movie.douban.com/subject/36173819/",
                Title = "来福大酒店（更新）",
                Source = "detail"
            });
            Check("本地待看按 SubjectId 去重", watchlist.Snapshot().Count == 1 && duplicate.AddedAt == first.AddedAt);
            Check("重复加入只更新本地条目且保留详情 URL", duplicate.Title == "来福大酒店（更新）" && duplicate.SubjectUrl.EndsWith("/36173819/", StringComparison.Ordinal));
            var restored = new LocalWatchlistStore(watchlistTestDirectory);
            Check("本地待看重启后可恢复", restored.Find("36173819")?.SubjectUrl == "https://movie.douban.com/subject/36173819/");
            File.WriteAllBytes(Path.Combine(restored.PostersDirectory, "36173819.jpg"), [1, 2, 3]);
            var withPoster = restored.SetPosterPath("36173819", "36173819.jpg");
            Check("本地待看海报路径可保存并恢复", withPoster.PosterPath == "36173819.jpg" && restored.HasPoster(withPoster));
            var posterRestored = new LocalWatchlistStore(watchlistTestDirectory);
            Check("本地待看重启后海报缓存可发现", posterRestored.HasPoster(posterRestored.Find("36173819")!));
            Check("本地待看模型不包含豆瓣官方状态字段", typeof(LocalWatchlistItem).GetProperty(nameof(DoubanHistoryRecord.Status)) is null);
            Check("本地待看删除只删除本地条目", restored.Remove("36173819") && restored.Snapshot().Count == 0 && !File.Exists(Path.Combine(restored.PostersDirectory, "36173819.jpg")));
            File.WriteAllText(restored.JsonPath, "{ invalid json");
            var corruptRejected = false;
            try { _ = new LocalWatchlistStore(watchlistTestDirectory); } catch (InvalidDataException) { corruptRejected = true; }
            Check("本地待看 JSON 损坏时拒绝覆盖原文件", corruptRejected && File.ReadAllText(restored.JsonPath) == "{ invalid json");
        }
        catch (Exception ex)
        {
            Check("本地待看存储自检无未处理异常", false);
            lines.Add("  " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(watchlistTestDirectory)) Directory.Delete(watchlistTestDirectory, true); } catch { }
        }
        Check("CDP 回退保留且不会绕过登录或验证码", DoubanConnectorRouter.CanFallback(new IOException("WebView2进程退出")) && !DoubanConnectorRouter.CanFallback(new InvalidOperationException("内置豆瓣 Profile 尚未登录")) && !DoubanConnectorRouter.CanFallback(new InvalidOperationException("豆瓣要求验证码")));
        var structuredStatus = new DoubanSearchCandidate("1", "https://movie.douban.com/subject/1/", "", "影片原始文本", [new DoubanStatusOption("在看", true)]);
        Check("豆瓣状态与原始可见文本分开保存", structuredStatus.VisibleText == "影片原始文本" && structuredStatus.StatusOptions?.Single().Text == "在看" && structuredStatus.StatusOptions.Single().Selected);
        var loggedInStatus = new DoubanSessionStatus("logged-in", "豆瓣：已登录", "196650036", DateTime.Now, "");
        Check("豆瓣会话状态区分已登录与 Profile ID", loggedInStatus.IsLoggedIn && loggedInStatus.ProfileId == "196650036");
        var tombstone = new DoubanHistoryRecord { SubjectId = "write-delete", Tombstoned = true, TombstonedAt = DateTime.Now, TombstoneReason = "test" };
        var tombstoneJson = System.Text.Json.JsonSerializer.Serialize(tombstone);
        var tombstoneRoundTrip = System.Text.Json.JsonSerializer.Deserialize<DoubanHistoryRecord>(tombstoneJson);
        Check("豆瓣删除使用可持久化 tombstone", tombstoneRoundTrip?.Tombstoned == true && tombstoneRoundTrip.TombstoneReason == "test");
        var reviewIntent = new DoubanEntryWriteRequestV2("collect", ReviewFieldAction.Set, 4, ReviewFieldAction.Clear, null);
        var reviewTarget = new ResolvedReviewTarget("collect", 4, "", false, reviewIntent);
        var reviewSubmitScript = DoubanOfficialFormScripts.BuildSubmitScript("1", reviewTarget);
        Check("豆瓣评价 v2 只使用官方 DOM 表单", reviewSubmitScript.Contains("requestSubmit", StringComparison.Ordinal) && !reviewSubmitScript.Contains("fetch(", StringComparison.OrdinalIgnoreCase) && !reviewSubmitScript.Contains("document.cookie", StringComparison.OrdinalIgnoreCase));
        Check("豆瓣评价 v2 不直接写隐藏评分值", reviewSubmitScript.Contains("input[name=\"rating\"]", StringComparison.Ordinal) && !reviewSubmitScript.Contains("ratingHidden.value =", StringComparison.Ordinal));
        Check("豆瓣写入使用固定 Profile 快照回退", DoubanWebView2Connector.DoubanWriteSnapshotScript.Contains("__PROFILE_ID__", StringComparison.Ordinal) && DoubanWebView2Connector.DoubanWriteSnapshotScript.Contains("fixedProfileId", StringComparison.Ordinal));
        using var diagnosticProbe = System.Text.Json.JsonDocument.Parse("{\"loginPage\":false,\"profileId\":\"123\"}");
        Check("豆瓣诊断字段兼容 JSON 布尔值", DoubanWebView2Connector.ReadDiagnosticValue(diagnosticProbe.RootElement, "loginPage") == "False" && DoubanWebView2Connector.ReadDiagnosticValue(diagnosticProbe.RootElement, "profileId") == "123");
        Check("豆瓣状态编辑受控回退到个人区域", DoubanWebView2Connector.DoubanWriteOpenScriptV2.Contains("pbtn-", StringComparison.Ordinal) && DoubanWebView2Connector.DoubanWriteOpenScriptV2.Contains("personal", StringComparison.Ordinal));
        Check("评价保存与删除操作受固定白名单保护", HtmlMediaLibraryForm.IsAllowedOperation("saveDoubanEntry") && HtmlMediaLibraryForm.IsAllowedOperation("deleteEntry") && !HtmlMediaLibraryForm.IsAllowedOperation("delete") && !HtmlMediaLibraryForm.IsAllowedOperation("setStatus") && !HtmlMediaLibraryForm.IsAllowedOperation("setRating") && !HtmlMediaLibraryForm.IsAllowedOperation("saveShortReview") && !HtmlMediaLibraryForm.IsAllowedOperation("deleteAll"));
        Check("删除 v2 只操作官方 DOM 并要求官方回读", DoubanWebView2Connector.DoubanDeleteInvokeScript.Contains("node.click()", StringComparison.Ordinal) && !DoubanWebView2Connector.DoubanDeleteInvokeScript.Contains("fetch(", StringComparison.OrdinalIgnoreCase) && DoubanWebView2Connector.DoubanDeleteReadbackScript.Contains("deleteControlCount", StringComparison.Ordinal) && DoubanWebView2Connector.DoubanDeleteHistoryProbeScript.Contains("contains", StringComparison.Ordinal));
        Check("BuildFix12 R8 在看删除保留个人 do 列表真实鼠标点击", DoubanWebView2Connector.DoubanDoListDeleteProbeScript.Contains("expectedSubjectId", StringComparison.Ordinal) && DoubanWebView2Connector.DoubanDoListDeleteHitTestScript.Contains("elementFromPoint", StringComparison.Ordinal) && !DoubanWebView2Connector.DoubanDoListDeleteHitTestScript.Contains("fetch(", StringComparison.OrdinalIgnoreCase));
        Check("评价字段支持 Keep Set Clear 三态", Enum.GetValues<ReviewFieldAction>().SequenceEqual([ReviewFieldAction.Keep, ReviewFieldAction.Set, ReviewFieldAction.Clear]));
        var existingOfficial = new OfficialReviewSnapshot(true, true, true, "collect", true, 5, true, "网页短评", true, ["wish", "do", "collect"], true, true, true, "self-test", null);
        var keepRequest = new DoubanEntryWriteRequestV2("do", ReviewFieldAction.Keep, null, ReviewFieldAction.Keep, null);
        var keepTarget = ReviewTargetResolver.Resolve(existingOfficial, keepRequest);
        Check("只改状态时保持网页最新评分短评", keepTarget.Status == "do" && keepTarget.Rating == 5 && keepTarget.Comment == "网页短评");
        var clearRequest = new DoubanEntryWriteRequestV2("collect", ReviewFieldAction.Clear, null, ReviewFieldAction.Clear, null);
        var clearTarget = ReviewTargetResolver.Resolve(existingOfficial, clearRequest);
        Check("评分和短评可明确清除", clearTarget.Rating is null && clearTarget.Comment == "");
        Check("官方表单空评分仍标记为已知", DoubanWebView2Connector.DoubanWriteFormProbeScript.Contains("const ratingKnown=!!ratingHidden", StringComparison.Ordinal));
        Check("统一保存使用连续稳定结算", DoubanWebView2Connector.DoubanWritePostSubmitProbeScript.Contains("formOpen", StringComparison.Ordinal) && new ReviewSettlementPolicy().Timeout(null).State == "timeout-uncertain");
        var legacyRecord = System.Text.Json.JsonSerializer.Deserialize<DoubanHistoryRecord>("{\"SubjectId\":\"36173819\",\"Title\":\"来福大酒店\",\"Status\":\"collect\"}");
        Check("旧版豆瓣历史缺少状态选项时仍可读取", legacyRecord?.SubjectId == "36173819" && legacyRecord.Title == "来福大酒店" && legacyRecord.DoubanStatusOptions.Count == 0);
        Check("Worker 优先级保留删除/保存、官方读取和搜索", WorkerJobQueue.PriorityFor(WorkerJobType.ReviewDelete) == 0 && WorkerJobQueue.PriorityFor(WorkerJobType.ReviewSave) == 0 && WorkerJobQueue.PriorityFor(WorkerJobType.OfficialReviewRead) == 1 && WorkerJobQueue.PriorityFor(WorkerJobType.Search) == 4);
        lines.Insert(0, $"内置自检：{passed}/{total} 项通过");
        return string.Join(Environment.NewLine, lines);
    }
    private static BrowserMediaSnapshot Snap(double time, bool paused, string target = "page-1") => new(target, "https://www.iqiyi.com/v_test.html", "测试影片", 2026, "剧情", 600, time, paused);
}

