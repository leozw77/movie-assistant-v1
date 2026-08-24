#!/usr/bin/env python3
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FAILURES = []


def check(name: str, condition: bool, detail: str = "") -> None:
    if condition:
        print(f"PASS: {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL: {name}{' | ' + detail if detail else ''}")


shell_js = (ROOT / "WebAssets/DoubanPlus/douban-shell.js").read_text(encoding="utf-8-sig")
personal_source = (ROOT / "WebAssets/DoubanPlus/douban-personal-source-bridge.js").read_text(encoding="utf-8-sig")
source_bridge = (ROOT / "WebAssets/DoubanPlus/douban-source-bridge.js").read_text(encoding="utf-8-sig")
shell_css = (ROOT / "WebAssets/DoubanPlus/douban-shell.css").read_text(encoding="utf-8-sig")
host = (ROOT / "HtmlMediaLibraryForm.cs").read_text(encoding="utf-8-sig")
host_all = "\n".join(path.read_text(encoding="utf-8-sig") for path in ROOT.glob("HtmlMediaLibraryForm*.cs"))
script_host = (ROOT / "DoubanPlusWebView2Script.cs").read_text(encoding="utf-8-sig")

check("Shell JS 存在", bool(shell_js.strip()))
check("Shell CSS 存在", bool(shell_css.strip()))
check("Shell 有统一导航和卡片区域", all(token in shell_js for token in ("qb-douban-shell-root", "qb-douban-shell-grid", "QbDoubanCard", "doubanShellOpenDetail", "doubanShellNavigateContentType")))
check("搜索框提交到真实豆瓣影视搜索", all(token in shell_js + host + script_host for token in ("qb-douban-shell-search-input", "doubanShellSearch", "HandleDoubanShellSearchAsync", "search.douban.com/movie/subject_search")))
check("搜索结果沿用当前统一卡片而非旧搜索卡片", "QbDoubanCard" in shell_js and "atv-search-page-card" not in shell_js and "atv-search-page-card" not in shell_css)
check("搜索 Source 只读取真实搜索 DOM", all(token in source_bridge + host for token in ("isSearchPage", "readSearchCards", "searchSubjectUrlFor", "searchPageLinks", "IsDoubanSearchPageUrl", "IsAllowedDoubanSourceUrl")))
check("搜索文本按当前海报拆分年份国家类型导演主演", all(token in source_bridge + shell_js for token in ("searchPeopleFor", ".abstract_2", "people.director", "people.cast", "identity: [year, countries", "genres", "item.director", "item.cast", "label: \"导演\"", "label: \"主演\"")) and "castIndex" not in source_bridge)
check("搜索结果支持原生分页与代次导航", all(token in shell_js + host for token in ("doubanShellSearchPage", "searchPageLinks", "HandleDoubanShellSearchPageAsync", "_doubanSourceNavigationVersion")))
check("搜索页仅保留手动原生分页", all(token in shell_js + host for token in ("loadSearchPage", "append: false", "messageViewKind === \"search\"", "searchPageUrl", "HandleDoubanShellSearchPageAsync")) and "loadSearchPage(nextLink, true)" not in shell_js and "searchPagingArmed" not in shell_js and "SEARCH_PAGING_COOLDOWN_MS" not in shell_js)
check("搜索详情返回保留搜索地址", all(token in host for token in ("IsDoubanSearchPageUrl", "IsDoubanPlusListPageUrl", "_activeDoubanReturnUrl", "OpenDoubanPlusDetailAsync")))
check("个人 Source 桥读取真实个人 DOM", all(token in personal_source for token in ("QbDoubanPersonalSourceBridge", ".grid-view .item", "DOMParser", "credentials: \"same-origin\"", "collect", "wish", "do")))
check("个人影片三状态进入统一 Shell", all(token in shell_js + host + script_host + personal_source for token in ("doubanShellNavigatePersonal", "HandleDoubanShellPersonalStatusAsync", "GetPersonalSourceBridgeScript", "contentType: \"personal\"")))
check("个人影片详情返回保留当前列表上下文", all(token in host + personal_source for token in ("IsAllowedDoubanPersonalUrl", "_activeDoubanPlusNavigationUrl", "OpenDoubanPlusDetailAsync", "subjectUrl")))
check("个人影片统一 Shell 接入自动无限滚动哨兵", all(token in shell_js for token in ("IntersectionObserver", "qb-shell-paging-sentinel", "button.click()")) and "loadMore" in personal_source)
check("Explore 统一 Shell 接入自动无限滚动哨兵", all(token in shell_js for token in ("viewKind === \"explore\"", "qb-shell-paging-sentinel", "rootMargin: \"0px 0px 720px 0px\"", "pendingPaging", "isPagingOperation")))
check("Explore 和搜索加载更多只追加并按 SubjectId 去重", all(token in shell_js for token in ("append: pagingResponse && (messageViewKind === \"explore\" || messageViewKind === \"search\")", "existingIds", "target.querySelector(\".qb-shell-empty\")?.remove()")))
check("Explore 分页响应绑定当前请求与代次", all(token in shell_js for token in ("pendingPaging.requestId !== requestId", "pendingPaging.epoch !== pagingEpoch", "pendingPaging.contentType !== messageContentType")))
check("Explore 分页失败保留卡片并提供重试", all(token in shell_js for token in ("markPagingError", "重试加载更多", "if (!pendingPaging", "if (!pagingResponse)")) and "render([])" in shell_js)
check("个人 Source 读取与分页不会被重复请求拖住", all(token in host for token in ("_doubanSourceReadGate", "Source result event ignored", "load-more-noop", "totalTimeout")))
check("Shell 和待看海报通过消息桥请求详情", all(token in host + shell_js for token in ("doubanShellOpenDetail", "OpenDoubanPlusDetailAsync")) and "onPosterOpen" in (ROOT / "WebAssets/DoubanPlus/douban-card.js").read_text(encoding="utf-8-sig"))
check("Shell 兼容 WebView2 JSON 字符串消息", "JSON.parse(message)" in shell_js and "doubanShellDataApplied" in shell_js)
check("Shell 有统一筛选和下一页入口", all(token in shell_js for token in ("doubanShellFilterGroup", "doubanShellApplyFilter", "doubanShellLoadMore", "doubanShellFilterOptions")))
check("Explore 筛选组横向排列且选项独占下一行", all(token in shell_css for token in (".qb-shell-filters { display: flex;", ".qb-shell-filter-row { display: flex; flex: 0 0 auto; flex-wrap: nowrap;", ".qb-shell-filter-options { display: flex; flex: 1 0 100%;")))
check("Shell 海报保持完整比例", all(token in shell_css for token in ("aspect-ratio: 2 / 3", "object-fit: contain")))
check("宿主把筛选和分页状态转发给 Shell", all(token in host for token in ("filtersValue", "pagingValue", "HandleDoubanShellFilterGroupAsync", "HandleDoubanShellApplyFilterAsync", "HandleDoubanShellLoadMoreAsync")))
check("分页失败与筛选失败分开显示且可恢复", "doubanShellLoadMoreError" in host and "doubanShellLoadMoreError" in shell_js and "(() =>" in host)
check("宿主处理筛选 no-op 短路", all(token in host for token in ("filter-noop", "ReadBool(action, \"noOp\")")))
check("Shell 与宿主支持电影/电视剧内容类型切换", all(token in shell_js + host + script_host for token in ("data-douban-content-type", "HandleDoubanShellContentTypeAsync", "IsAllowedDoubanExploreOrTvUrl", "contentType")))
check("Source 海报 URL 直接传给 Shell", "PrepareSourcePosterItemsAsync(items)" not in host and "posterUrl" in shell_js)
check("Explore 卡片点击进入详情桥", all(token in shell_js + host for token in ("doubanShellOpenDetail", "OpenDoubanPlusDetailAsync", "_activeDoubanReturnUrl")))
check("Shell 不包含旧隐藏原生代理", all(token not in shell_js for token in ("clickNative", "wrapper.style.display", "MutationObserver")))
check("宿主创建 Source WebView", "_doubanSourceView" in host and "EnsureDoubanSourceViewAsync" in host)
shell_host = host.split("private async Task EnsureLegacyDoubanPlusViewAsync", 1)[0]
check("宿主不再让可见列表 WebView 导航豆瓣 Explore", "_doubanPlusView.CoreWebView2.Navigate(url)" not in shell_host)
check("Shell 使用本地 NavigateToString", "NavigateToString" in host and "GetShellDocument" in script_host)
check("Source 只在导航完成后读取", all(token in host for token in ("_doubanSourceNavigationCompleted", "NavigationCompleted", "read skipped")))
check("Source bridge 缺失时有宿主补注入", all(token in host for token in ("GetSourceBridgeScript", "Source bridge missing", "ExecuteScriptFallback")))
program = (ROOT / "Program.cs").read_text(encoding="utf-8-sig")
single_instance = (ROOT / "SingleInstanceControl.cs").read_text(encoding="utf-8-sig")
check("开发预览有独立单实例通道", all(token in program + single_instance for token in ("--unified-shell-preview", "UnifiedShellPreview", "UseUnifiedShellPreviewPipe")))
check("开发预览按可执行文件身份隔离", "GetUnifiedShellPreviewMutexName" in program and "IdentitySuffix" in single_instance)

check("personal source keeps an advancing pagination cursor", all(token in personal_source for token in ("requestedUrls", "state.requestedUrls.add(requestUrl)", "!state.requestedUrls.has(built.nextPageUrl)", "stale link")))
check("personal mode can navigate back to movie Explore", 'viewKind === "explore" && contentType === currentContentType' in shell_js and "PrepareSourcePosterItemsAsync(items)" not in host)

check("Shell poster direct-load failure has host fallback", all(token in shell_js + host for token in ("doubanShellPosterFailed", "doubanShellPosterFallback", "TryFetchDoubanPosterDataUriAsync")))
check("Shell 动态补图保留评分和短评叠加节点", all(token in shell_js for token in ("querySelectorAll(\".qb-media-card-score, .qb-media-card-comment\")", "overlays.forEach(overlay => poster.append(overlay))")))
check("Explore navigation timeout clears busy state", all(token in shell_js + host for token in ("doubanShellContentTypeError", "MonitorDoubanSourceContentTypeNavigationAsync", "豆瓣探索页面加载超时，请重试。")))
check("人物详情与全部演职员页纳入稳定探针和恢复", all(token in host_all for token in ("IsDoubanSubjectCelebritiesPageUrl", "IsDoubanPersonagePageUrl", "IsDoubanPlusEnhancedPageUrl", "isCelebrities", "isPersonage", "celebritiesContent", "personageContent")))
card_js = (ROOT / "WebAssets/DoubanPlus/douban-card.js").read_text(encoding="utf-8-sig")
card_css = (ROOT / "WebAssets/DoubanPlus/douban-card.css").read_text(encoding="utf-8-sig")
check("统一卡片支持两行标题和固定信息区", all(token in card_js + card_css for token in ("qb-media-card-title", "-webkit-line-clamp: 2", "qb-media-card-info", "qb-media-card-info-row", "qb-media-card-info-label", "qb-media-card-info-value")))
check("统一卡片海报使用完整比例", "object-fit: contain" in shell_css and "object-fit: contain" in (ROOT / "WebAssets/DoubanPlus/douban-personal-page.css").read_text(encoding="utf-8-sig"))
check("统一卡片短评移动到海报左下且不占用卡片底部", all(token in card_js + card_css for token in ("poster.append(comment)", "left: 10px", "bottom: 10px", "if (model.chips.length) body.append(footer)")))
check("统一卡片仅在有短评时提供独立短评悬浮", all(token in card_js + card_css for token in ("qb-media-card-comment", "qb-media-card-comment-popover", "model.comment")))
check("统一卡片演员限制为前两位", "slice(0, 2)" in shell_js and "slice(0, 2)" in source_bridge)
check("Explore 拆分导演和前两位主演", all(token in source_bridge for token in ("splitExplorePeople", "director", "genreIndex + 2")))
check("Explore 支持同一字段内的多个国家", all(token in source_bridge for token in ("COUNTRY_LABELS_BY_LENGTH", "extractCountryLabels", "indexOf(label)", "found.index + found.label.length")))
check("统一卡片评分叠加在海报右下角", all(token in card_js + card_css + shell_js for token in ("poster.append(makeNode(\"strong\", \"qb-media-card-score\"", "position: absolute", "right: 10px", "bottom: 10px", "pointer-events: none")))
check("统一卡片固定信息区和放大评分字号", all(token in card_css + shell_css for token in ("qb-media-card-info", "height: 96px", "font-size: 20px", "font-weight: 700")))
check("列表切换清理旧分页并回到页面顶部", all(token in shell_js for token in ("const beginListSwitch", "pagingObserver?.disconnect()", "pendingPaging = null", "window.scrollTo(0, 0)", "settleListSwitch")))
check("统一卡片评分保持金色且底部不显示媒体类型", "color: var(--qb-card-accent)" in card_css and "mediaType:" not in card_js + shell_js + personal_source and "qb-media-card-media-type" not in card_css)
check("Shell 和个人页评分去除来源标签且不显示状态日期", all(token in shell_js for token in ("publicScore", "personalScore", "replace(/^豆瓣\\s*/u, \"\")")) and "豆瓣 ${item.score}" not in shell_js and "`我的 ${" not in shell_js and "item.statusLabel || personalStatusLabel()" not in shell_js and "item.markedDate ? { text: item.markedDate }" not in shell_js and "`豆瓣 ${item.score}`" not in (ROOT / "WebAssets/DoubanPlus/douban-personal-page.js").read_text(encoding="utf-8-sig"))
check("统一卡片评分和短评使用黑色描边而非半透明底框", all(token in card_css for token in ("-webkit-text-stroke: 1px #080b0f", "background: transparent", "paint-order: stroke fill")) and "background: rgb(15 20 28 / 72%)" not in card_css)
check("个人 Source 独立解析类型、导演和演员字段", all(token in personal_source for token in ("COUNTRY_LABELS", "GENRE_LABELS", "personalFields", "identity: parsed.identity", "genre: parsed.genre", "director: parsed.director", "cast: parsed.cast", "isUrlPart")))

node = shutil.which("node")
if node:
    with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8", delete=False) as handle:
        handle.write(shell_js.replace("__QB_DOUBAN_SHELL_CSS__", '""'))
        path = Path(handle.name)
    try:
        result = subprocess.run([node, "--check", str(path)], capture_output=True, text=True)
        check("Shell JS Node 语法检查", result.returncode == 0, (result.stderr or result.stdout).strip())
    finally:
        path.unlink(missing_ok=True)
else:
    check("Shell JS Node 语法检查", False, "node 未安装")

if node:
    with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8", delete=False) as handle:
        handle.write(personal_source)
        path = Path(handle.name)
    try:
        result = subprocess.run([node, "--check", str(path)], capture_output=True, text=True)
        check("个人 Source 桥 Node 语法检查", result.returncode == 0, (result.stderr or result.stdout).strip())
    finally:
        path.unlink(missing_ok=True)
else:
    check("个人 Source 桥 Node 语法检查", False, "node 未安装")

if node:
    with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8", delete=False) as handle:
        handle.write(source_bridge)
        path = Path(handle.name)
    try:
        result = subprocess.run([node, "--check", str(path)], capture_output=True, text=True)
        check("豆瓣 Source 桥 Node 语法检查", result.returncode == 0, (result.stderr or result.stdout).strip())
    finally:
        path.unlink(missing_ok=True)
else:
    check("豆瓣 Source 桥 Node 语法检查", False, "node 未安装")

print(f"SUMMARY: {len(FAILURES)} failures")
sys.exit(1 if FAILURES else 0)

