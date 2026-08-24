from pathlib import Path


def require_once(text: str, marker: str, label: str) -> int:
    count = text.count(marker)
    if count != 1:
        nearby = ""
        token = marker.strip().splitlines()[0][:40]
        pos = text.find(token[:20])
        if pos >= 0:
            nearby = repr(text[max(0, pos - 80):pos + 240])
        raise RuntimeError(f"{label}: expected exactly one marker, found {count}; nearby={nearby}")
    return text.index(marker)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    require_once(text, old, label)
    return text.replace(old, new, 1)


js_path = Path("WebAssets/DoubanPlus/douban-watchlist.js")
cs_path = Path("HtmlMediaLibraryForm.cs")
js = js_path.read_text(encoding="utf-8")
cs = cs_path.read_text(encoding="utf-8")

# Keep the existing bridge, but allow metadata + browser opening enough time.
js = replace_once(
    js,
    "const request = (type, payload = {}) => new Promise",
    "const request = (type, payload = {}, timeoutMs = 8000) => new Promise",
    "request timeout signature",
)
js = replace_once(js, "}, 8000);", "}, timeoutMs);", "request timeout use")

item_marker = "  const itemFromTarget = target => {"
detail_helper = '''  const detailItemFromPage = () => {
    const match = location.pathname.match(/^\\/subject\\/(\\d+)\\/?$/u);
    if (!match) return null;
    return {
      subjectId: match[1],
      subjectUrl: subjectUrl(match[1]),
      title: text(document.querySelector(".atv-hero-title, .qb-douban-plus-detail h1, h1")) || `豆瓣条目 ${match[1]}`,
      source: "detail-page"
    };
  };

'''
item_pos = require_once(js, item_marker, "itemFromTarget marker")
js = js[:item_pos] + detail_helper + js[item_pos:]

hero_start_marker = '    const heroPoster = target.closest?.(".atv-poster-card");'
hero_start = require_once(js, hero_start_marker, "detail poster start")
hero_end_marker = "    return null;"
hero_end = js.find(hero_end_marker, hero_start)
if hero_end < 0:
    raise RuntimeError("detail poster end marker not found")
new_hero = '''    const heroPoster = target.closest?.(".atv-poster-card");
    if (heroPoster) {
      // Recommendation posters live inside another movie's detail page. Resolve the
      // poster's own subject link first; only the current hero poster may fall back.
      const linkedSubject = heroPoster.matches?.('a[href*="/subject/"]')
        ? heroPoster
        : (heroPoster.closest?.('a[href*="/subject/"]') || heroPoster.querySelector?.('a[href*="/subject/"]'));
      const linkedUrl = validSubjectUrl(linkedSubject?.getAttribute?.("href") || linkedSubject?.href || "");
      const linkedMatch = linkedUrl.match(/\\/subject\\/(\\d+)\\//u);
      if (linkedUrl && linkedMatch) {
        return {
          ...cardSnapshot(target),
          subjectId: linkedMatch[1],
          subjectUrl: linkedUrl,
          title: text(heroPoster.querySelector?.(".atv-poster-card-title, h2, h3")) ||
            String(heroPoster.querySelector?.("img")?.alt || linkedSubject?.getAttribute?.("title") || "").trim() ||
            `豆瓣条目 ${linkedMatch[1]}`,
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "detail-poster"
        };
      }
      const detailItem = detailItemFromPage();
      if (detailItem) {
        return {
          ...cardSnapshot(target),
          ...detailItem,
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "detail-poster"
        };
      }
    }
'''
js = js[:hero_start] + new_hero + js[hero_end:]

page_actions_marker = "  const appendPageActions = menu => {"
pt_menu = '''  const appendPtSearchAction = (menu, item) => {
    menu.append(menuButton("PT 搜索", async () => {
      hideMenu();
      showToast("正在读取 IMDb 并打开 PT 搜索…");
      try {
        const response = await request("doubanWatchlistPtSearchRequest", {
          subjectId: item.subjectId,
          subjectUrl: item.subjectUrl
        }, 20000);
        showToast(response.imdbId ? `已打开 PT 搜索 · ${response.imdbId}` : "已打开 PT 搜索");
      } catch (error) {
        showToast(String(error.message || error));
      }
    }));
  };
'''
page_pos = require_once(js, page_actions_marker, "page actions marker")
js = js[:page_pos] + pt_menu + js[page_pos:]

loading_line = '    menu.append(menuButton("正在检查待看状态…", () => {}, true));'
js = replace_once(js, loading_line, loading_line + "\n    appendPtSearchAction(menu, item);", "PT loading menu")
js = replace_once(
    js,
    "      appendPageActions(menu);\n    } catch (error) {",
    "      appendPtSearchAction(menu, item);\n      appendPageActions(menu);\n    } catch (error) {",
    "PT resolved menu",
)
js = replace_once(
    js,
    "        appendRefreshButton(menu);",
    "        appendPtSearchAction(menu, item);\n        appendPageActions(menu);",
    "PT error menu",
)

context_start_marker = '  document.addEventListener("contextmenu", event => {'
context_start = require_once(js, context_start_marker, "context menu start")
context_end_marker = "  }, true);"
context_end = js.find(context_end_marker, context_start)
if context_end < 0:
    raise RuntimeError("context menu end not found")
context_end += len(context_end_marker)
new_context = '''  document.addEventListener("contextmenu", event => {
    // Poster identity always wins over the enclosing detail page. This keeps
    // recommendation posters bound to their own movie.
    const posterItem = itemFromTarget(event.target);
    if (posterItem) {
      event.preventDefault();
      event.stopPropagation();
      showMenu(event.clientX, event.clientY, posterItem);
      return;
    }

    const detailItem = detailItemFromPage();
    if (detailItem) {
      event.preventDefault();
      event.stopPropagation();
      showMenu(event.clientX, event.clientY, detailItem);
      return;
    }

    if (!isSupportedPage()) return;
    event.preventDefault();
    event.stopPropagation();
    showPageMenu(event.clientX, event.clientY);
  }, true);'''
js = js[:context_start] + new_context + js[context_end:]

cs_anchor = '                case "doubanWatchlistAdd":'
cs_case = '''                case "doubanWatchlistPtSearchRequest":
                {
                    var subjectId = RequiredDigits(root, "subjectId");
                    var subjectUrl = RequiredSubjectUrl(root, "subjectUrl");
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

'''
cs_pos = require_once(cs, cs_anchor, "host PT search request anchor")
cs = cs[:cs_pos] + cs_case + cs[cs_pos:]

js_path.write_text(js, encoding="utf-8", newline="\n")
cs_path.write_text(cs, encoding="utf-8", newline="\n")
print("PT context search patch applied successfully.")
