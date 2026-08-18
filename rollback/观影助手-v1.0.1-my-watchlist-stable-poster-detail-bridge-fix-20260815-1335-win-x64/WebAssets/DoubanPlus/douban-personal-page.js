(() => {
  "use strict";

  const ROOT_ID = "qb-douban-personal-root";
  const STYLE_ID = "qb-douban-personal-style";
  const RESTORE_KEY_PREFIX = "qb-douban-personal-state:";
  const WATCHLIST_QUERY = "qb_watchlist";
  const cssText = __QB_DOUBAN_PERSONAL_CSS__;
  const STATUS_META = {
    collect: { label: "看过", title: "我看过的影视", path: "collect" },
    wish: { label: "想看", title: "我想看的影视", path: "wish" },
    do: { label: "在看", title: "我在看的影视", path: "do" }
  };
  const COUNTRY_LABELS = Array.isArray(window.__qbDoubanCountryLabels) ? window.__qbDoubanCountryLabels : [];
  const COUNTRY_LABELS_BY_LENGTH = [...COUNTRY_LABELS].sort((left, right) => right.length - left.length);
  const GENRE_LABELS = ["剧情", "喜剧", "爱情", "动作", "科幻", "动画", "悬疑", "惊悚", "犯罪", "冒险", "音乐", "历史", "奇幻", "恐怖", "战争", "传记", "歌舞", "武侠", "情色", "灾难", "西部", "纪录片", "短片", "家庭", "儿童", "古装", "运动", "真人秀", "脱口秀", "同性", "黑色电影"];
  const LANGUAGE_LABELS = ["汉语普通话", "英语", "粤语", "闽南语", "马来语", "日语", "韩语", "法语", "德语", "西班牙语", "手语"];

  const text = element => String(element?.textContent || "").replace(/\s+/gu, " ").trim();
  const safeUrl = raw => {
    try {
      const url = new URL(raw, location.href);
      if (url.protocol !== "https:" || url.hostname !== "movie.douban.com") return null;
      return url;
    } catch {
      return null;
    }
  };
  const routeFromUrl = raw => {
    const url = safeUrl(raw);
    if (!url) return null;
    const match = url.pathname.match(/^\/people\/(\d+)\/(collect|wish|do)\/?$/u);
    if (!match) return null;
    return { profileId: match[1], status: match[2], meta: STATUS_META[match[2]], url };
  };
  const route = () => routeFromUrl(location.href);
  const isWatchlistPage = () => new URL(location.href).searchParams.get(WATCHLIST_QUERY) === "1";
  const subjectIdFromUrl = url => {
    const match = url.pathname.match(/^\/subject\/(\d+)\/?$/u);
    return match ? match[1] : "";
  };
  const imageUrl = image => image?.getAttribute("src") || image?.getAttribute("data-src") || "";
  const absoluteUrl = (raw, baseUrl = location.href) => {
    try {
      return raw ? new URL(raw, baseUrl).href : "";
    } catch {
      return "";
    }
  };
  const yearFromIntro = intro => text(intro).match(/\b(?:19|20)\d{2}\b/u)?.[0] || "";
  const numberFromText = value => {
    const match = String(value || "").replace(/,/gu, "").match(/\d+(?:\.\d+)?/u);
    return match ? Number(match[0]) : null;
  };
  const unique = values => [...new Set(values.filter(Boolean))];
  const introParts = intro => text(intro).split(/\s+\/\s+/u).map(value => value.trim()).filter(Boolean);
  const labelsInOrder = (parts, labels) => unique(parts.flatMap(part => labels
    .map(label => ({ label, index: part.indexOf(label) }))
    .filter(found => found.index >= 0)
    .sort((left, right) => left.index - right.index)
    .map(found => found.label)));
  const isDatePart = part => /(?:19|20)\d{2}/u.test(part);
  const extractCountryLabels = parts => unique(parts.flatMap(part => {
    const raw = String(part || "");
    let cursor = 0;
    const candidates = COUNTRY_LABELS_BY_LENGTH
      .map(label => ({ label, index: raw.indexOf(label) }))
      .filter(found => found.index >= 0)
      .sort((left, right) => left.index - right.index || right.label.length - left.label.length);
    return candidates.filter(found => {
      if (found.index < cursor) return false;
      cursor = found.index + found.label.length;
      return true;
    }).map(found => found.label);
  }));
  const isUrlPart = part => /^(?:https?:\/\/|www\.)/iu.test(String(part || "").trim());
  const countryLabelsInPart = part => extractCountryLabels([part]);
  const isCountryPart = part => countryLabelsInPart(part).length > 0;
  const isCountryOnlyPart = part => {
    if (!isCountryPart(part) || isDatePart(part) || isUrlPart(part)) return false;
    return countryLabelsInPart(part).join("") === String(part || "").replace(/\s+/gu, "");
  };
  const isMetadataPart = part => isDatePart(part) || isCountryPart(part) || isUrlPart(part) || GENRE_LABELS.some(label => part.includes(label)) || /^\d+\s*分钟$/u.test(part) || LANGUAGE_LABELS.includes(part);
  const structuredCast = item => unique([
    ...[...item.querySelectorAll(".actors a, .cast a, [rel='v:starring']")].map(text),
    text(item.querySelector("[rel='v:starring']"))
  ]).filter(value => value && !isMetadataPart(value) && value !== text(item.querySelector(".title")));
  const castFromIntro = (parts, dateIndex) => {
    const countryIndices = parts
      .map((part, index) => ({ part, index }))
      .filter(found => found.index > dateIndex && isCountryOnlyPart(found.part))
      .map(found => found.index);
    const clean = values => values.filter(part => !isMetadataPart(part));
    if (countryIndices.length) {
      const beforeCountry = clean(parts.slice(dateIndex + 1, countryIndices[0]));
      if (beforeCountry.length) return beforeCountry;
    }
    const genreIndices = parts
      .map((part, index) => ({ part, index }))
      .filter(found => found.index > dateIndex && GENRE_LABELS.some(label => found.part.includes(label)))
      .map(found => found.index);
    const lastGenreIndex = genreIndices.length ? Math.max(...genreIndices) : -1;
    return lastGenreIndex >= 0 ? clean(parts.slice(lastGenreIndex + 1)) : [];
  };
  const countryFromDate = parts => {
    const raw = parts.find(part => /(?:19|20)\d{2}[^/]*\([^)]*\)/u.test(part))?.match(/\(([^)]+)\)/u)?.[1] || "";
    const normalized = raw.replace(/\s+/gu, "");
    return extractCountryLabels([raw]).find(label => label === normalized) || "";
  };
  const directorBoundary = (parts, dateIndex) => {
    for (let index = dateIndex + 1; index < parts.length; index += 1) {
      if (!isCountryOnlyPart(parts[index]) || (index > 0 && isCountryOnlyPart(parts[index - 1]))) continue;
      let endIndex = index;
      while (endIndex + 1 < parts.length && isCountryOnlyPart(parts[endIndex + 1])) endIndex += 1;
      const priorPeople = parts.slice(dateIndex + 1, index).filter(part => !isMetadataPart(part));
      if (priorPeople.length) return { startIndex: index, endIndex };
    }
    return null;
  };
  const directorFromIntro = (parts, dateIndex, title = "") => {
    const boundary = directorBoundary(parts, dateIndex);
    if (!boundary) return [];
    const countryEndIndex = boundary.endIndex;
    for (const part of parts.slice(countryEndIndex + 1)) {
      const candidate = String(part || "").trim();
      if (!candidate || isMetadataPart(candidate) || candidate === title) continue;
      return [candidate];
    }
    return [];
  };
  const personalFields = (item, intro, title = "") => {
    const parts = introParts(intro);
    const year = yearFromIntro(intro);
    const countries = unique([countryFromDate(parts), ...extractCountryLabels(parts.filter(part => !isDatePart(part)))]);
    const genres = labelsInOrder(parts, GENRE_LABELS);
    const dateIndex = parts.findIndex(part => isDatePart(part));
    let cast = structuredCast(item);
    if (!cast.length && dateIndex >= 0) cast = castFromIntro(parts, dateIndex);
    const directors = directorFromIntro(parts, dateIndex, title);
    const allCast = unique(cast);
    return {
      identity: [year, countries.slice(0, 3).join(" / ")].filter(Boolean).join(" / "),
      genre: genres.join(" / "),
      countries,
      genres,
      cast: allCast.slice(0, 2),
      director: directors[0] || "",
      directors
    };
  };

  function writeProbe(page, itemCount, error = "", state = {}) {
    window.__qbDoubanPersonalProbe = {
      mounted: true,
      profileId: page?.profileId || "",
      status: page?.status || "",
      itemCount,
      loadedItemCount: itemCount,
      loading: Boolean(state.loading),
      nextPageUrl: state.nextPageUrl || "",
      href: location.href,
      error
    };
  }

  function pageFailure(sourceDocument = document) {
    const bodyText = text(sourceDocument.body);
    if (sourceDocument.querySelector("#login-form, form[action*='/login'], .captcha, [class*='captcha'], iframe[src*='captcha']")) {
      return "豆瓣登录或验证码页面，未读取个人列表。";
    }
    if (!sourceDocument.querySelector("#content, .article")) {
      return /无法访问|无法打开|网络|错误|ERR_/u.test(bodyText)
        ? "豆瓣个人页网络加载失败。"
        : "豆瓣个人页结构未加载。";
    }
    return "";
  }

  function readScore(item) {
    const node = item.querySelector("[data-score], .rating_num, .rating-value, .score");
    if (!node) return null;
    const raw = node.getAttribute("data-score") || text(node);
    const score = numberFromText(raw);
    return score !== null && score >= 0 && score <= 10 ? score : null;
  }

  function readRatingCount(item) {
    const node = item.querySelector("[data-rating-count], .rating_people, .rating-count, .votes");
    if (!node) return null;
    const raw = node.getAttribute("data-rating-count") || text(node);
    const count = numberFromText(raw);
    return count !== null ? Math.round(count) : null;
  }

  function readPersonalRating(item) {
    const node = item.querySelector("[class*='rating'][class$='-t']");
    const match = String(node?.className || "").match(/rating([0-5])-t/u);
    return match ? Number(match[1]) : null;
  }

  function readItem(item, status, baseUrl = location.href) {
    const subjectAnchor = item.querySelector(".pic a[href*='/subject/'], .title a[href*='/subject/'], a.title[href*='/subject/'], a[href*='/subject/']");
    const subjectUrl = safeUrl(subjectAnchor?.getAttribute("href") || subjectAnchor?.href || "");
    const subjectId = subjectUrl ? subjectIdFromUrl(subjectUrl) : "";
    if (!subjectUrl || !subjectId) return null;

    const titleAnchor = item.querySelector(".title a[href*='/subject/'], a.title[href*='/subject/'], .title[href*='/subject/']") || subjectAnchor;
    const titleSource = text(titleAnchor?.querySelector("em")) || text(titleAnchor) || titleAnchor?.getAttribute("title") || titleAnchor?.getAttribute("aria-label") || text(item.querySelector(".title"));
    const title = titleSource.replace(/\s*\[可播放\]\s*$/u, "").split(/\s*\/\s*/u)[0].trim() || `豆瓣条目 ${subjectId}`;
    const intro = item.querySelector(".intro");
    const parsed = personalFields(item, intro, title);
    return {
      subjectId,
      subjectUrl: subjectUrl.href,
      posterUrl: absoluteUrl(imageUrl(item.querySelector(".pic img")), baseUrl),
      title,
      year: yearFromIntro(intro),
      identity: parsed.identity,
      genre: parsed.genre,
      countries: parsed.countries,
      genres: parsed.genres,
      cast: parsed.cast,
      director: parsed.director,
      directors: parsed.directors,
      contentType: new URL(baseUrl, location.href).searchParams.get("type") === "tv" ? "tv" : "movie",
      score: readScore(item),
      ratingCount: readRatingCount(item),
      myRating: readPersonalRating(item),
      status,
      markedDate: text(item.querySelector(".date")),
      comment: text(item.querySelector(".comment")),
      intro: text(intro)
    };
  }

  function readItems(sourceDocument, page, baseUrl) {
    return [...sourceDocument.querySelectorAll(".grid-view .item")]
      .map(item => readItem(item, page.status, baseUrl))
      .filter(Boolean);
  }

  function linksFor(sourceDocument, selector, predicate = () => true) {
    return [...sourceDocument.querySelectorAll(selector)].map(anchor => {
      const url = safeUrl(anchor.getAttribute("href") || anchor.href || "");
      return url && predicate(url, anchor) ? { href: url.href, label: text(anchor), url } : null;
    }).filter(Boolean);
  }

  function renderLink(link, className = "") {
    const anchor = document.createElement("a");
    anchor.href = link.href;
    anchor.textContent = link.label || "—";
    if (className) anchor.className = className;
    return anchor;
  }

  function statusLinks(page) {
    return Object.entries(STATUS_META).map(([status, meta]) => ({
      href: `https://movie.douban.com/people/${page.profileId}/${meta.path}`,
      label: meta.label,
      status
    }));
  }

  function typeLinks(page) {
    const current = new URL(location.href);
    const links = [];
    const allUrl = new URL(current.href);
    allUrl.pathname = `/people/${page.profileId}/${page.status}`;
    allUrl.searchParams.delete("type");
    if (!allUrl.searchParams.has("filter")) allUrl.searchParams.set("filter", "all");
    links.push({ href: allUrl.href, label: "全部", type: "all" });

    for (const label of ["电影", "电视"]) {
      const native = [...document.querySelectorAll(".tabs-more-list a[href]")]
        .map(anchor => ({ anchor, url: safeUrl(anchor.getAttribute("href") || anchor.href || ""), label: text(anchor) }))
        .find(candidate => candidate.url && candidate.label === label && candidate.url.pathname.endsWith(`/${page.status}`));
      if (native) links.push({ href: native.url.href, label, type: label === "电影" ? "movie" : "tv" });
    }

    return [...new Map(links.map(link => [link.href, link])).values()];
  }

  function paginationLinks(sourceDocument = document) {
    const paginator = sourceDocument.querySelector(".paginator");
    if (!paginator) return [];
    return linksFor(sourceDocument, ".paginator a[href]");
  }

  function pageScopeKey(raw) {
    const url = raw instanceof URL ? new URL(raw.href) : new URL(raw, location.href);
    url.searchParams.delete("start");
    for (const [key, defaultValue] of [["sort", "time"], ["type", "all"], ["filter", "all"], ["mode", "grid"]]) {
      if (url.searchParams.get(key) === defaultValue) url.searchParams.delete(key);
    }
    url.searchParams.sort();
    return `${url.pathname}?${url.searchParams.toString()}`;
  }

  function isCompatiblePersonalUrl(raw, page, referenceUrl = location.href) {
    const candidate = routeFromUrl(raw);
    const reference = routeFromUrl(referenceUrl);
    return Boolean(candidate && reference && candidate.profileId === page.profileId && candidate.status === page.status && pageScopeKey(candidate.url) === pageScopeKey(reference.url));
  }

  function nextPageUrl(sourceDocument, page, referenceUrl = location.href) {
    const paginator = sourceDocument.querySelector(".paginator");
    if (!paginator) return "";
    const next = [...paginator.querySelectorAll("a[href]")].find(anchor => {
      const className = String(anchor.parentElement?.className || anchor.className || "");
      return /\bnext\b/u.test(className) || /^(?:后页|下一页|next)$/iu.test(text(anchor));
    });
    const url = next ? safeUrl(next.getAttribute("href") || next.href || "") : null;
    return url && url.href !== referenceUrl && isCompatiblePersonalUrl(url, page, referenceUrl) ? url.href : "";
  }

  function readRestoreState(profileId, page) {
    try {
      const key = `${RESTORE_KEY_PREFIX}${profileId}`;
      const saved = JSON.parse(sessionStorage.getItem(key) || "null");
      if (!saved || saved.href !== location.href || saved.profileId !== profileId || saved.status !== page.status || saved.scope !== pageScopeKey(location.href)) return null;
      if (!saved.subjectId && !Number.isFinite(saved.scrollY)) return null;
      return { key, saved };
    } catch {
      return null;
    }
  }

  function findPersonalCard(root, subjectId) {
    if (!subjectId) return null;
    return [...root.querySelectorAll(".qb-personal-card")].find(card => card.dataset.subjectId === subjectId) || null;
  }

  function applyScrollRestore(root, saved) {
    const card = findPersonalCard(root, saved.subjectId);
    if (card && Number.isFinite(saved.viewportTop)) {
      const delta = card.getBoundingClientRect().top - saved.viewportTop;
      window.scrollTo(0, Math.max(0, window.scrollY + delta));
      return true;
    }
    if (Number.isFinite(saved.scrollY)) {
      window.scrollTo(0, Math.max(0, saved.scrollY));
      return true;
    }
    return false;
  }

  async function restorePersonalScroll(profileId, page, root, state, loadNext) {
    const pending = readRestoreState(profileId, page);
    if (!pending) return;
    const { key, saved } = pending;
    let restorePages = 0;
    while (saved.subjectId && !findPersonalCard(root, saved.subjectId) && state.nextPageUrl && !state.exhausted && !state.lastError && restorePages < 60) {
      const previousUrl = state.nextPageUrl;
      await loadNext();
      restorePages += 1;
      if (state.lastError || state.nextPageUrl === previousUrl) break;
    }
    await new Promise(resolve => requestAnimationFrame(resolve));
    applyScrollRestore(root, saved);
    requestAnimationFrame(() => {
      applyScrollRestore(root, saved);
      setTimeout(() => {
        applyScrollRestore(root, saved);
        sessionStorage.removeItem(key);
      }, 450);
    });
  }

  function rememberAndOpen(item, event) {
    const page = route();
    if (!window.chrome?.webview || !page || !item.subjectId || !item.subjectUrl) return;
    event.preventDefault();
    event.stopPropagation();
    try {
      const card = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
      sessionStorage.setItem(`${RESTORE_KEY_PREFIX}${page.profileId}`, JSON.stringify({
        href: location.href,
        profileId: page.profileId,
        status: page.status,
        scope: pageScopeKey(location.href),
        subjectId: item.subjectId,
        scrollY: window.scrollY,
        viewportTop: card?.getBoundingClientRect().top
      }));
    } catch { }
    window.chrome.webview.postMessage({
      type: "doubanPersonalOpenSubject",
      subjectId: item.subjectId,
      subjectUrl: item.subjectUrl,
      personalUrl: location.href,
      profileId: page.profileId,
      status: page.status,
      scrollY: window.scrollY
    });
  }

  function appendNav(parent, links, activePredicate) {
    for (const link of links) {
      const anchor = renderLink(link);
      if (activePredicate(link)) anchor.classList.add("qb-active");
      parent.append(anchor);
    }
  }

  function createCard(item, page) {
    const card = QbDoubanCard.render({
      model: {
        subjectId: item.subjectId,
        subjectUrl: item.subjectUrl,
        title: item.title,
        posterUrl: item.posterUrl,
        identity: item.identity,
        infoRows: [
          item.genre ? { label: "类型", value: item.genre } : null,
          item.director ? { label: "导演", value: item.director } : null,
          item.cast.length ? { label: "主演", value: item.cast.slice(0, 2).join(" / ") } : null
        ].filter(Boolean),
        comment: item.comment,
        score: item.score || (item.myRating ? "★".repeat(item.myRating) : ""),
      },
      cardClass: "qb-personal-card",
      posterClass: "qb-personal-poster",
      bodyClass: "qb-personal-card-body",
      titleClass: "qb-personal-card-title",
      identityClass: "qb-personal-card-year",
      contextClass: "qb-personal-comment",
      metaClass: "qb-personal-card-meta",
      onOpen: event => rememberAndOpen(item, event)
    });
    return card;
  }

  function ensureGrid(content, page) {
    let grid = content.querySelector(".qb-personal-grid");
    if (grid) return grid;
    content.querySelector(".qb-personal-empty:not(.qb-personal-error)")?.remove();
    grid = document.createElement("section");
    grid.className = "qb-personal-grid";
    grid.setAttribute("aria-label", `${page.meta.label}影片列表`);
    content.prepend(grid);
    return grid;
  }

  function appendItems(content, items, page, seen) {
    const unique = items.filter(item => {
      if (!item.subjectId || seen.has(item.subjectId)) return false;
      seen.add(item.subjectId);
      return true;
    });
    if (!unique.length) return 0;
    const grid = ensureGrid(content, page);
    for (const item of unique) grid.append(createCard(item, page));
    return unique.length;
  }

  function mount(items, page, error = "") {
    if (document.getElementById(ROOT_ID)) return true;

    try { sessionStorage.setItem("qb-douban-personal-url-v1", location.href); } catch { /* storage is optional */ }

    const root = document.createElement("main");
    root.id = ROOT_ID;
    root.dataset.profileId = page.profileId;
    root.dataset.status = page.status;
    root.dataset.page = location.href;

    const header = document.createElement("header");
    header.className = "qb-personal-header";
    const heading = document.createElement("div");
    const kicker = document.createElement("p");
    kicker.className = "qb-personal-kicker";
    kicker.textContent = isWatchlistPage() ? "Douban Plus / Watchlist" : "Douban Plus / Personal";
    const title = document.createElement("h1");
    title.className = "qb-personal-title";
    title.textContent = isWatchlistPage() ? "我的待看" : page.meta.title;
    heading.append(kicker, title);
    const searchHost = document.createElement("div");
    searchHost.className = "qb-personal-search-host";
    searchHost.dataset.qbDoubanPlusSearchHost = "true";
    header.append(heading, searchHost);

    const statusNav = document.createElement("nav");
    statusNav.className = "qb-personal-primary-nav";
    statusNav.setAttribute("aria-label", "豆瓣个人状态");
    for (const link of statusLinks(page)) {
      const anchor = renderLink(link, "qb-personal-status-tab");
      anchor.setAttribute("role", "tab");
      anchor.setAttribute("aria-selected", link.status === page.status ? "true" : "false");
      if (link.status === page.status) anchor.classList.add("qb-active");
      statusNav.append(anchor);
    }
    const exploreLink = document.createElement("a");
    exploreLink.className = "qb-personal-status-tab qb-personal-explore-tab";
    exploreLink.href = "https://movie.douban.com/explore";
    exploreLink.textContent = "探索";
    exploreLink.setAttribute("role", "tab");
    exploreLink.setAttribute("aria-selected", "false");
    statusNav.append(exploreLink);

    const filterBar = document.createElement("div");
    filterBar.className = "qb-personal-filter-bar";
    const typeNav = document.createElement("nav");
    typeNav.className = "qb-personal-type-nav";
    typeNav.setAttribute("aria-label", "影片类型");
    const typeLabel = document.createElement("span");
    typeLabel.className = "qb-personal-toolbar-label";
    typeLabel.textContent = "类型";
    typeNav.append(typeLabel);
    const currentType = new URL(location.href).searchParams.get("type") || "all";
    appendNav(typeNav, typeLinks(page), link => link.type === currentType);
    filterBar.append(typeNav);

    const content = document.createElement("section");
    content.className = "qb-personal-content";
    if (error) {
      const failure = document.createElement("div");
      failure.className = "qb-personal-empty qb-personal-error";
      failure.textContent = error;
      content.append(failure);
    } else if (!items.length) {
      const empty = document.createElement("div");
      empty.className = "qb-personal-empty";
      empty.textContent = `${page.meta.label}列表暂无条目。`;
      content.append(empty);
    } else {
      appendItems(content, items, page, new Set());
    }

    const infinite = document.createElement("div");
    infinite.className = "qb-personal-infinite";
    infinite.hidden = Boolean(error);
    const infiniteStatus = document.createElement("span");
    infiniteStatus.className = "qb-personal-infinite-status";
    infinite.append(infiniteStatus);
    const pager = document.createElement("nav");
    pager.className = "qb-personal-pager";
    pager.setAttribute("aria-label", "豆瓣原生分页");
    for (const link of paginationLinks()) pager.append(renderLink(link));
    const start = new URL(location.href).searchParams.get("start") || "0";
    const current = [...pager.querySelectorAll("a")].find(anchor => anchor.href.includes(`start=${start}`));
    current?.classList.add("qb-current");

    root.append(header, statusNav, filterBar, content, infinite, pager);
    document.body.prepend(root);
    document.body.classList.add("qb-douban-personal-enhanced");
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = cssText;
    (document.head || document.documentElement || document.body)?.append(style);
    const wrapper = document.querySelector("#wrapper");
    if (wrapper) wrapper.style.display = "none";
    writeProbe(page, items.length, error);
    return true;
  }

  function setupInfiniteScroll(initialItems, page, error = "") {
    if (error || isWatchlistPage()) return;
    const root = document.getElementById(ROOT_ID);
    const content = root?.querySelector(".qb-personal-content");
    const infinite = root?.querySelector(".qb-personal-infinite");
    const status = infinite?.querySelector(".qb-personal-infinite-status");
    if (!root || !content || !infinite || !status) return;

    const state = {
      loading: false,
      exhausted: false,
      lastError: "",
      nextPageUrl: nextPageUrl(document, page),
      requestedUrls: new Set([location.href]),
      seen: new Set(initialItems.map(item => item.subjectId).filter(Boolean)),
      loadedItemCount: initialItems.length
    };
    const sentinel = document.createElement("div");
    sentinel.className = "qb-personal-infinite-sentinel";
    sentinel.setAttribute("aria-hidden", "true");
    infinite.append(sentinel);
    let retryButton = null;

    const setStatus = (kind, message) => {
      infinite.dataset.state = kind;
      status.textContent = message;
      retryButton?.remove();
      retryButton = null;
      if (kind === "error") {
        retryButton = document.createElement("button");
        retryButton.type = "button";
        retryButton.className = "qb-personal-infinite-retry";
        retryButton.textContent = "重试";
        retryButton.addEventListener("click", () => loadNext(true));
        infinite.append(retryButton);
      }
    };

    const syncProbe = errorMessage => writeProbe(page, state.loadedItemCount, errorMessage, state);

    const loadNext = async (retry = false) => {
      if (state.loading || state.exhausted || !state.nextPageUrl) return;
      const requestUrl = state.nextPageUrl;
      if (state.requestedUrls.has(requestUrl) && !retry) {
        state.exhausted = true;
        setStatus("done", "已加载全部内容");
        syncProbe();
        return;
      }
      state.requestedUrls.add(requestUrl);
      state.loading = true;
      state.lastError = "";
      setStatus("loading", "正在加载下一页…");
      syncProbe();
      try {
        const response = await fetch(requestUrl, { credentials: "same-origin", headers: { Accept: "text/html" } });
        if (!response.ok) throw new Error(`下一页请求失败（HTTP ${response.status}）。`);
        const html = await response.text();
        const parsed = new DOMParser().parseFromString(html, "text/html");
        const parsedPage = routeFromUrl(requestUrl);
        const parsedError = pageFailure(parsed);
        if (!parsedPage || !isCompatiblePersonalUrl(requestUrl, page) || parsedError) {
          throw new Error(parsedError || "下一页不是当前个人状态页面。");
        }
        const nextItems = readItems(parsed, page, requestUrl);
        const added = appendItems(content, nextItems, page, state.seen);
        state.loadedItemCount += added;
        const followingUrl = nextPageUrl(parsed, page, requestUrl);
        state.nextPageUrl = followingUrl && !state.requestedUrls.has(followingUrl) ? followingUrl : "";
        state.exhausted = !state.nextPageUrl;
        state.loading = false;
        setStatus(state.exhausted ? "done" : "idle", state.exhausted ? "已加载全部内容" : (added ? "继续下滑加载更多" : "当前页没有新的条目，继续下滑加载更多"));
        syncProbe();
      } catch (requestError) {
        state.loading = false;
        state.lastError = String(requestError?.message || requestError || "下一页加载失败。");
        setStatus("error", state.lastError);
        syncProbe(state.lastError);
      }
    };

    const startInfiniteObserver = () => {
      if (typeof IntersectionObserver === "function") {
        const observer = new IntersectionObserver(entries => {
          if (entries.some(entry => entry.isIntersecting)) loadNext();
        }, { rootMargin: "0px 0px 720px 0px" });
        observer.observe(sentinel);
      } else {
        const onScroll = () => {
          if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 720) loadNext();
        };
        window.addEventListener("scroll", onScroll, { passive: true });
        onScroll();
      };
    };

    if (!state.nextPageUrl) {
      state.exhausted = true;
      setStatus("done", "已加载全部内容");
      syncProbe();
    } else {
      setStatus("idle", "继续下滑加载更多");
    }
    void restorePersonalScroll(page.profileId, page, root, state, loadNext)
      .catch(() => { })
      .finally(startInfiniteObserver);
  }

  function install() {
    const page = route();
    if (!page || window.top !== window) return;
    const error = pageFailure();
    const nativeItems = error ? [] : readItems(document, page, location.href);
    const items = [...new Map(nativeItems.filter(item => item.subjectId).map(item => [item.subjectId, item])).values()];
    if (!mount(items, page, error)) return;
    setupInfiniteScroll(items, page, error);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", install, { once: true });
  else install();
})();
