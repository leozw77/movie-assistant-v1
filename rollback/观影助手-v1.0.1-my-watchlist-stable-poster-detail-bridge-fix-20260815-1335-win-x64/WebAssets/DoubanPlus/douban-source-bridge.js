(() => {
  "use strict";

  const text = node => String(node?.textContent || "").replace(/\s+/gu, " ").trim();
  const validId = value => /^\d+$/u.test(String(value || ""));
  const subjectUrl = subjectId => `https://movie.douban.com/subject/${subjectId}/`;
  const explorePath = () => location.pathname.replace(/\/+$/u, "");
  const isExplorePage = () => window.top === window && location.hostname === "movie.douban.com" && ["/explore", "/tv"].includes(explorePath());
  const isTvExplore = () => isExplorePage() && explorePath() === "/tv";
  const contentType = () => isTvExplore() ? "tv" : "movie";
  const contentTypeLabel = () => isTvExplore() ? "电视剧" : "电影";
  const exploreMode = () => `explore-${contentType()}`;
  const post = message => window.chrome?.webview?.postMessage({ type: "doubanSourceResult", ...message });
  const COUNTRY_LABELS = Array.isArray(window.__qbDoubanCountryLabels) ? window.__qbDoubanCountryLabels : [];
  const GENRE_LABELS = ["剧情", "喜剧", "爱情", "动作", "科幻", "动画", "悬疑", "惊悚", "犯罪", "冒险", "音乐", "历史", "奇幻", "恐怖", "战争", "传记", "歌舞", "武侠", "情色", "灾难", "西部", "纪录片", "短片", "家庭", "儿童", "古装", "运动", "真人秀", "脱口秀", "同性", "黑色电影"];
  const COUNTRY_LABELS_BY_LENGTH = [...COUNTRY_LABELS].sort((left, right) => right.length - left.length);

  const selected = node => {
    if (!node) return false;
    const aria = node.getAttribute?.("aria-selected");
    if (aria === "true") return true;
    if (node.dataset?.selected === "true" || node.dataset?.active === "true") return true;
    return /(?:^|\s)(?:selected|active|current|on)(?:\s|$)/u.test(String(node.className || ""));
  };

  const clickNative = node => {
    if (!node?.isConnected) return false;
    if (typeof node.click === "function") {
      node.click();
      return true;
    }
    let event;
    try {
      event = new MouseEvent("click", { bubbles: true, cancelable: true, view: window });
    } catch {
      event = document.createEvent("MouseEvents");
      event.initMouseEvent("click", true, true, window, 1, 0, 0, 0, 0, false, false, false, false, 0, null);
    }
    return node.dispatchEvent(event);
  };

  const allSelectorHost = () => document.querySelector(".explore-all-selectors-main");
  const allSelectorNodes = () => {
    const host = allSelectorHost();
    if (!host) return [];
    return [...host.querySelectorAll(".base-selector")]
      .filter(node => node.closest(".explore-all-selectors-main") === host && !node.parentElement?.closest(".base-selector"));
  };
  const isAllMode = () => {
    const active = [...document.querySelectorAll(".explore-menu li.explore-recent-hot-tag")].find(selected);
    return /全部/u.test(text(active));
  };
  const native = {
    primary: () => [...document.querySelectorAll(".explore-menu li.explore-recent-hot-tag")],
    secondary: () => [...document.querySelectorAll(".explore-menu-second li.explore-menu-second-tag")],
    loadMore: () => [...document.querySelectorAll(".subject-list-main button")]
      .find(button => /加载更多/u.test(text(button))),
    list: () => document.querySelector(".subject-list-list")
  };
  const nativeGroupTitle = node => text(node?.querySelector(".base-selector-title span") || node?.querySelector(".base-selector-title"));
  const nativeGroupValue = (node, fallback = "筛选") => text(node?.querySelector(".base-selector-selected") || node?.querySelector(".base-selector-title span") || node) || fallback;
  const visibleNativeOptions = group => {
    const card = group?.classList.contains("expand") ? group.querySelector(".expand-card") : null;
    return [...(card?.querySelectorAll(".drc-label") || [])].filter(option => text(option));
  };
  const nativeOptionSelected = node => selected(node) || selected(node.parentElement) || selected(node.closest("li"));
  const filterLabel = node => text(node?.querySelector(".base-selector-title span") || node);
  const simpleFilter = (kind, label) => {
    const nodes = kind === "primary" ? native.primary() : native.secondary();
    return nodes.find(node => text(node) === label || filterLabel(node) === label) || null;
  };
  const filterGroups = () => allSelectorNodes()
    .map((node, index) => ({ node, index, title: nativeGroupTitle(node) }))
    .filter(group => group.title);
  const findFilterGroup = title => filterGroups().find(group => group.title === title)?.node || null;
  const readFilterSnapshot = () => {
    const primary = native.primary().map(node => ({ label: text(node), selected: selected(node) })).filter(item => item.label);
    const secondary = native.secondary().map(node => ({
      label: text(node),
      selected: selected(node),
      title: nativeGroupTitle(node),
      value: nativeGroupValue(node)
    })).filter(item => item.label);
    const groups = filterGroups().map(group => ({
      title: group.title,
      value: nativeGroupValue(group.node),
      expanded: group.node.classList.contains("expand"),
      options: visibleNativeOptions(group.node).map(option => ({ label: text(option), selected: selected(option) }))
    }));
    const loadMore = native.loadMore();
    return {
      primary,
      secondary,
      groups,
      selectedPrimary: text(native.primary().find(selected)),
      isAllMode: isAllMode(),
      paging: { hasMore: Boolean(loadMore), label: text(loadMore) || "加载更多" }
    };
  };

  const unique = values => [...new Set(values.filter(Boolean))];
  const extractCountryLabels = parts => unique(parts.flatMap(part => {
    const text = String(part || "");
    let cursor = 0;
    const candidates = COUNTRY_LABELS_BY_LENGTH
      .map(label => ({ label, index: text.indexOf(label) }))
      .filter(found => found.index >= 0)
      .sort((left, right) => left.index - right.index || right.label.length - left.label.length);
    return candidates.filter(found => {
      if (found.index < cursor) return false;
      cursor = found.index + found.label.length;
      return true;
    }).map(found => found.label);
  }));
  const isUrlPart = part => /^(?:https?:\/\/|www\.)/iu.test(String(part || "").trim());
  const splitExplorePeople = part => String(part || "")
    .split(/\s+/u)
    .map(value => value.trim())
    .filter(value => value && !isUrlPart(value))
    .slice(0, 2);
  const parseExploreMeta = subtitle => {
    const parts = String(subtitle || "").split(/\s*\/\s*/u).map(value => value.trim()).filter(Boolean);
    const year = parts.find(part => /(?:19|20)\d{2}/u.test(part))?.match(/(?:19|20)\d{2}/u)?.[0] || "";
    const countries = extractCountryLabels(parts);
    const genreIndex = parts.findIndex((part, index) => index > 0 && GENRE_LABELS.some(label => part.includes(label)));
    const genrePart = genreIndex >= 0 ? parts[genreIndex] : "";
    const genres = unique([genrePart].flatMap(part => GENRE_LABELS
      .map(label => ({ label, index: part.indexOf(label) }))
      .filter(found => found.index >= 0)
      .sort((left, right) => left.index - right.index)
      .map(found => found.label)));
    const director = genreIndex >= 0 && !isUrlPart(parts[genreIndex + 1]) ? parts[genreIndex + 1] || "" : "";
    const cast = genreIndex >= 0 ? splitExplorePeople(parts[genreIndex + 2]) : [];
    return {
      identity: [year, countries.slice(0, 3).join(" / ")].filter(Boolean).join(" / "),
      genre: genres.join(" / "),
      countries,
      genres,
      director,
      directors: director ? [director] : [],
      cast
    };
  };

  const parseCard = anchor => {
    const rawHref = anchor.getAttribute("href") || "";
    const match = rawHref.match(/(?:movie|tv)\/(\d+)/u) || rawHref.match(/(?:uri=\/)(?:movie|tv)\/(\d+)/u) || rawHref.match(/\/subject\/(\d+)/u);
    const subjectId = match?.[1] || anchor.dataset.subjectId || "";
    if (!validId(subjectId)) return null;
    const card = anchor.querySelector(".drc-subject-card") || anchor;
    const title = text(card.querySelector(".drc-subject-info-title-text, h2, .title")) || `豆瓣条目 ${subjectId}`;
    const subtitle = text(card.querySelector(".drc-subject-info-subtitle, .subtitle"));
    const score = text(card.querySelector(".drc-rating-num, .rating_num"));
    const poster = card.querySelector("img.drc-cover-pic, img")?.currentSrc || card.querySelector("img")?.src || "";
    const parsed = parseExploreMeta(subtitle);
    const year = parsed.identity.match(/(?:19|20)\d{2}/u)?.[0] || "";
    return { subjectId, subjectUrl: subjectUrl(subjectId), title, subtitle, score, posterUrl: poster, year, identity: parsed.identity, genre: parsed.genre, countries: parsed.countries, genres: parsed.genres, director: parsed.director, directors: parsed.directors, cast: parsed.cast, contentType: contentType() };
  };

  const readCards = () => {
    const seen = new Set();
    const selector = ".subject-list-list a[href], .subject-list-main a[href], .subject-list a[href]";
    return [...document.querySelectorAll(selector)]
      .map(parseCard)
      .filter(item => item && !seen.has(item.subjectId) && seen.add(item.subjectId));
  };
  const cardSignature = () => readCards().map(item => item.subjectId).join(",");

  const readPage = request => {
    const args = request || {};
    const items = readCards();
    const filters = readFilterSnapshot();
    return {
      type: "doubanSourceResult",
      phase: "readPage",
      requestId: String(args.requestId || ""),
      mode: String(args.mode || exploreMode()),
      generation: Number(args.generation || 0),
      url: location.href,
      contentType: contentType(),
      items,
      filters,
      paging: filters.paging,
      signature: cardSignature(),
      dom: {
        readyState: document.readyState,
        title: document.title,
        htmlLength: document.documentElement?.outerHTML?.length || 0,
        subjectListCount: document.querySelectorAll(".subject-list-list").length,
        candidateAnchorCount: document.querySelectorAll(".subject-list-list a[href], .subject-list-main a[href], .subject-list a[href]").length,
        exploreMenuCount: document.querySelectorAll(".explore-menu").length,
        listMainCount: document.querySelectorAll(".subject-list-main").length,
        bodyTextLength: text(document.body).length
      },
      error: isExplorePage() ? "" : `Source 页面不是${contentTypeLabel()} Explore。`
    };
  };

  const openFilterGroup = request => {
    const args = request || {};
    const title = String(args.title || "").trim();
    const group = findFilterGroup(title);
    if (!group) return { ok: false, title, error: `豆瓣原生筛选组不存在：${title}` };
    if (!group.classList.contains("expand")) {
      clickNative(group.querySelector(".base-selector-title") || group);
    }
    return { ok: true, title, filters: readFilterSnapshot() };
  };

  const selectFilter = request => {
    const args = request || {};
    const kind = String(args.kind || "").trim();
    const label = String(args.label || "").trim();
    if (!label) return { ok: false, error: "筛选项为空。" };
    if (kind === "primary" || kind === "secondary") {
      const node = simpleFilter(kind, label);
      if (!node) return { ok: false, error: `豆瓣原生筛选项不存在：${label}` };
      const beforeSignature = cardSignature();
      if (nativeOptionSelected(node)) return { ok: true, noOp: true, kind, label, beforeSignature };
      clickNative(node);
      return { ok: true, kind, label, beforeSignature };
    }
    if (kind === "group") {
      const title = String(args.title || "").trim();
      const group = findFilterGroup(title);
      if (!group) return { ok: false, error: `豆瓣原生筛选组不存在：${title}` };
      if (!group.classList.contains("expand")) clickNative(group.querySelector(".base-selector-title") || group);
      const option = visibleNativeOptions(group).find(node => text(node) === label);
      if (!option) return { ok: false, error: `豆瓣原生筛选选项不存在：${title} / ${label}` };
      const beforeSignature = cardSignature();
      const currentValue = nativeGroupValue(group, "");
      if (nativeOptionSelected(option) || currentValue === label) return { ok: true, noOp: true, kind, title, label, beforeSignature };
      clickNative(option);
      return { ok: true, kind, title, label, beforeSignature };
    }
    return { ok: false, error: "不支持的豆瓣原生筛选类型。" };
  };

  const loadMore = () => {
    const button = native.loadMore();
    if (!button) return { ok: false, end: true, error: "豆瓣当前没有下一页加载按钮。" };
    if (button.disabled || button.getAttribute("aria-disabled") === "true") return { ok: false, end: true, error: `豆瓣已经没有更多${contentTypeLabel()}了。` };
    const beforeSignature = cardSignature();
    clickNative(button);
    return { ok: true, beforeSignature };
  };

  window.QbDoubanSourceBridge = Object.freeze({ readPage, openFilterGroup, selectFilter, loadMore });
  const ready = () => { if (isExplorePage()) window.chrome?.webview?.postMessage({ type: "doubanSourceReady", url: location.href, contentType: contentType(), explore: true }); };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", ready, { once: true });
  else ready();
})();
