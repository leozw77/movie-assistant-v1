(() => {
  "use strict";

  const STATUS_META = {
    collect: { label: "看过" },
    wish: { label: "想看" },
    do: { label: "在看" }
  };
  const COUNTRY_LABELS = Array.isArray(window.__qbDoubanCountryLabels) ? window.__qbDoubanCountryLabels : [];
  const COUNTRY_LABELS_BY_LENGTH = [...COUNTRY_LABELS].sort((left, right) => right.length - left.length);
  const GENRE_LABELS = ["剧情", "喜剧", "爱情", "动作", "科幻", "动画", "悬疑", "惊悚", "犯罪", "冒险", "音乐", "历史", "奇幻", "恐怖", "战争", "传记", "歌舞", "武侠", "情色", "灾难", "西部", "纪录片", "短片", "家庭", "儿童", "古装", "运动", "真人秀", "脱口秀", "同性", "黑色电影"];
  const LANGUAGE_LABELS = ["汉语普通话", "英语", "粤语", "闽南语", "马来语", "日语", "韩语", "法语", "德语", "西班牙语", "手语"];
  const text = element => String(element?.textContent || "").replace(/\s+/gu, " ").trim();
  const decodeHref = raw => String(raw || "").replace(/&amp;/gu, "&");
  const safeUrl = raw => {
    try {
      const url = new URL(decodeHref(raw), location.href);
      return url.protocol === "https:" && url.hostname === "movie.douban.com" ? url : null;
    } catch { return null; }
  };
  const routeFromUrl = raw => {
    const url = safeUrl(raw);
    const match = url?.pathname.match(/^\/people\/(\d+)\/(collect|wish|do)\/?$/u);
    return match ? { profileId: match[1], status: match[2], url } : null;
  };
  const subjectIdFromUrl = url => url.pathname.match(/^\/subject\/(\d+)\/?$/u)?.[1] || "";
  const absoluteUrl = raw => {
    try { return raw ? new URL(raw, location.href).href : ""; } catch { return ""; }
  };
  const numberFromText = raw => {
    const match = String(raw || "").replace(/,/gu, "").match(/\d+(?:\.\d+)?/u);
    return match ? Number(match[0]) : null;
  };
  const yearFromIntro = intro => text(intro).match(/\b(?:19|20)\d{2}\b/u)?.[0] || "";
  const imageUrl = image => image?.getAttribute("src") || image?.getAttribute("data-src") || "";
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
  const countryFromDate = parts => {
    const raw = parts.find(part => /(?:19|20)\d{2}[^/]*\([^)]*\)/u.test(part))?.match(/\(([^)]+)\)/u)?.[1] || "";
    const normalized = raw.replace(/\s+/gu, "");
    return extractCountryLabels([raw]).find(label => label === normalized) || "";
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
    const allGenres = unique(genres);
    const allCast = unique(cast);
    return {
      identity: [year, countries.slice(0, 3).join(" / ")].filter(Boolean).join(" / "),
      genre: allGenres.join(" / "),
      countries,
      genres: allGenres,
      cast: allCast.slice(0, 2),
      director: directors[0] || "",
      directors
    };
  };
  const contentTypeFromUrl = raw => {
    try { return new URL(raw, location.href).searchParams.get("type") === "tv" ? "tv" : "movie"; }
    catch { return "movie"; }
  };

  const pageFailure = sourceDocument => {
    const bodyText = text(sourceDocument.body);
    if (sourceDocument.querySelector("#login-form, form[action*='/login'], .captcha, [class*='captcha'], iframe[src*='captcha']"))
      return "豆瓣登录或验证码页面，未读取个人列表。";
    if (!sourceDocument.querySelector("#content, .article"))
      return /无法访问|无法打开|网络|错误|ERR_/u.test(bodyText) ? "豆瓣个人页网络加载失败。" : "豆瓣个人页结构未加载。";
    return "";
  };

  const readScore = item => {
    const node = item.querySelector("[data-score], .rating_num, .rating-value, .score");
    if (!node) return null;
    const score = numberFromText(node.getAttribute("data-score") || text(node));
    return score !== null && score >= 0 && score <= 10 ? score : null;
  };
  const readRatingCount = item => {
    const node = item.querySelector("[data-rating-count], .rating_people, .rating-count, .votes");
    if (!node) return null;
    const count = numberFromText(node.getAttribute("data-rating-count") || text(node));
    return count === null ? null : Math.round(count);
  };
  const readPersonalRating = item => {
    const match = String(item.querySelector("[class*='rating'][class$='-t']")?.className || "").match(/rating([0-5])-t/u);
    return match ? Number(match[1]) : null;
  };
  const readItem = (item, status, baseUrl) => {
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
      contentType: contentTypeFromUrl(baseUrl),
      score: readScore(item),
      ratingCount: readRatingCount(item),
      myRating: readPersonalRating(item),
      status,
      statusLabel: STATUS_META[status]?.label || status,
      markedDate: text(item.querySelector(".date")),
      comment: text(item.querySelector(".comment")),
      intro: text(intro)
    };
  };
  const readItems = (sourceDocument, page, baseUrl) => [...sourceDocument.querySelectorAll(".grid-view .item")]
    .map(item => readItem(item, page.status, baseUrl)).filter(Boolean);
  const pageScopeKey = raw => {
    const url = new URL(raw, location.href);
    url.searchParams.delete("start");
    // Douban adds this presentation-only parameter to filter links but omits
    // it from paginator links. It must not make the next page look out of
    // scope after a native filter navigation.
    url.searchParams.delete("tags_sort");
    for (const [key, defaultValue] of [["sort", "time"], ["type", "all"], ["filter", "all"], ["mode", "grid"]])
      if (url.searchParams.get(key) === defaultValue) url.searchParams.delete(key);
    url.searchParams.sort();
    return `${url.pathname}?${url.searchParams.toString()}`;
  };
  const resetPersonalPageUrl = (raw, page) => {
    const url = safeUrl(raw);
    if (!url || !page) return null;
    const candidate = routeFromUrl(url.href);
    if (!candidate || candidate.profileId !== page.profileId || candidate.status !== page.status) return null;
    url.searchParams.set("start", "0");
    url.searchParams.set("mode", "grid");
    return url;
  };
  const optionSelected = (url, currentUrl) => Boolean(url && currentUrl && pageScopeKey(url) === pageScopeKey(currentUrl));
  const optionRank = (option, parameter, order) => {
    const value = safeUrl(option?.url)?.searchParams.get(parameter) || "";
    const rank = order.indexOf(value);
    return rank >= 0 ? rank : Number.MAX_SAFE_INTEGER;
  };
  const sortOptionsByNativeOrder = (options, parameter, order) => options.sort((left, right) =>
    optionRank(left, parameter, order) - optionRank(right, parameter, order));
  const readPersonalFilters = (sourceDocument, page, baseUrl) => {
    const currentUrl = safeUrl(baseUrl);
    if (!currentUrl || !page) return { groups: [] };
    const groups = [];
    const addTabsGroup = (node, title, resetParameter) => {
      if (!node) return;
      const currentLabel = text(node.querySelector(":scope > .lnk-tab-more span")) || "全部";
      const options = [];
      const currentOptionUrl = resetPersonalPageUrl(currentUrl.href, page);
      if (currentOptionUrl) {
        if (currentLabel === "全部") {
          currentOptionUrl.searchParams.set(resetParameter, "all");
          if (resetParameter === "filter") currentOptionUrl.searchParams.delete("tag");
        }
        options.push({ label: currentLabel, url: currentOptionUrl.href, selected: true });
      }
      if (currentLabel !== "全部") {
        const allUrl = resetPersonalPageUrl(currentUrl.href, page);
        if (allUrl) {
          allUrl.searchParams.set(resetParameter, "all");
          if (resetParameter === "filter") allUrl.searchParams.delete("tag");
          options.push({ label: "全部", url: allUrl.href, selected: optionSelected(allUrl, currentUrl) });
        }
      }
      for (const anchor of node.querySelectorAll(":scope > .tabs-more-list a[href]")) {
        const url = resetPersonalPageUrl(anchor.href || anchor.getAttribute("href"), page);
        const label = text(anchor);
        if (!url || !label) continue;
        options.push({ label, url: url.href, selected: optionSelected(url, currentUrl) });
      }
      const uniqueOptions = [...new Map(options.map(option => [option.url, option])).values()];
      sortOptionsByNativeOrder(uniqueOptions, resetParameter, resetParameter === "filter"
        ? ["all", "schedule", "video"]
        : ["all", "movie", "tv"]);
      if (uniqueOptions.length) {
        const selected = uniqueOptions.find(option => option.selected);
        groups.push({ title, value: selected?.label || currentLabel, options: uniqueOptions });
      }
    };

    for (const node of sourceDocument.querySelectorAll("#content .tabs-more")) {
      const label = text(node.querySelector(":scope > .gray")).replace(/[：:]$/u, "");
      if (label === "筛选影片") addTabsGroup(node, label, "filter");
      if (label === "影片类型") addTabsGroup(node, label, "type");
    }

    const sortNode = sourceDocument.querySelector("#content .sort");
    if (sortNode) {
      const options = [];
      const currentSort = currentUrl.searchParams.get("sort") || "time";
      const currentSortLabel = [...sortNode.childNodes]
        .filter(node => node.nodeType === Node.TEXT_NODE)
        .map(node => text(node))
        .filter(Boolean)
        .join(" ") || ({ time: "按时间排序", rating: "按评价排序", title: "按标题排序" }[currentSort] || "按时间排序");
      const currentSortUrl = resetPersonalPageUrl(currentUrl.href, page);
      if (currentSortUrl) {
        currentSortUrl.searchParams.set("sort", currentSort);
        options.push({ label: currentSortLabel, url: currentSortUrl.href, selected: true });
      }
      for (const anchor of sortNode.querySelectorAll("a[href]")) {
        const url = safeUrl(anchor.href || anchor.getAttribute("href"));
        const sort = url?.searchParams.get("sort");
        const label = text(anchor);
        const normalizedUrl = url ? resetPersonalPageUrl(url.href, page) : null;
        if (normalizedUrl && sort && label) options.push({ label, url: normalizedUrl.href, selected: optionSelected(normalizedUrl, currentUrl) });
      }
      const uniqueOptions = [...new Map(options.map(option => [option.url, option])).values()];
      sortOptionsByNativeOrder(uniqueOptions, "sort", ["time", "rating", "title"]);
      if (uniqueOptions.length) {
        const selected = uniqueOptions.find(option => option.selected);
        groups.push({ title: "排序", value: selected?.label || currentSortLabel, options: uniqueOptions });
      }
    }
    return { groups };
  };
  const compatible = (raw, page, referenceUrl) => {
    const candidate = routeFromUrl(raw);
    const reference = routeFromUrl(referenceUrl);
    return Boolean(candidate && reference && candidate.profileId === page.profileId && candidate.status === page.status && pageScopeKey(candidate.url) === pageScopeKey(reference.url));
  };
  const nextPageUrl = (sourceDocument, page, referenceUrl) => {
    const next = [...sourceDocument.querySelectorAll(".paginator a[href]")].find(anchor => {
      const className = String(anchor.parentElement?.className || anchor.className || "");
      return /\bnext\b/u.test(className) || /^(?:后页|下一页|next)$/iu.test(text(anchor));
    });
    const url = next ? safeUrl(next.getAttribute("href") || next.href || "") : null;
    return url && url.href !== referenceUrl && compatible(url.href, page, referenceUrl) ? url.href : "";
  };
  const signature = items => items.map(item => `${item.subjectId}:${item.title}`).join("|");
  const state = {
    scope: "",
    page: null,
    items: [],
    nextPageUrl: "",
    requestedUrls: new Set(),
    loading: false,
    ready: false,
    error: ""
  };

  const buildPage = (sourceDocument, url) => {
    const page = routeFromUrl(url);
    const failure = page ? pageFailure(sourceDocument) : "豆瓣个人页地址无效。";
    const items = page && !failure ? readItems(sourceDocument, page, url) : [];
    return { page, failure, items, nextPageUrl: page && !failure ? nextPageUrl(sourceDocument, page, url) : "" };
  };
  const postResult = payload => window.chrome?.webview?.postMessage({ type: "doubanSourceResult", ...payload });

  const readPage = request => {
    const url = location.href;
    const built = buildPage(document, url);
    const page = built.page;
    const scope = page ? pageScopeKey(url) : "";
    if (scope !== state.scope || state.page?.status !== page?.status || state.page?.profileId !== page?.profileId) {
      state.scope = scope;
      state.page = page;
      state.items = built.items;
      state.nextPageUrl = built.nextPageUrl;
      state.requestedUrls = new Set(url ? [url] : []);
    } else if (!state.items.length && built.items.length) {
      state.items = built.items;
      state.nextPageUrl = built.nextPageUrl || state.nextPageUrl;
    }
    // The source WebView stays on page 1 while later pages are fetched in the
    // background. Once a later page has been consumed, the current DOM still
    // exposes the old page-2 link. Never let that stale link move the cursor
    // backwards or resurrect a page that was already requested.
    if (!state.nextPageUrl && built.nextPageUrl && !state.requestedUrls.has(built.nextPageUrl))
      state.nextPageUrl = built.nextPageUrl;
    state.ready = Boolean(page && !built.failure && document.querySelector("#content, .article"));
    state.error = built.failure;
    const result = {
      requestId: request?.requestId || "",
      mode: request?.mode || `personal-${page?.status || "unknown"}`,
      generation: Number(request?.generation || 0),
      url,
      contentType: "personal",
      personalStatus: page?.status || "",
      profileId: page?.profileId || "",
      pageReady: state.ready,
      items: state.items,
      paging: { hasMore: Boolean(state.nextPageUrl), label: "加载更多" },
      filters: readPersonalFilters(document, page, url),
      signature: signature(state.items),
      dom: { gridItemCount: document.querySelectorAll(".grid-view .item").length, paginator: Boolean(document.querySelector(".paginator")), ready: state.ready },
      error: state.error
    };
    postResult(result);
    return result;
  };

  const loadMoreAsync = async beforeSignature => {
    const requestUrl = state.nextPageUrl;
    try {
      if (!requestUrl || state.requestedUrls.has(requestUrl)) {
        state.nextPageUrl = "";
        return { ok: true, noOp: true, beforeSignature };
      }
      const response = await fetch(requestUrl, { credentials: "same-origin" });
      if (!response.ok) throw new Error(`下一页读取失败（HTTP ${response.status}）。`);
      const html = await response.text();
      const parsed = new DOMParser().parseFromString(html, "text/html");
      const page = state.page;
      const built = buildPage(parsed, requestUrl);
      if (!page || !built.page || built.page.profileId !== page.profileId || built.page.status !== page.status || built.failure)
        throw new Error(built.failure || "下一页个人列表范围不一致。");
      const seen = new Set(state.items.map(item => item.subjectId));
      for (const item of built.items) if (!seen.has(item.subjectId)) { state.items.push(item); seen.add(item.subjectId); }
      state.requestedUrls.add(requestUrl);
      state.nextPageUrl = built.nextPageUrl && !state.requestedUrls.has(built.nextPageUrl) ? built.nextPageUrl : "";
      return { ok: true, beforeSignature, changed: signature(state.items) !== beforeSignature };
    } catch (error) {
      state.error = String(error?.message || error);
      return { ok: false, error: state.error, beforeSignature };
    } finally { state.loading = false; }
  };

  // ExecuteScriptAsync receives the bridge return value synchronously. Start the
  // same-origin fetch here and let the host poll readPage until state.items changes.
  const loadMore = () => {
    if (state.loading) return { ok: false, error: "个人列表正在加载中。" };
    if (!state.nextPageUrl) return { ok: true, noOp: true, beforeSignature: signature(state.items) };
    const beforeSignature = signature(state.items);
    state.loading = true;
    void loadMoreAsync(beforeSignature);
    return { ok: true, pending: true, beforeSignature };
  };

  window.QbDoubanPersonalSourceBridge = Object.freeze({
    readPage,
    loadMore,
    openFilterGroup: () => ({ ok: false, error: "个人影片暂不支持 Explore 筛选。" }),
    selectFilter: () => ({ ok: false, error: "个人影片暂不支持 Explore 筛选。" })
  });
  window.chrome?.webview?.postMessage({ type: "doubanSourceReady", contentType: "personal" });
})();
