"""Static guardrails for the read-only Douban collect/wish/do adapter."""

from __future__ import annotations

import hashlib
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PERSONAL_JS = ROOT / "WebAssets" / "DoubanPlus" / "douban-personal-page.js"
PERSONAL_CSS = ROOT / "WebAssets" / "DoubanPlus" / "douban-personal-page.css"
WATCHLIST_JS = ROOT / "WebAssets" / "DoubanPlus" / "douban-watchlist.js"
HOST = ROOT / "HtmlMediaLibraryForm.cs"
SCRIPT_HOST = ROOT / "DoubanPlusWebView2Script.cs"
PERSONAL_SEARCH = ROOT / "vendor" / "douban-plus-1.8.1" / "src" / "modules" / "subject" / "runtime" / "personal-search-mount.tsx"
SEARCH_PAGE = ROOT / "vendor" / "douban-plus-1.8.1" / "src" / "modules" / "subject" / "runtime" / "search-page-mount.tsx"
SUBJECT_SWITCHER_CSS = ROOT / "vendor" / "douban-plus-1.8.1" / "src" / "modules" / "subject" / "styles" / "subject-switcher.css"
SEARCH_PAGE_CSS = ROOT / "vendor" / "douban-plus-1.8.1" / "src" / "modules" / "subject" / "styles" / "search-page.css"
DOUBAN_PLUS_MAIN = ROOT / "vendor" / "douban-plus-1.8.1" / "src" / "main.ts"
SUBJECT_INDEX = ROOT / "vendor" / "douban-plus-1.8.1" / "src" / "modules" / "subject" / "index.ts"
BUNDLE = ROOT / "WebAssets" / "DoubanPlus" / "douban-plus.user.js"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> None:
    js = PERSONAL_JS.read_text(encoding="utf-8")
    css = PERSONAL_CSS.read_text(encoding="utf-8")
    watchlist_js = WATCHLIST_JS.read_text(encoding="utf-8")
    host = HOST.read_text(encoding="utf-8")
    script_host = SCRIPT_HOST.read_text(encoding="utf-8")
    personal_search = PERSONAL_SEARCH.read_text(encoding="utf-8")
    search_page = SEARCH_PAGE.read_text(encoding="utf-8")
    subject_switcher_css = SUBJECT_SWITCHER_CSS.read_text(encoding="utf-8")
    search_page_css = SEARCH_PAGE_CSS.read_text(encoding="utf-8")
    douban_plus_main = DOUBAN_PLUS_MAIN.read_text(encoding="utf-8")
    subject_index = SUBJECT_INDEX.read_text(encoding="utf-8")
    bundle = BUNDLE.read_text(encoding="utf-8")

    required_js = [
        "collect|wish|do",
        "profileId",
        '".grid-view .item"',
        '".pic a[href*=\'/subject/\']',
        '".title a[href*=\'/subject/\']',
        '".intro"',
        '".date"',
        '".comment"',
        "rating([0-5])-t",
        '".paginator"',
        "qb-douban-personal-root",
        '"doubanPersonalOpenSubject"',
        "STATUS_META",
        "page.meta.title",
        "statusLinks",
        "qb-personal-primary-nav",
        "qb-personal-status-tab",
        "typeLinks",
        "qb-personal-empty",
        "sessionStorage",
        "restorePersonalScroll",
        "viewportTop",
        "scope: pageScopeKey(location.href)",
        "qbDoubanPlusSearchHost",
        "qb-personal-search-host",
        "qb-douban-personal-url-v1",
        "qb-personal-toolbar-label",
        "pageScopeKey",
        "url.searchParams.get(key) === defaultValue",
        "credentials: \"same-origin\"",
        "DOMParser",
        "IntersectionObserver",
        "qb-personal-infinite",
        "qb-personal-infinite-retry",
        "已加载全部内容",
    ]
    for pattern in required_js:
        assert pattern in js, f"missing adapter guard: {pattern}"

    assert "fetch(" in js, "personal adapter must load later pages in the same-origin page context"
    source_bridge = (ROOT / "WebAssets" / "DoubanPlus" / "douban-personal-source-bridge.js").read_text(encoding="utf-8")
    shell = (ROOT / "WebAssets" / "DoubanPlus" / "douban-shell.js").read_text(encoding="utf-8")
    assert "readPersonalFilters" in source_bridge, "personal source bridge must expose native personal filter state"
    assert 'url.searchParams.delete("tags_sort")' in source_bridge, "personal page scope must ignore Douban presentation-only tags_sort"
    assert "currentOptionUrl" in source_bridge, "personal filter snapshot must retain the currently selected native option"
    assert "currentSortUrl" in source_bridge, "personal filter snapshot must retain the currently selected sort option"
    assert "sortOptionsByNativeOrder" in source_bridge, "personal filter snapshot must preserve native option order"
    assert '["all", "schedule", "video"]' in source_bridge, "personal filter order must remain stable"
    assert '["time", "rating", "title"]' in source_bridge, "personal sort order must remain stable"
    assert "doubanShellApplyPersonalFilter" in shell, "personal shell must route native filter navigation"
    assert "qb-personal-view" in shell, "personal shell must expose its layout scope"
    assert "await loadNext()" in js, "personal restore must load pages until the clicked card is present"
    assert "findPersonalCard" in js, "personal restore must use the clicked card as its anchor"
    assert "requestAnimationFrame" in js, "personal restore must wait for the mounted layout"
    assert "credentials: \"same-origin\"" in js, "later pages must reuse the signed-in page session"
    assert "qb_watchlist" in js, "personal infinite scroll must not run on the local watchlist page"
    assert "GM_xmlhttpRequest" not in js, "personal adapter must not use vendor GM APIs"
    assert "filterInput" not in js, "personal adapter must not mount the local library search"
    assert "qb-personal-subtitle" not in js, "personal page subtitle row must remain removed"
    assert "pageLabel" not in js, "personal page count subtitle must remain removed"
    assert "只读模式" not in js, "personal page read-only badge must remain removed"
    assert "#qb-douban-personal-root" in css
    assert ".qb-personal-primary-nav" in css
    assert ".qb-personal-status-tab" in css
    assert ".qb-personal-filter-bar" in css
    assert "background: linear-gradient(135deg" in css
    assert ".qb-personal-type-nav::before" not in css
    assert "qb-personal-header .qb-watchlist-action" in css
    assert "qb-watchlist-action" in watchlist_js
    assert "我的待看" in watchlist_js
    assert ".qb-personal-subtitle" not in css
    assert ".qb-personal-readonly" not in css
    assert "openDoubanPersonalPage" in host
    assert "IsAllowedDoubanPersonalUrl" in host
    assert "(?:collect|wish|do)" in host
    assert 'status is not ("collect" or "wish" or "do")' in host
    assert "_activeDoubanPlusNavigationUrl = e.Uri" in host
    assert "if (IsAllowedDoubanPersonalUrl(e.Uri)) _activeDoubanPersonalPageUrl = e.Uri;" in host
    assert "douban-personal-page.js" in script_host
    assert "douban-personal-page.css" in script_host
    assert "0da5072d4636ae85f572ff1e673e27ad85d8d4dd" in script_host
    assert "CreateDoubanNavigationOverlay" in host
    assert "ShowDoubanNavigationOverlay" in host
    assert "HideDoubanNavigationOverlay" in host
    assert "previousDoubanViewVisible" in host
    assert "ListToListNavigation" in host
    assert "NavigationOverlay=" in host
    assert "core.DOMContentLoaded" in host
    assert "Phase=DOMContentLoaded" in host
    assert "NavigationCompletedAfterEarlyShow=True" in host
    assert "data-qb-douban-plus-search-host" in personal_search
    assert "SubjectSwitcher" in personal_search
    assert "qb-global-search-switcher" in personal_search
    assert "qbGlobalSearchVisible" in personal_search
    assert "SubjectSwitcher" in search_page
    assert "qb-global-search-host" in search_page
    assert "syncSearchVisibility" in search_page
    assert "qb-global-search-host" in subject_switcher_css
    assert "installEnhancedRoot" in search_page
    assert "atv-search-page" in search_page
    assert "personalSearchPage" in douban_plus_main
    assert "searchPage" in douban_plus_main
    assert "search.douban.com" in subject_index
    assert "mountSearchPage" in subject_index
    assert "IsDoubanSearchPageUrl" in host
    assert "IsDoubanSubjectPageUrl" in host
    assert "_activeDoubanReturnUrl" in host
    assert "_doubanSubjectView" in host
    assert "EnsureDoubanSubjectViewAsync" in host
    assert "Mode=SwitchToListView" in host
    assert "NavigationReused=True" in host
    assert "DualVisibleWebViews=True" in host
    assert "ListViewStateRestore=True" in host
    assert "WebView=DoubanSubject" in host
    assert "CanShowDoubanPlusReturnButton" in host
    assert "_localView" not in host
    assert "CurrentDocumentShown=False; StableRender=False" in host
    assert "extractSearchResults" in search_page
    assert "subjectUrlFor" in search_page
    assert "MutationObserver" in search_page
    assert "IntersectionObserver" not in search_page
    assert "MAX_AUTO_SEARCH_PAGES" not in search_page
    assert "data-qb-search-infinite-sentinel" not in search_page
    assert "nextSearchPageUrl" not in search_page
    assert "loadSearchDocumentInFrame" not in search_page
    assert ".atv-search-page-infinite" not in search_page_css
    assert "atv-search-page-results" in search_page
    assert "atv-search-page-card" in search_page
    assert "atv-search-page-card-rating" in search_page
    assert "atv-search-page-card-facts" in search_page
    assert "atv-search-page-card-index" not in search_page
    assert "atv-search-page-card-badge" not in search_page
    assert "打开详情" in search_page
    assert "data-qb-douban-plus-search-host" in bundle
    assert "atv-subject-switcher-input" in bundle
    assert 'Text = "返回"' in host
    assert "返回探索选片" not in host
    assert "返回搜索" not in host
    assert "返回个人页" not in host

    release_dir = ROOT.parents[1] / "发布版本" / "观影助手-v0.9.0-豆瓣评价写入删除与自动同步-BuildFix12R11-net8轻量版"
    expected = {
        release_dir / "观影助手-v0.9.0-BuildFix12R11.exe": "A35408ED8F2D2AE17DE4E50CC6F977F6582D9EB7B34A963532369BD9624C2B92",
        release_dir.with_name(release_dir.name + ".zip"): "5D9E1EBF115A042EA200823A8D54E22ABF15D81DB33A42479940A1D2F07F9592",
    }
    for path, expected_hash in expected.items():
        assert path.is_file(), f"formal release artifact missing: {path}"
        assert sha256(path) == expected_hash, f"formal release hash changed: {path}"

    print("Douban personal page static validation: PASS")
    print("- routes: /people/{ProfileId}/collect, /wish, /do")
    print("- fields: subject, poster, title, year, score/count when present, personal rating, status, comment")
    print("- status/type navigation: read-only links with current route highlighting")
    print("- pagination: native Douban hrefs preserved as fallback")
    print("- infinite scroll: same-origin next-page fetch, scope validation, SubjectId dedupe, retry and completion state")
    print("- empty/login/captcha/network states: explicit adapter fallback")
    print("- page writes and GM APIs in adapter: none; same-origin fetch is limited to later-page reads")
    print("- formal v0.9.0 artifacts: unchanged")


if __name__ == "__main__":
    main()
