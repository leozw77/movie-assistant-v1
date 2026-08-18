(() => {
  "use strict";

  const ROOT_ID = "qb-douban-explore-root";
  const STYLE_ID = "qb-douban-explore-style";
  const STATE_KEY = "qb-douban-explore-state-v1";
  const PERSONAL_URL_KEY = "qb-douban-personal-url-v1";
  const EXPLORE_URL = /^https:\/\/movie\.douban\.com\/(?:explore|tv)\/?(?:\?.*)?$/u;
  if (window.top !== window || !EXPLORE_URL.test(location.href)) return;

  const isTvPage = () => new URL(location.href).pathname.replace(/\/+$/u, "") === "/tv";
  const contentType = () => isTvPage() ? "tv" : "movie";
  const contentTypeLabel = () => isTvPage() ? "电视剧" : "电影";
  const COUNTRY_LABELS = Array.isArray(window.__qbDoubanCountryLabels) ? window.__qbDoubanCountryLabels : [];
  const COUNTRY_LABELS_BY_LENGTH = [...COUNTRY_LABELS].sort((left, right) => right.length - left.length);
  const GENRE_LABELS = ["剧情", "喜剧", "爱情", "动作", "科幻", "动画", "悬疑", "惊悚", "犯罪", "冒险", "音乐", "历史", "奇幻", "恐怖", "战争", "传记", "歌舞", "武侠", "情色", "灾难", "西部", "纪录片", "短片", "家庭", "儿童", "古装", "运动", "真人秀", "脱口秀", "同性", "黑色电影"];
  const unique = values => [...new Set(values.filter(Boolean))];
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
  const parseExploreSubtitle = subtitle => {
    const parts = String(subtitle || "").split(/\s*\/\s*/u).map(value => value.trim()).filter(Boolean);
    const year = parts.find(part => /(?:19|20)\d{2}/u.test(part))?.match(/(?:19|20)\d{2}/u)?.[0] || "";
    const countries = extractCountryLabels(parts);
    const genreIndex = parts.findIndex((part, index) => index > 0 && GENRE_LABELS.some(label => part.includes(label)));
    const genrePart = genreIndex >= 0 ? parts[genreIndex] : "";
    const genres = unique(GENRE_LABELS.filter(label => genrePart.includes(label)));
    const director = genreIndex >= 0 && !isUrlPart(parts[genreIndex + 1]) ? parts[genreIndex + 1] || "" : "";
    const cast = genreIndex >= 0
      ? String(parts[genreIndex + 2] || "").split(/\s+/u).filter(value => value && !isUrlPart(value)).slice(0, 2)
      : [];
    return {
      year,
      countries,
      genres,
      genre: genres.join(" / "),
      director,
      directors: director ? [director] : [],
      cast,
      identity: [year, countries.slice(0, 3).join(" / ")].filter(Boolean).join(" / ")
    };
  };

  const cssText = __QB_DOUBAN_EXPLORE_CSS__;
  const expectedAllGroupTitles = () => isTvPage()
    ? ["类型", "地区", "年代", "平台", "排序"]
    : ["类型", "地区", "年代", "排序"];
  const allSelectorHost = () => document.querySelector(".explore-all-selectors-main");
  const allSelectorNodes = () => {
    const host = allSelectorHost();
    if (!host) return [];
    return [...host.querySelectorAll(".base-selector")]
      .filter(node => node.closest(".explore-all-selectors-main") === host &&
        !node.parentElement?.closest(".base-selector"));
  };
  const native = {
    wrapper: () => document.querySelector("#wrapper"),
    primary: () => [...document.querySelectorAll(".explore-menu li.explore-recent-hot-tag")],
    secondary: () => isAllMode()
      ? allSelectorNodes()
      : [...document.querySelectorAll(".explore-menu-second li.explore-menu-second-tag")],
    filterContainer: () => document.querySelector(".explore-filter-container, .explore-all"),
    scoreTitle: () => document.querySelector(".rating-range-title"),
    list: () => document.querySelector(".subject-list-list"),
    loadMore: () => [...document.querySelectorAll(".subject-list-main button")].find(button => /加载更多/u.test(button.textContent || ""))
  };

  const nativeCheckbox = value => [...(native.filterContainer()?.querySelectorAll('input[type="checkbox"]') || [])]
    .find(input => input.value === value) || null;
  const hasNativeCheckbox = value => Boolean(nativeCheckbox(value));

  const state = {
    nativeReady: false,
    loading: false,
    lastError: "",
    loadedItemCount: 0,
    loadGeneration: 0,
    loadCount: 0,
    endReached: false,
    endReason: "",
    lastLoadDurationMs: 0,
    lastLoadTrigger: "",
    lastNativeSignature: "",
    observer: null,
    scrollObserver: null,
    monitorTimer: 0,
    customFilterMenu: null,
    scoreModalCleanup: null,
    scoreRange: "",
    restoreStarted: false,
    filterGeneration: 0,
    filterOperationGeneration: 0,
    filterOperationBusy: false,
    filterPhase: "idle",
    filterContext: null,
    filterRenderTimers: new Set(),
    primarySelectionText: "",
    groupSelectionOverrides: new Map()
  };

  const text = node => String(node?.textContent || "").replace(/\s+/gu, " ").trim();
  const normalizeText = value => String(value || "").replace(/\s+/gu, " ").trim();
  const findNativePrimary = label => native.primary().find(node => text(node) === label) || null;
  const subjectUrl = subjectId => `https://movie.douban.com/subject/${subjectId}/`;
  const validId = value => /^\d+$/u.test(String(value || ""));
  const selected = node => {
    const aria = node?.getAttribute?.("aria-selected");
    return aria === "true" || node?.getAttribute?.("aria-checked") === "true" || node?.getAttribute?.("data-selected") === "true" || node?.getAttribute?.("data-active") === "true" ||
      [...(node?.classList || [])].some(value => /(?:selected|active|current|on)$/iu.test(value));
  };
  const selectedText = nodes => text(nodes.find(selected) || nodes[0]);
  const currentPrimary = () => native.primary().find(selected) || native.primary()[0] || null;
  const selectedPrimary = () => native.primary().find(selected) || null;
  const isAllMode = () => text(selectedPrimary()) === "全部";
  const nativeGroupTitle = node => {
    const title = node?.querySelector?.(".base-selector-title");
    return text(title?.querySelector?.("span") || title);
  };
  const allFilterGroups = () => {
    const nodes = native.secondary();
    const order = new Map(expectedAllGroupTitles().map((title, index) => [title, index]));
    return nodes.map((node, index) => ({ node, index, title: nativeGroupTitle(node) }))
      .filter(group => group.title)
      .sort((left, right) => (order.get(left.title) ?? Number.MAX_SAFE_INTEGER) - (order.get(right.title) ?? Number.MAX_SAFE_INTEGER));
  };
  const allFilterSignature = groups => groups.map(group => `${group.title}:${nativeGroupValue(group.node, "")}`).join("|");
  const allFilterShape = groups => groups.map(group => group.title).join("|");
  const expectedAllFilterShape = () => expectedAllGroupTitles().join("|");

  function findNativeGroup(title) {
    if (!title) return null;
    return allFilterGroups().find(group => group.title === title)?.node || null;
  }

  function nativeGroupValue(node, fallback = "筛选") {
    return text(node?.querySelector?.(".base-selector-selected") || node?.querySelector?.(".base-selector-title span") || node) || fallback;
  }

  function isDefaultNativeGroupValue(value, title) {
    const normalizedValue = normalizeText(value);
    return !normalizedValue || normalizedValue === title || normalizedValue === "全部" || normalizedValue === `全部${title}`;
  }

  function groupSelectionKey(groupTitle) {
    return `${contentType()}:${groupTitle}`;
  }

  function syncNativeGroupButton(button, group, node = group?.node, explicitValue = undefined) {
    if (!button || !group || !node) return;
    const value = explicitValue ?? state.groupSelectionOverrides.get(groupSelectionKey(group.title)) ?? nativeGroupValue(node, group.title);
    const active = !isDefaultNativeGroupValue(value, group.title);
    button.textContent = active ? `${group.title}：${value}` : group.title;
    button.classList.toggle("qb-active", active);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  }

  function nativeGroupExpanded(group) {
    return Boolean(group?.classList.contains("expand"));
  }

  function visibleNativeOptions(group) {
    const card = nativeGroupExpanded(group) ? group.querySelector(".expand-card") : null;
    return [...(card?.querySelectorAll(".drc-label") || [])]
      .filter(node => text(node));
  }

  function findNativeOption(group, label) {
    return visibleNativeOptions(group).find(node => text(node) === label) || null;
  }

  const nativeOptionSelected = option => Boolean(option && (selected(option) || selected(option.parentElement) || selected(option.closest("li"))));

  function filterSelectionSignature() {
    if (isAllMode()) {
      return allFilterGroups().map(group => `${group.title}=${nativeGroupValue(group.node, "")}`).join("|");
    }
    return selectedText(native.secondary());
  }

  function allFilterValues() {
    return Object.fromEntries(allFilterGroups().map(group => [group.title, nativeGroupValue(group.node, "")]));
  }

  function clearFilterRenderTimers() {
    for (const timer of state.filterRenderTimers) clearTimeout(timer);
    state.filterRenderTimers.clear();
  }

  function scheduleFilterRender(generation, callback, delay = 0) {
    const timer = window.setTimeout(() => {
      state.filterRenderTimers.delete(timer);
      if (generation === state.filterGeneration) callback();
    }, delay);
    state.filterRenderTimers.add(timer);
    return timer;
  }

  function disposeFilterContext(context, phase = "idle", reason = "cancelled") {
    if (!context) return;
    const ownsContext = state.filterContext === context;
    context.cancelled = true;
    context.cancelReason = reason;
    for (const timer of context.timers) clearTimeout(timer);
    context.timers.clear();
    for (const observer of context.observers) observer.disconnect();
    context.observers.clear();
    for (const waiter of context.waiters) waiter();
    context.waiters.clear();
    context.resultObserver = null;
    if (!ownsContext) return;
    state.filterContext = null;
    state.filterPhase = phase;
    state.filterOperationBusy = false;
    const root = document.getElementById(ROOT_ID);
    if (root) {
      root.dataset.filterState = phase;
      root.querySelectorAll("[data-native-group]").forEach(button => {
        button.disabled = false;
      });
    }
  }

  function cancelFilterContext(reason = "cancelled") {
    const context = state.filterContext;
    if (context) disposeFilterContext(context, "idle", reason);
  }

  function isCurrentFilterContext(context) {
    return Boolean(context && !context.cancelled && state.filterContext === context &&
      context.operationId === state.filterOperationGeneration && context.pageGeneration === state.filterGeneration);
  }

  function setFilterPhase(context, phase, extra = {}, { writeProbe = true } = {}) {
    if (context && !isCurrentFilterContext(context)) return false;
    state.filterPhase = phase;
    const root = document.getElementById(ROOT_ID);
    if (root) root.dataset.filterState = phase;
    if (writeProbe) writeProbe({ filterState: phase, ...extra });
    return true;
  }

  function contextWaitFor(context, condition, timeout = 1200, interval = 30) {
    return new Promise(resolve => {
      const started = Date.now();
      let timer = 0;
      let cancelled = false;
      const finish = value => {
        if (timer) context.timers.delete(timer);
        context.waiters.delete(cancel);
        resolve(value);
      };
      const cancel = () => {
        cancelled = true;
        finish(false);
      };
      context.waiters.add(cancel);
      const check = () => {
        if (cancelled || !isCurrentFilterContext(context)) {
          finish(false);
          return;
        }
        let passed = false;
        try { passed = Boolean(condition()); } catch { passed = false; }
        if (passed || Date.now() - started >= timeout) {
          finish(passed);
          return;
        }
        timer = window.setTimeout(check, interval);
        context.timers.add(timer);
      };
      check();
    });
  }

  function observeFilterDom(context) {
    const mutationOptions = {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ["class", "aria-selected", "aria-checked", "data-selected", "data-active", "disabled"]
    };
    const markChanged = (mutations = [], source = "host") => {
      if (!isCurrentFilterContext(context)) return;
      const currentList = native.list();
      const listMutation = source === "list" || Boolean(currentList && mutations.some(mutation =>
        mutation.target === currentList || currentList.contains(mutation.target)));
      if (listMutation) context.listDomVersion += 1;
      context.domVersion += 1;
      context.lastMutationAt = Date.now();
      context.filterContainer = native.filterContainer();
      context.list = currentList;
    };
    const reconnect = () => {
      if (!isCurrentFilterContext(context)) return;
      const filterParent = native.filterContainer()?.parentElement || null;
      const list = native.list();
      if (filterParent !== context.filterParent) {
        context.filterParentObserver?.disconnect();
        context.filterParent = filterParent;
        if (filterParent) {
          context.filterParentObserver = new MutationObserver(markChanged);
          context.filterParentObserver.observe(filterParent, mutationOptions);
          context.observers.add(context.filterParentObserver);
        }
      }
      if (list !== context.list) {
        if (context.list) context.listDomVersion += 1;
        context.listObserver?.disconnect();
        context.list = list;
        if (list) {
          context.listObserver = new MutationObserver(mutations => markChanged(mutations, "list"));
          context.listObserver.observe(list, mutationOptions);
          context.observers.add(context.listObserver);
        }
      }
    };
    const host = native.wrapper() || document.body;
    context.hostObserver = new MutationObserver(() => reconnect());
    context.hostObserver.observe(host, { childList: true, subtree: true });
    context.observers.add(context.hostObserver);
    reconnect();
    return reconnect;
  }

  function beginFilterContext(reason) {
    cancelFilterContext("superseded");
    clearFilterRenderTimers();
    const context = {
      operationId: ++state.filterOperationGeneration,
      pageGeneration: state.filterGeneration,
      reason,
      cancelled: false,
      cancelReason: "",
      timers: new Set(),
      observers: new Set(),
      waiters: new Set(),
      filterParent: null,
      filterParentObserver: null,
      list: null,
      listObserver: null,
      hostObserver: null,
      applying: false,
      filterContainer: native.filterContainer(),
      domVersion: 0,
      lastMutationAt: Date.now(),
      actionDomVersion: 0,
      listDomVersion: 0,
      actionListDomVersion: 0,
      actionStartedAt: 0,
      resultObserver: null
    };
    state.filterContext = context;
    observeFilterDom(context);
    return context;
  }

  function setFilterOperationBusy(busy, reason = "", { writeProbe = true } = {}) {
    state.filterOperationBusy = busy;
    const root = document.getElementById(ROOT_ID);
    if (root) {
      if (busy) root.dataset.filterState = state.filterPhase || "native-dom-settling";
      root.querySelectorAll("[data-native-group]").forEach(button => {
        button.disabled = busy;
      });
    }
    if (writeProbe) writeProbe({ filterState: busy ? state.filterPhase : "filter-ready", filterOperationReason: reason });
  }

  function nativeResultLoading() {
    const list = native.list();
    const host = list?.closest(".subject-list-main") || list?.parentElement || document.querySelector(".subject-list-main");
    if (!host) return false;
    if (host.matches(".loading, [aria-busy=\"true\"]")) return true;
    return [...host.querySelectorAll(".loading, [aria-busy=\"true\"], .spinner, .loader")]
      .some(node => node.offsetParent !== null || /加载中|正在加载/u.test(text(node)));
  }

  function nativeResultEmpty() {
    const list = native.list();
    const host = list?.closest(".subject-list-main") || list?.parentElement || document.querySelector(".subject-list-main");
    if (!host) return false;
    return /暂无|没有找到|无结果|没有符合|未找到/u.test(text(host));
  }

  function waitForFilterResult({ context, groupTitle, expectedValue, beforeFilterValues, beforeList, beforeItemsSignature, beforeListDomVersion, timeout = 5000 }) {
    return new Promise(resolve => {
      const started = Date.now();
      let cancelled = false;
      let deadlineTimer = 0;
      let checkTimer = 0;
      let finished = false;
      let resultMutated = false;
      for (const observer of context.observers) observer.disconnect();
      context.observers.clear();
      const finish = value => {
        if (finished) return;
        finished = true;
        if (deadlineTimer) {
          clearTimeout(deadlineTimer);
          context.timers.delete(deadlineTimer);
        }
        if (checkTimer) {
          clearTimeout(checkTimer);
          context.timers.delete(checkTimer);
        }
        context.resultObserver?.disconnect();
        context.resultObserver = null;
        context.waiters.delete(cancel);
        resolve(value);
      };
      const cancel = () => {
        cancelled = true;
        finish({ applied: false, cancelled: true });
      };
      const check = () => {
        if (cancelled || !isCurrentFilterContext(context)) {
          finish({ applied: false, cancelled: true });
          return;
        }
        const current = findNativeGroup(groupTitle);
        const selectedValue = nativeGroupValue(current, "");
        const selectionSignature = filterSelectionSignature();
        const targetSelected = selectedValue === expectedValue;
        const currentFilterValues = allFilterValues();
        const otherFiltersPreserved = Object.entries(beforeFilterValues).every(([title, value]) =>
          title === groupTitle || currentFilterValues[title] === value);
        if (targetSelected && otherFiltersPreserved) {
          const currentList = native.list();
          const currentItems = readItems();
          const currentItemsSignature = itemsSignature(currentItems);
          const listTransitioned = resultMutated || currentList !== beforeList ||
            currentItemsSignature !== beforeItemsSignature ||
            context.listDomVersion > beforeListDomVersion;
          const resultReady = listTransitioned && !nativeResultLoading() &&
            (currentItems.length > 0 || nativeResultEmpty());
          if (resultReady) {
            finish({ applied: true, items: currentItems, selectionSignature });
            return;
          }
        }
        if (Date.now() - started >= timeout) {
          finish({ applied: false, cancelled: false });
          return;
        }
      };
      const scheduleCheck = mutations => {
        const currentList = native.list();
        const touchesResult = mutations.some(mutation => {
          const target = mutation.target;
          const addedResultNode = [...mutation.addedNodes].some(node =>
            node === currentList || Boolean(node.contains?.(currentList)));
          return Boolean((currentList && (target === currentList || currentList.contains(target))) || addedResultNode);
        });
        if (!touchesResult || cancelled || finished || checkTimer) return;
        resultMutated = true;
        checkTimer = window.setTimeout(() => {
          context.timers.delete(checkTimer);
          checkTimer = 0;
          check();
        }, 0);
        context.timers.add(checkTimer);
      };
      context.waiters.add(cancel);
      const resultObserver = new MutationObserver(scheduleCheck);
      context.resultObserver = resultObserver;
      context.observers.add(resultObserver);
      const host = native.wrapper() || document.body;
      resultObserver.observe(host, {
        childList: true,
        subtree: true,
        characterData: true,
        attributes: true,
        attributeFilter: ["class", "aria-selected", "aria-checked", "data-selected", "data-active", "disabled"]
      });
      deadlineTimer = window.setTimeout(() => {
        context.timers.delete(deadlineTimer);
        check();
      }, timeout);
      context.timers.add(deadlineTimer);
      check();
    });
  }

  function parseCard(anchor) {
    const rawHref = anchor.getAttribute("href") || "";
    const match = rawHref.match(/(?:movie\/|uri=\/movie\/)(\d+)/u) || rawHref.match(/(?:tv\/|uri=\/tv\/)(\d+)/u) || rawHref.match(/\/subject\/(\d+)/u);
    const subjectId = match?.[1] || anchor.dataset.subjectId || "";
    if (!validId(subjectId)) return null;
    const card = anchor.querySelector(".drc-subject-card") || anchor;
    const title = text(card.querySelector(".drc-subject-info-title-text, h2, .title")) || `豆瓣条目 ${subjectId}`;
    const subtitle = text(card.querySelector(".drc-subject-info-subtitle, .subtitle"));
    const score = text(card.querySelector(".drc-rating-num, .rating_num"));
    const rating = card.querySelector("[data-rating]")?.getAttribute("data-rating") || "";
    const poster = card.querySelector("img.drc-cover-pic, img")?.currentSrc || card.querySelector("img")?.src || "";
    const parsed = parseExploreSubtitle(subtitle);
    return { subjectId, subjectUrl: subjectUrl(subjectId), title, subtitle, score, rating, poster, year: parsed.year, identity: parsed.identity, genre: parsed.genre, genres: parsed.genres, countries: parsed.countries, director: parsed.director, directors: parsed.directors, cast: parsed.cast, contentType: contentType() };
  }

  function readItems() {
    const seen = new Set();
    return [...(native.list()?.querySelectorAll("li > a[href]") || [])]
      .map(parseCard)
      .filter(item => {
        if (!item || seen.has(item.subjectId)) return false;
        seen.add(item.subjectId);
        return true;
      });
  }

  function itemsSignature(items = readItems()) {
    return items.map(item => `${item.subjectId}:${item.title}:${item.score}`).join(",");
  }

  function readState(items = null) {
    const filterContainer = native.filterContainer();
    const checks = [...(filterContainer?.querySelectorAll('input[type="checkbox"]') || [])];
    const score = scoreButtonLabel();
    const resolvedItems = items || readItems();
    return {
      href: location.href,
      primary: selectedText(native.primary()),
      secondary: selectedText(native.secondary()),
      uncollect: Boolean(checks.find(input => input.value === "uncollect")?.checked),
      playable: Boolean(checks.find(input => input.value === "playable")?.checked),
      score,
      loadedItemCount: resolvedItems.length,
      scrollY: window.scrollY || document.documentElement.scrollTop || 0,
      viewportTop: document.querySelector(`#${ROOT_ID}`)?.getBoundingClientRect().top || 0
    };
  }

  function writeProbe(extra = {}) {
    const { items: providedItems, selected: providedSelected, ...probeExtra } = extra;
    const items = providedItems || readItems();
    const selectedState = providedSelected || readState(items);
    window.__qbDoubanExploreProbe = {
      mounted: true,
      href: location.href,
      nativeReady: state.nativeReady,
      cardCount: items.length,
      loadedItemCount: state.loadedItemCount,
      loading: state.loading,
      error: state.lastError,
      autoInfinite: true,
      contentType: contentType(),
      loadCount: state.loadCount,
      endReached: state.endReached,
      endReason: state.endReason,
      lastLoadDurationMs: state.lastLoadDurationMs,
      lastLoadTrigger: state.lastLoadTrigger,
      hasNativeLoadMore: Boolean(native.loadMore()),
      availableCheckboxes: ["uncollect", "playable"].filter(hasNativeCheckbox),
      excludedHotList: !document.querySelector(".explore-doulist, .explore-hot-list, .doulist"),
      filterOperationBusy: state.filterOperationBusy,
      filterOperationGeneration: state.filterOperationGeneration,
      selected: selectedState,
      ...probeExtra
    };
    const root = document.getElementById(ROOT_ID);
    if (root) {
      root.dataset.loading = state.loading ? "true" : "false";
      root.dataset.cardCount = String(items.length);
      root.dataset.error = state.lastError;
    }
  }

  function updateLoadMoreControl() {
    const root = document.getElementById(ROOT_ID);
    const moreButton = root?.querySelector(".qb-explore-load-more");
    if (!moreButton) return;
    const loadMore = native.loadMore();
    moreButton.disabled = state.loading || state.endReached || !loadMore || Boolean(loadMore.disabled);
    moreButton.textContent = state.loading ? "正在加载…" : state.endReached || !loadMore ? "已加载全部" : "加载更多";
    root.querySelector(".qb-explore-footer")?.toggleAttribute("data-end", state.endReached || !loadMore);
  }

  function resetExplorePaging(reason) {
    state.loadGeneration += 1;
    state.loading = false;
    state.lastError = "";
    state.loadCount = 0;
    state.endReached = false;
    state.endReason = "";
    state.lastLoadDurationMs = 0;
    state.lastLoadTrigger = reason;
    state.lastNativeSignature = "";
    updateLoadMoreControl();
    writeProbe({ resetReason: reason });
  }

  function waitForNativeLoad(beforeSignature, generation, timeout = 12000) {
    return new Promise(resolve => {
      const started = Date.now();
      let changedAt = 0;
      const check = () => {
        if (generation !== state.loadGeneration) {
          resolve({ cancelled: true, changed: false });
          return;
        }
        const currentSignature = itemsSignature();
        if (currentSignature !== beforeSignature) {
          if (!changedAt) changedAt = Date.now();
          if (Date.now() - changedAt >= 180) {
            resolve({ cancelled: false, changed: true });
            return;
          }
        }
        if (Date.now() - started >= timeout) {
          resolve({ cancelled: false, changed: currentSignature !== beforeSignature });
          return;
        }
        setTimeout(check, 80);
      };
      check();
    });
  }

  async function loadNextExplorePage(trigger = "auto") {
    if (!state.nativeReady || state.loading || state.endReached) return false;
    const nativeButton = native.loadMore();
    if (!nativeButton) {
      state.endReached = true;
      state.endReason = "native-button-missing";
      updateLoadMoreControl();
      writeProbe();
      return false;
    }
    if (nativeButton.disabled) {
      state.endReached = true;
      state.endReason = "native-button-disabled";
      updateLoadMoreControl();
      writeProbe();
      return false;
    }
    const generation = state.loadGeneration;
    const beforeSignature = itemsSignature();
    const started = Date.now();
    state.loading = true;
    state.lastError = "";
    state.lastLoadTrigger = trigger;
    updateLoadMoreControl();
    writeProbe();
    clickNative(nativeButton);
    const result = await waitForNativeLoad(beforeSignature, generation);
    if (result.cancelled) return false;
    state.loading = false;
    state.lastLoadDurationMs = Date.now() - started;
    if (!result.changed) {
      state.endReason = "native-load-timeout";
      renderError("加载更多未返回新影片，可点击“加载更多”重试。");
      updateLoadMoreControl();
      writeProbe();
      return false;
    }
    const items = readItems();
    const beforeIds = new Set(beforeSignature.split(",").map(value => value.split(":", 1)[0]).filter(Boolean));
    const addedCount = items.filter(item => !beforeIds.has(item.subjectId)).length;
    if (!addedCount) {
      state.endReached = true;
      state.endReason = "native-no-new-subject";
    } else {
      state.loadCount += 1;
      state.endReason = "";
    }
    renderCards({ append: true, items });
    if (!native.loadMore() || native.loadMore()?.disabled) {
      state.endReached = true;
      state.endReason = state.endReason || "native-end";
    }
    updateLoadMoreControl();
    writeProbe({ addedCount });
    return addedCount > 0;
  }

  function addStyle() {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = cssText;
    (document.head || document.documentElement || document.body)?.append(style);
  }

  function makeButton(label, className = "") {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `qb-explore-filter-button ${className}`.trim();
    button.textContent = label;
    return button;
  }

  function collapseNativeGroup(groupTitle) {
    const group = findNativeGroup(groupTitle);
    if (nativeGroupExpanded(group)) {
      clickNative(group.querySelector(".base-selector-title") || group);
    }
  }

  function closeCustomFilterMenu({ closeNative = true } = {}) {
    const active = state.customFilterMenu;
    state.customFilterMenu = null;
    if (active?.documentHandler) document.removeEventListener("click", active.documentHandler, true);
    active?.menu?.remove();
    document.body.classList.remove("qb-explore-custom-menu-open");
    if (closeNative && active?.groupTitle) collapseNativeGroup(active.groupTitle);
  }

  function scoreRangeFromModal(modal) {
    const marks = [...(modal?.querySelectorAll(".drc-slider-mark") || [])]
      .map(node => text(node).match(/(\d+(?:\.\d+)?)\s*分/u)?.[1])
      .filter(Boolean);
    if (marks.length < 2) return "";
    const min = marks[0];
    const max = marks[marks.length - 1];
    return min === "0" && max === "10" ? "" : `评分 ${min}-${max}分`;
  }

  function scoreButtonLabel() {
    return state.scoreRange || "评分区间";
  }

  function syncFilterLabels({ items = null, writeProbe: shouldWriteProbe = true } = {}) {
    const root = document.getElementById(ROOT_ID);
    if (!root) return;
    root.querySelectorAll("[data-native-group]").forEach(button => {
      const groupTitle = button.dataset.nativeGroup;
      const group = allFilterGroups().find(item => item.title === groupTitle);
      const node = findNativeGroup(groupTitle);
      if (group && node) {
        syncNativeGroupButton(button, group, node);
      }
    });
    root.querySelector('[data-native-check="uncollect"]')?.classList.toggle("qb-active", Boolean(nativeCheckbox("uncollect")?.checked));
    root.querySelector('[data-native-check="playable"]')?.classList.toggle("qb-active", Boolean(nativeCheckbox("playable")?.checked));
    const scoreButton = root.querySelector("[data-native-score]");
    if (scoreButton) {
      scoreButton.textContent = scoreButtonLabel();
      scoreButton.classList.toggle("qb-active", Boolean(state.scoreRange));
    }
    if (shouldWriteProbe) writeProbe({ items });
  }

  function clickNative(node) {
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
      event.initMouseEvent("click", true, true, window, 1, 0, 0, 0, 0, false, false, false, false, 0, node);
    }
    node.dispatchEvent(event);
    return true;
  }

  function connectNativeGroup(button, group) {
    if (!group?.node || !group.title) return;
    const groupTitle = group.title;
    button.dataset.nativeGroup = groupTitle;
    button.addEventListener("click", async () => {
      if (state.filterOperationBusy || state.filterContext) return;
      const context = beginFilterContext(`open:${groupTitle}`);
      setFilterPhase(context, "native-option-opening", { groupTitle });
      closeCustomFilterMenu();
      const liveGroup = findNativeGroup(groupTitle);
      const title = liveGroup?.querySelector(".base-selector-title") || liveGroup;
      if (!liveGroup || !title) {
        writeProbe({ error: `豆瓣筛选组未找到：${groupTitle}` });
        disposeFilterContext(context, "idle", "group-missing");
        return;
      }
      if (!nativeGroupExpanded(liveGroup)) clickNative(title);
      const opened = await contextWaitFor(context, () => {
        const current = findNativeGroup(groupTitle);
        return visibleNativeOptions(current).length > 0;
      });
      if (!isCurrentFilterContext(context)) return;
      const currentGroup = findNativeGroup(groupTitle);
      const options = opened ? visibleNativeOptions(currentGroup)
        .map(option => ({ label: text(option), selected: nativeOptionSelected(option) }))
        .filter(option => option.label) : [];
      if (!options.length) {
        writeProbe({ error: `豆瓣筛选选项未加载：${group.title}` });
        collapseNativeGroup(groupTitle);
        disposeFilterContext(context, "idle", "options-missing");
        return;
      }
      setFilterPhase(context, "native-option-opening", { groupTitle, optionCount: options.length });
      const menu = document.createElement("div");
      menu.className = "qb-explore-popover";
      menu.setAttribute("role", "menu");
      const rect = button.getBoundingClientRect();
      const width = Math.min(Math.max(rect.width, 360), window.innerWidth - 24);
      const left = Math.max(12, Math.min(rect.left, window.innerWidth - width - 12));
      const viewportPadding = 16;
      const maxMenuHeight = Math.min(420, Math.max(160, window.innerHeight - viewportPadding * 2));
      const preferredTop = rect.bottom + 8;
      const maxTop = window.innerHeight - maxMenuHeight - viewportPadding;
      menu.style.left = `${Math.round(left)}px`;
      menu.style.top = `${Math.round(Math.max(viewportPadding, Math.min(preferredTop, maxTop)))}px`;
      menu.style.width = `${Math.round(width)}px`;
      menu.style.maxHeight = `${Math.round(maxMenuHeight)}px`;
      for (const item of options) {
        const optionButton = makeButton(item.label, "qb-explore-popover-option");
        optionButton.classList.toggle("qb-active", item.selected);
        optionButton.addEventListener("click", async () => {
          if (!isCurrentFilterContext(context) || state.filterOperationBusy) return;
          optionButton.disabled = true;
          context.applying = true;
          closeCustomFilterMenu({ closeNative: false });
          const beforeFilterValues = allFilterValues();
          setFilterPhase(context, "native-option-selected", { groupTitle, expectedValue: item.label }, { writeProbe: false });
          setFilterOperationBusy(true, `filter-group:${group.title}`, { writeProbe: false });
          let applied = false;
          let appliedItems = null;
          try {
            const optionReady = await contextWaitFor(context, () => {
              const current = findNativeGroup(groupTitle);
              if (!nativeGroupExpanded(current)) {
                clickNative(current?.querySelector(".base-selector-title") || current);
              }
              return nativeGroupExpanded(current) && Boolean(findNativeOption(current, item.label));
            }, 1200, 30);
            const freshOption = optionReady ? findNativeOption(findNativeGroup(groupTitle), item.label) : null;
            if (freshOption && isCurrentFilterContext(context)) {
              const beforeList = native.list();
              const beforeItemsSignature = itemsSignature();
              const beforeListDomVersion = context.listDomVersion;
              resetExplorePaging("filter-group");
              setFilterPhase(context, "native-dom-settling", { groupTitle, expectedValue: item.label }, { writeProbe: false });
              context.actionDomVersion = context.domVersion;
              context.actionListDomVersion = context.listDomVersion;
              context.actionStartedAt = Date.now();
              state.groupSelectionOverrides.set(groupSelectionKey(groupTitle), item.label);
              const selectedGroup = findNativeGroup(groupTitle);
              if (selectedGroup) syncNativeGroupButton(button, group, selectedGroup, item.label);
              const resultPromise = waitForFilterResult({
                context,
                groupTitle,
                expectedValue: item.label,
                beforeFilterValues,
                beforeList,
                beforeItemsSignature,
                beforeListDomVersion
              });
              clickNative(freshOption);
              const result = await resultPromise;
              applied = Boolean(result?.applied);
              appliedItems = result?.items || null;
            }
          } finally {
            if (state.filterContext !== context) return;
            try {
              collapseNativeGroup(groupTitle);
              if (!applied) {
                state.groupSelectionOverrides.delete(groupSelectionKey(groupTitle));
                const rollbackGroup = findNativeGroup(groupTitle);
                if (rollbackGroup) syncNativeGroupButton(button, group, rollbackGroup);
              }
              setFilterOperationBusy(false, `filter-group:${group.title}`, { writeProbe: false });
              if (applied) setFilterPhase(context, "filter-applied", { groupTitle, expectedValue: item.label }, { writeProbe: false });
              if (applied) {
                renderCards({ items: appliedItems || [] });
              } else {
                syncFilterLabels({ writeProbe: false });
                writeProbe({ error: `豆瓣筛选未确认稳定生效：${group.title} / ${item.label}` });
              }
            } finally {
              disposeFilterContext(context, applied ? "filter-applied" : "idle", applied ? "completed" : "not-applied");
            }
          }
        });
        menu.append(optionButton);
      }
      document.body.append(menu);
      document.body.classList.add("qb-explore-custom-menu-open");
      const documentHandler = event => {
        if (context.applying) return;
        if (!menu.contains(event.target) && event.target !== button) {
          closeCustomFilterMenu();
          if (isCurrentFilterContext(context)) disposeFilterContext(context, "idle", "popover-closed");
        }
      };
      state.customFilterMenu = { menu, documentHandler, context, groupTitle };
      const documentTimer = window.setTimeout(() => {
        context.timers.delete(documentTimer);
        if (isCurrentFilterContext(context)) document.addEventListener("click", documentHandler, true);
      }, 0);
      context.timers.add(documentTimer);
    });
  }

  function renderPrimaryTabs(container) {
    container.replaceChildren();
    container.className = "qb-explore-primary-tabs";
    const nodes = native.primary();
    const nativeCurrent = nodes.find(selected) || nodes[0] || null;
    if (!state.primarySelectionText || !nodes.some(node => text(node) === state.primarySelectionText)) {
      state.primarySelectionText = text(nativeCurrent);
    }
    for (const node of nodes) {
      const primaryLabel = text(node);
      const tab = makeButton(primaryLabel, "qb-explore-primary-tab");
      const active = primaryLabel === state.primarySelectionText;
      tab.classList.toggle("qb-active", active);
      tab.setAttribute("aria-selected", active ? "true" : "false");
      tab.addEventListener("click", () => {
        const liveNode = findNativePrimary(primaryLabel);
        if (!liveNode) {
          writeProbe({ error: `豆瓣一级筛选节点未找到：${primaryLabel}` });
          return;
        }
        cancelFilterContext("primary-transition");
        clearFilterRenderTimers();
        closeCustomFilterMenu();
        state.primarySelectionText = primaryLabel;
        container.querySelectorAll(".qb-explore-primary-tab").forEach(candidate => {
          const selectedNow = text(candidate) === state.primarySelectionText;
          candidate.classList.toggle("qb-active", selectedNow);
          candidate.setAttribute("aria-selected", selectedNow ? "true" : "false");
        });
        state.groupSelectionOverrides.clear();
        resetExplorePaging("primary-tab");
        state.filterGeneration += 1;
        state.filterOperationGeneration += 1;
        state.filterPhase = "primary-transition";
        setFilterOperationBusy(false, "primary-tab");
        const secondary = container.parentElement?.querySelector(".qb-explore-secondary-controls");
        if (secondary) {
          secondary.replaceChildren();
          secondary.dataset.filterState = "primary-transition";
        }
        clickNative(liveNode);
        renderSecondaryControlsWhenReady(secondary, state.filterGeneration);
        scheduleFilterRender(state.filterGeneration, () => {
          renderPrimaryTabs(container);
          renderCards();
        }, 120);
      });
      container.append(tab);
    }
  }

  function renderSecondaryControls(container) {
    if (!container) return;
    container.replaceChildren();
    const structuredAllFilters = isAllMode();
    container.className = `qb-explore-secondary-controls ${structuredAllFilters ? "qb-explore-all-filters" : "qb-explore-region-filters"}`;
    const nodes = native.secondary();
    if (structuredAllFilters) {
      for (const group of allFilterGroups()) {
        const button = makeButton(nativeGroupValue(group.node, group.title), "qb-explore-filter-button qb-explore-group-filter");
        syncNativeGroupButton(button, group);
        connectNativeGroup(button, group);
        container.append(button);
      }
      return;
    }
    for (const node of nodes) {
      const regionLabel = text(node);
      const button = makeButton(regionLabel, "qb-explore-region-button");
      button.classList.toggle("qb-active", selected(node));
      button.setAttribute("aria-pressed", selected(node) ? "true" : "false");
      button.addEventListener("click", () => {
        if (state.filterOperationBusy || state.filterContext) return;
        const liveNode = native.secondary().find(candidate => text(candidate) === regionLabel);
        if (!liveNode) {
          writeProbe({ error: `豆瓣二级筛选节点未找到：${regionLabel}` });
          return;
        }
        const generation = state.filterGeneration;
        resetExplorePaging("secondary-filter");
        clickNative(liveNode);
        scheduleFilterRender(generation, () => { renderSecondaryControls(container); renderCards(); }, 260);
      });
      container.append(button);
    }
  }

  function renderSecondaryControlsWhenReady(container, generation = state.filterGeneration, attempt = 0, previousSignature = "", stableReads = 0) {
    if (!container || generation !== state.filterGeneration) return;
    if (isAllMode()) {
      const groups = allFilterGroups();
      const expectedCount = expectedAllGroupTitles().length;
      const signature = allFilterSignature(groups);
      const complete = groups.length === expectedCount && allFilterShape(groups) === expectedAllFilterShape() && groups.every(group =>
        group.node.isConnected && group.node.closest(".explore-all-selectors-main") === allSelectorHost() &&
        nativeGroupTitle(group.node));
      const nextStableReads = complete && signature === previousSignature ? stableReads + 1 : (complete ? 1 : 0);
      if ((!complete || nextStableReads < 2) && attempt < 60) {
        container.dataset.filterState = "all-pending";
        scheduleFilterRender(generation, () => renderSecondaryControlsWhenReady(container, generation, attempt + 1, signature, nextStableReads), 80);
        return;
      }
      if (!complete || generation !== state.filterGeneration) {
        if (!container.childElementCount && groups.length) renderSecondaryControls(container);
        container.dataset.filterState = "all-pending";
        writeProbe({ filterState: "all-pending", allFilterSignature: signature, allFilterCount: groups.length });
        return;
      }
      renderSecondaryControls(container);
      container.dataset.filterState = "all-ready";
      writeProbe({ filterState: "all-ready", allFilterSignature: signature, allFilterCount: groups.length });
      return;
    }

    const secondaryNodes = native.secondary();
    if ((!selectedPrimary() || !secondaryNodes.length) && attempt < 60) {
      container.dataset.filterState = "non-all-pending";
      scheduleFilterRender(generation, () => renderSecondaryControlsWhenReady(container, generation, attempt + 1), 80);
      return;
    }
    if (generation !== state.filterGeneration) return;
    renderSecondaryControls(container);
    container.dataset.filterState = "non-all-ready";
  }

  function connectNativeCheckbox(button, value) {
    button.dataset.nativeCheck = value;
    button.addEventListener("click", () => {
      if (state.filterOperationBusy || state.filterContext) return;
      resetExplorePaging(`checkbox-${value}`);
      const input = nativeCheckbox(value);
      const label = input ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
      if (label) clickNative(label); else if (input) clickNative(input);
      scheduleFilterRender(state.filterGeneration, syncFilterLabels, 180);
    });
  }

  function connectScore(button) {
    button.dataset.nativeScore = "true";
    button.addEventListener("click", () => {
      if (state.filterOperationBusy || state.filterContext) return;
      const generation = state.filterGeneration;
      const title = native.scoreTitle();
      if (!title) return;
      state.scoreModalCleanup?.();
      document.body.classList.add("qb-explore-score-open");
      clickNative(title);
      scheduleFilterRender(generation, () => {
        const modal = document.querySelector(".drc-modal");
        if (!modal) {
          document.body.classList.remove("qb-explore-score-open");
          return;
        }
        const container = modal.closest(".drc-modal-container") || modal;
        const confirm = modal.querySelector(".drc-modal-confirm");
        confirm?.addEventListener("click", () => {
          state.scoreRange = scoreRangeFromModal(modal);
          resetExplorePaging("score-filter");
        }, { capture: true, once: true });
        let finished = false;
        const observer = new MutationObserver(() => {
          if (!document.body.contains(container)) finish();
        });
        const finish = () => {
          if (finished) return;
          finished = true;
          observer.disconnect();
          if (state.scoreModalCleanup === finish) state.scoreModalCleanup = null;
          document.body.classList.remove("qb-explore-score-open");
          scheduleFilterRender(generation, () => { syncFilterLabels(); renderCards(); }, 180);
        };
        state.scoreModalCleanup = finish;
        observer.observe(document.body, { childList: true, subtree: true });
        const cleanup = (attempt = 0) => {
          if (!document.body.contains(container) || attempt > 30) {
            finish();
            return;
          }
          setTimeout(() => cleanup(attempt + 1), 200);
        };
        cleanup();
      }, 80);
    });
  }

  function createCard(item) {
    const identity = [contentTypeLabel(), item.year ? `${isTvPage() ? "首播" : "上映"} ${item.year}` : ""].filter(Boolean).join(" · ");
    const card = QbDoubanCard.render({
      model: {
        subjectId: item.subjectId,
        subjectUrl: item.subjectUrl,
        title: item.title,
        posterUrl: item.poster,
        identity: item.identity || identity,
        infoRows: [
          item.genre ? { label: "类型", value: item.genre } : null,
          item.director ? { label: "导演", value: item.director } : null,
          item.cast?.length ? { label: "主演", value: item.cast.slice(0, 2).join(" / ") } : null
        ].filter(Boolean),
        score: item.score
      },
      cardClass: "qb-explore-card",
      posterClass: "qb-explore-poster",
      bodyClass: "qb-explore-card-body",
      titleClass: "qb-explore-card-title",
      contextClass: "qb-explore-card-subtitle",
      metaClass: "qb-explore-card-meta",
      onOpen: () => rememberAndOpen(item.subjectId),
      onKeyDown: event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); rememberAndOpen(item.subjectId); } }
    });
    return card;
  }

  function renderCards({ append = false, items = readItems() } = {}) {
    const root = document.getElementById(ROOT_ID);
    const grid = root?.querySelector(".qb-explore-grid");
    if (!grid) return;
    const previousScroll = window.scrollY || 0;
    if (append) {
      const existingIds = new Set([...grid.querySelectorAll("[data-subject-id]")].map(node => node.dataset.subjectId).filter(Boolean));
      for (const item of items) {
        if (!existingIds.has(item.subjectId)) {
          grid.append(createCard(item));
          existingIds.add(item.subjectId);
        }
      }
    } else {
      grid.replaceChildren(...items.map(createCard));
    }
    state.loadedItemCount = items.length;
    updateLoadMoreControl();
    const empty = root.querySelector(".qb-explore-empty");
    if (empty) empty.hidden = items.length > 0;
    const status = root.querySelector(".qb-explore-status");
    if (status && !state.lastError) {
      status.textContent = "";
      status.removeAttribute("data-error");
    }
    syncFilterLabels({ items });
    if (state.restoreStarted) window.scrollTo(0, previousScroll);
  }

  function renderError(message) {
    state.lastError = message;
    const root = document.getElementById(ROOT_ID);
    if (!root) return;
    const status = root.querySelector(".qb-explore-status");
    if (status) status.textContent = message;
    status?.setAttribute("data-error", "true");
    writeProbe({ error: message });
  }

  function rememberAndOpen(subjectId) {
    const current = readState();
    sessionStorage.setItem(STATE_KEY, JSON.stringify({ ...current, subjectId }));
    window.chrome?.webview?.postMessage({
      type: "doubanExploreOpenSubject",
      subjectId,
      subjectUrl: subjectUrl(subjectId),
      exploreUrl: location.href,
      requestId: `explore-${subjectId}-${Date.now()}`,
      scrollY: String(current.scrollY)
    });
  }

  function setupRestore() {
    if (state.restoreStarted) return;
    state.restoreStarted = true;
    let saved;
    try { saved = JSON.parse(sessionStorage.getItem(STATE_KEY) || "null"); } catch { saved = null; }
    if (!saved || saved.href && !EXPLORE_URL.test(saved.href)) return;
    sessionStorage.removeItem(STATE_KEY);
    const clickLabel = (nodes, wanted) => {
      const node = nodes.find(item => text(item) === wanted);
      if (node && text(nodes.find(selected)) !== wanted) clickNative(node);
      return Boolean(node);
    };
    clickLabel(native.primary(), saved.primary);
    setTimeout(() => {
      clickLabel(native.secondary(), saved.secondary);
      const checks = [...(native.filterContainer()?.querySelectorAll('input[type="checkbox"]') || [])];
      for (const [value, wanted] of [["uncollect", saved.uncollect], ["playable", saved.playable]]) {
        const input = checks.find(item => item.value === value);
        if (input && Boolean(input.checked) !== Boolean(wanted)) {
          const label = document.querySelector(`label[for="${CSS.escape(input.id)}"]`);
          clickNative(label || input);
        }
      }
      setTimeout(() => {
        renderCards();
        window.scrollTo(0, Number(saved.scrollY) || 0);
      }, 320);
    }, 260);
  }

  function mount() {
    if (document.getElementById(ROOT_ID)) return true;
    const list = native.list();
    const wrapper = native.wrapper();
    if (!list || !wrapper) return false;
    addStyle();
    const root = document.createElement("main");
    root.id = ROOT_ID;
    const header = document.createElement("header");
    header.className = "qb-explore-header";
    const heading = document.createElement("div");
    const kicker = document.createElement("p");
    kicker.className = "qb-explore-kicker";
    kicker.textContent = "Douban Plus / Explore";
    const title = document.createElement("h1");
    title.className = "qb-explore-title";
    title.textContent = isTvPage() ? "探索剧集" : "探索选片";
    heading.append(kicker, title);
    const searchHost = document.createElement("div");
    searchHost.className = "qb-explore-search-host";
    searchHost.dataset.qbDoubanPlusSearchHost = "true";
    header.append(heading, searchHost);

    const appNav = document.createElement("nav");
    appNav.className = "qb-explore-app-nav";
    appNav.setAttribute("aria-label", "Douban Plus 页面导航");
    let personalUrl = "";
    try { personalUrl = sessionStorage.getItem(PERSONAL_URL_KEY) || ""; } catch { personalUrl = ""; }
    const profileMatch = personalUrl.match(/^https:\/\/movie\.douban\.com\/people\/(\d+)\/(?:collect|wish|do)\/?(?:\?.*)?$/u);
    if (profileMatch) {
      const profileId = profileMatch[1];
      for (const [status, label] of [["collect", "看过"], ["wish", "想看"], ["do", "在看"]]) {
        const link = document.createElement("a");
        link.className = "qb-explore-app-tab";
        link.href = `https://movie.douban.com/people/${profileId}/${status}`;
        link.textContent = label;
        appNav.append(link);
      }
    }
    const exploreTab = document.createElement("span");
    exploreTab.className = "qb-explore-app-tab qb-active";
    exploreTab.textContent = "探索";
    exploreTab.setAttribute("aria-current", "page");
    appNav.append(exploreTab);

    const contentTypeNav = document.createElement("nav");
    contentTypeNav.className = "qb-explore-content-type-nav";
    contentTypeNav.setAttribute("aria-label", "内容类型");
    for (const option of [
      { label: "电影", href: "https://movie.douban.com/explore" },
      { label: "电视剧", href: "https://movie.douban.com/tv/" }
    ]) {
      const link = document.createElement("a");
      link.className = "qb-explore-content-type-tab";
      link.href = option.href;
      link.textContent = option.label;
      link.setAttribute("aria-current", option.label === contentTypeLabel() ? "page" : "false");
      if (option.label === contentTypeLabel()) link.classList.add("qb-active");
      contentTypeNav.append(link);
    }

    const toolbar = document.createElement("section");
    toolbar.className = "qb-explore-toolbar";
    const primaryTabs = document.createElement("nav");
    primaryTabs.setAttribute("aria-label", "探索模式");
    renderPrimaryTabs(primaryTabs);
    const secondaryControls = document.createElement("div");
    renderSecondaryControlsWhenReady(secondaryControls);
    const score = makeButton(text(native.scoreTitle()) || "评分区间");
    connectScore(score);
    const commonControls = document.createElement("div");
    commonControls.className = "qb-explore-common-controls";
    commonControls.append(score);
    for (const [label, value] of [["未看过", "uncollect"], ["可播放", "playable"]]) {
      if (!hasNativeCheckbox(value)) continue;
      const checkbox = makeButton(label, "qb-explore-toggle");
      connectNativeCheckbox(checkbox, value);
      commonControls.append(checkbox);
    }
    toolbar.append(primaryTabs, secondaryControls, commonControls);

    const content = document.createElement("section");
    content.className = "qb-explore-content";
    const grid = document.createElement("div");
    grid.className = "qb-explore-grid";
    const empty = document.createElement("div");
    empty.className = "qb-explore-empty";
    empty.hidden = true;
    empty.textContent = `当前筛选暂无${contentTypeLabel()}。`;
    content.append(grid, empty);
    const footer = document.createElement("footer");
    footer.className = "qb-explore-footer";
    const status = document.createElement("p");
    status.className = "qb-explore-status";
    status.setAttribute("role", "status");
    const loadMore = makeButton("加载更多", "qb-explore-load-more");
    loadMore.addEventListener("click", () => { void loadNextExplorePage("manual"); });
    const sentinel = document.createElement("div");
    sentinel.className = "qb-explore-scroll-sentinel";
    sentinel.setAttribute("aria-hidden", "true");
    footer.append(status, loadMore, sentinel);
    root.append(header, appNav, contentTypeNav, toolbar, content, footer);
    document.body.prepend(root);
    document.body.classList.add("qb-douban-explore-enhanced");
    wrapper.style.display = "none";
    state.nativeReady = true;
    renderCards();
    const syncNativeCards = () => {
      if (state.loading || state.filterOperationBusy) return;
      const items = readItems();
      const signature = itemsSignature(items);
      if (signature === state.lastNativeSignature) return;
      state.lastNativeSignature = signature;
      renderCards({ items });
    };
    state.observer = new MutationObserver(syncNativeCards);
    state.observer.observe(list, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["class", "disabled", "href", "src"] });
    state.lastNativeSignature = itemsSignature();
    state.monitorTimer = window.setInterval(() => {
      if (!document.getElementById(ROOT_ID)) return;
      syncNativeCards();
    }, 500);
    if ("IntersectionObserver" in window) {
      state.scrollObserver = new IntersectionObserver(entries => {
        if (entries.some(entry => entry.isIntersecting)) void loadNextExplorePage("scroll");
      }, { root: null, rootMargin: "0px 0px 720px 0px", threshold: 0 });
      state.scrollObserver.observe(sentinel);
    }
    setupRestore();
    writeProbe();
    return true;
  }

  function boot(attempt = 0) {
    if (mount()) return;
    if (attempt < 120) setTimeout(() => boot(attempt + 1), 100);
    else renderError("豆瓣 Explore 原生内容加载超时。");
  }

  const start = () => boot();
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
  else start();
})();
