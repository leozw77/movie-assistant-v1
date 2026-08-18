"""Static guardrails for the independent local watchlist adapter."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    store = (ROOT / "LocalWatchlistStore.cs").read_text(encoding="utf-8")
    host = (ROOT / "HtmlMediaLibraryForm.cs").read_text(encoding="utf-8")
    script_host = (ROOT / "WatchlistWebView2Script.cs").read_text(encoding="utf-8")
    script = (ROOT / "WebAssets" / "DoubanPlus" / "douban-watchlist.js").read_text(encoding="utf-8")
    css = (ROOT / "WebAssets" / "DoubanPlus" / "douban-watchlist.css").read_text(encoding="utf-8")
    card_js = (ROOT / "WebAssets" / "DoubanPlus" / "douban-card.js").read_text(encoding="utf-8")
    card_css = (ROOT / "WebAssets" / "DoubanPlus" / "douban-card.css").read_text(encoding="utf-8")
    bundle = (ROOT / "WebAssets" / "DoubanPlus" / "douban-plus.user.js").read_text(encoding="utf-8")

    required_store = [
        "LocalWatchlistItem",
        "SubjectId",
        "SubjectUrl",
        "PosterSourceUrl",
        "Identity",
        "Genre",
        "Director",
        "Cast",
        "Score",
        "Comment",
        "AddedAt",
        "File.Replace",
        "本地待看数据损坏，原文件已保留",
    ]
    for pattern in required_store:
        assert pattern in store, f"missing store guard: {pattern}"
    assert "DoubanHistoryRecord" not in store
    assert "Status" not in store

    required_host = [
        "LocalWatchlistStore",
        "WatchlistWebView2Script.InstallAsync",
        "doubanWatchlistListRequest",
        "doubanWatchlistStateRequest",
        "doubanWatchlistAdd",
        "doubanWatchlistDelete",
        "doubanPageRefresh",
        "search.douban.com",
        "IsAllowedWatchlistSubjectSource",
        "ValidatePosterSourceUrl",
        "SetVirtualHostNameToFolderMapping",
        "SaveWatchlistPosterAsync",
        "PosterUrl",
        "ReadAsByteArrayAsync",
        "VerifySessionAsync",
        "doubanShellWatchlistRefresh",
    ]
    for pattern in required_host:
        assert pattern in host, f"missing host guard: {pattern}"
    assert "wish" not in host[host.index("HandleWatchlistMessageAsync"):host.index("IsAllowedWatchlistListSource")]

    required_script = [
        "qb_watchlist",
        "qb-watchlist-action",
        "contextmenu",
        "qb-personal-poster",
        "qb-explore-poster",
        "qb-douban-explore-root",
        "qb-media-card-poster",
        "source: \"shell\"",
        "isShellPage",
        "loadShellWatchlist",
        "const personalRoot",
        "const exploreRoot",
        "atv-search-page-card-poster",
        "atv-poster-card",
        "doubanWatchlistAdd",
        "doubanWatchlistDelete",
        "qb-watchlist-poster",
        "subjectUrl",
        "cardSnapshot",
        "posterSourceUrlFromTarget",
        "dataset.posterUrl",
        "refreshWatchlistList",
        "isShellWatchlistPage",
        "qb-media-card-info-row",
        "viewKind",
    ]
    for pattern in required_script:
        assert pattern in script, f"missing page guard: {pattern}"
    assert "fetch(" not in script
    assert "GM_xmlhttpRequest" not in script
    assert "const mountStyle" in script
    assert 'DOMContentLoaded", mountStyle' in script
    assert "observer.observe(document," in script
    assert "observer.observe(document.documentElement" not in script
    assert "qb-douban-watchlist-menu" in css
    assert "qb-watchlist-poster" in css
    assert "QbDoubanCard.render" in script
    assert "onPosterOpen" in script
    assert "postWatchlistDetail" in script
    assert "const post =" in script
    assert 'target?.closest?.(".qb-watchlist-poster")' in script
    assert "doubanPersonalOpenSubject" in script
    assert "doubanShellOpenDetail" in script
    assert "qb-media-card-title" in card_css
    assert "__QB_DOUBAN_CARD_CSS__" in card_js
    assert "qb-watchlist-remove" in css
    assert "qb-watchlist-remove" in script
    assert "margin-top: 4px" in css
    assert "margin-top: auto" not in css
    assert "qb-watchlist-meta-line" in css
    assert "上映年份" in script
    assert "infoRows" in script
    assert "comment: item.comment" in script
    assert "score: item.score" in script
    assert 'chips: [{ text: "待看"' not in script
    assert "action: remove" in script
    assert ".qb-media-card.qb-watchlist-card .qb-media-card-info" in css
    assert "height: auto" in css
    assert ".qb-media-card.qb-watchlist-card .qb-media-card-action" in css
    assert "posterUrl: item.posterUrl" in script
    assert "posterUrl: item.posterUrl || item.posterSourceUrl" not in script
    assert "加入时间" not in script
    assert "personalWatchlistUrl" in script
    assert "qb-personal-watchlist-tab" not in script
    assert "body.append(title, meta, remove)" not in script
    assert "doubanWatchlistAdd" not in bundle
    assert "qb_watchlist" not in bundle
    assert "__QB_DOUBAN_WATCHLIST_CSS__" in script_host
    assert "PageRefreshRequested" in host
    assert "IsShellMessageSource" in host

    print("Local watchlist static validation: PASS")
    print("- independent JSON store with SubjectId/URL validation and atomic replacement")
    print("- personal/explore/search/detail context-menu sources")
    print("- final personal navigation tab and URL-backed empty poster")
    print("- generated Douban Plus bundle remains unchanged")


if __name__ == "__main__":
    main()
