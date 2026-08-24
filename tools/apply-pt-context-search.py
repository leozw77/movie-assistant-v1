from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


js_path = Path("WebAssets/DoubanPlus/douban-watchlist.js")
cs_path = Path("HtmlMediaLibraryForm.cs")
js = js_path.read_text(encoding="utf-8")
cs = cs_path.read_text(encoding="utf-8")

js = replace_once(
    js,
    '  const request = (type, payload = {}) => new Promise((resolve, reject) => {\n',
    '  const request = (type, payload = {}, timeoutMs = 8000) => new Promise((resolve, reject) => {\n',
    "request timeout signature",
)
js = replace_once(
    js,
    '    }, 8000);\n',
    '    }, timeoutMs);\n',
    "request timeout use",
)

item_anchor = '  const itemFromTarget = target => {\n'
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
js = replace_once(js, item_anchor, detail_helper + item_anchor, "detail page helper")

old_hero = '''    const heroPoster = target.closest?.(".atv-poster-card");
    if (heroPoster) {
      const match = location.pathname.match(/^\\/subject\\/(\\d+)\\/?$/u);
      if (match) {
        return {
          ...cardSnapshot(target),
          subjectId: match[1],
          subjectUrl: subjectUrl(match[1]),
          title: text(document.querySelector(".atv-hero-title")) || `豆瓣条目 ${match[1]}`,
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "detail"
        };
      }
    }
'''
new_hero = '''    const heroPoster = target.closest?.(".atv-poster-card");
    if (heroPoster) {
      // Detail pages can contain recommendation posters. A poster's own subject
      // link always wins; only the current hero poster may fall back to the page subject.
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
js = replace_once(js, old_hero, new_hero, "detail poster resolution")

menu_anchor = '''  const appendPageActions = menu => {
    menu.append(menuButton("刷新页面", refreshPage));
    menu.append(menuButton("返回首页", goHome));
  };
'''
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
js = replace_once(js, menu_anchor, pt_menu + menu_anchor, "PT menu helper")

js = replace_once(
    js,
    '''    menu.append(menuButton("正在检查待看状态…", () => {}, true));
    appendPageActions(menu);
''',
    '''    menu.append(menuButton("正在检查待看状态…", () => {}, true));
    appendPtSearchAction(menu, item);
    appendPageActions(menu);
''',
    "PT menu while watchlist state loads",
)
js = replace_once(
    js,
    '''      }
      appendPageActions(menu);
    } catch (error) {
      if (contextItem === item && document.getElementById(MENU_ID)) {
        menu.replaceChildren(menuButton(String(error.message || error), hideMenu));
        appendRefreshButton(menu);
      }
''',
    '''      }
      appendPtSearchAction(menu, item);
      appendPageActions(menu);
    } catch (error) {
      if (contextItem === item && document.getElementById(MENU_ID)) {
        menu.replaceChildren(menuButton(String(error.message || error), hideMenu));
        appendPtSearchAction(menu, item);
        appendPageActions(menu);
      }
''',
    "PT menu after watchlist state",
)

old_context = '''  document.addEventListener("contextmenu", event => {
    const item = itemFromTarget(event.target);
    if (!item) {
      if (!isSupportedPage()) return;
      event.preventDefault();
      event.stopPropagation();
      showPageMenu(event.clientX, event.clientY);
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    showMenu(event.clientX, event.clientY, item);
  }, true);
'''
new_context = '''  document.addEventListener("contextmenu", event => {
    // Poster identity has priority over the enclosing detail page. This is critical
    // for recommendation posters shown inside another movie's subject page.
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
  }, true);
'''
js = replace_once(js, old_context, new_context, "poster-first context menu")

cs_anchor = '                case "doubanWatchlistAdd":\n'
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
cs = replace_once(cs, cs_anchor, cs_case + cs_anchor, "host PT search request")

js_path.write_text(js, encoding="utf-8", newline="\n")
cs_path.write_text(cs, encoding="utf-8", newline="\n")
print("PT context search patch applied successfully.")
