(() => {
  "use strict";

  const cssText = __QB_DOUBAN_SHELL_CSS__;
  const root = () => document.getElementById("qb-douban-shell-root");
  const grid = () => document.getElementById("qb-douban-shell-grid");
  const status = () => document.getElementById("qb-douban-shell-status");
  const filtersHost = () => document.getElementById("qb-douban-shell-filters");
  const pagingHost = () => document.getElementById("qb-douban-shell-paging");
  const searchForm = () => document.getElementById("qb-douban-shell-search");
  const searchInput = () => document.getElementById("qb-douban-shell-search-input");
  const value = raw => String(raw ?? "").replace(/\s+/gu, " ").trim();
  const list = raw => Array.isArray(raw)
    ? [...new Set(raw.map(value).filter(Boolean))]
    : value(raw).split(/\s*\/\s*/u).map(value).filter(Boolean);
  const previewList = (items, limit = 2) => list(items).slice(0, limit).join(" / ");
  const post = (type, payload = {}) => window.chrome?.webview?.postMessage({ type, ...payload });
  let busy = false;
  let currentContentType = "movie";
  let viewKind = "explore";
  let personalStatus = "collect";
  let searchQuery = "";
  let searchPageUrl = "";
  let pagingObserver = null;
  let pagingEpoch = 0;
  let pendingPaging = null;
  let pagingError = false;
  let resetViewportOnNextData = false;
  let personalLocalFilterState = null;
  let personalAdvancedFiltersOpen = false;
  let personalAdvancedFilterSection = "";
  let personalScorePopoverOpen = false;
  const personalStatusMeta = { collect: "看过", wish: "想看", do: "在看" };
  const defaultPersonalLocalCriteria = () => ({
    contentType: "",
    playableOnly: false,
    scoreMin: null,
    scoreMax: null,
    myRating: null,
    unrated: false,
    period: "",
    genre: "",
    country: "",
    sort: "marked-desc"
  });

  const isPagingOperation = operation => operation === "load-more" || operation === "load-more-noop";

  const scrollShellTop = () => {
    const apply = () => {
      window.scrollTo(0, 0);
      document.documentElement.scrollTop = 0;
      document.body.scrollTop = 0;
    };
    apply();
    window.requestAnimationFrame?.(apply);
  };

  const beginListSwitch = (waitForData = true) => {
    pagingObserver?.disconnect();
    pagingObserver = null;
    pendingPaging = null;
    pagingError = false;
    pagingEpoch += 1;
    resetViewportOnNextData = waitForData;
    scrollShellTop();
  };

  const settleListSwitch = () => {
    if (!resetViewportOnNextData) return;
    resetViewportOnNextData = false;
    scrollShellTop();
  };

  const contentTypeLabel = () => currentContentType === "tv" ? "电视剧" : "电影";
  const contentTypeMode = () => `explore-${currentContentType}`;
  const personalStatusLabel = () => personalStatusMeta[personalStatus] || "看过";
  const viewLabel = () => viewKind === "search" ? `搜索「${searchQuery || ""}」` : viewKind === "personal" ? `个人影片 · ${personalStatusLabel()}` : viewKind === "watchlist" ? "我的待看" : `探索${contentTypeLabel()}`;
  const updateContentTypeUi = contentType => {
    currentContentType = contentType === "tv" ? "tv" : "movie";
    viewKind = "explore";
    const shellRoot = root();
    shellRoot?.classList.remove("qb-personal-view");
    if (shellRoot) shellRoot.dataset.viewKind = viewKind;
    grid()?.classList.remove("qb-watchlist-grid", "qb-watchlist-shell-grid");
    const label = contentTypeLabel();
    const heading = document.getElementById("qb-douban-shell-heading");
    const description = document.getElementById("qb-douban-shell-description");
    if (heading) heading.textContent = `探索${label}`;
    if (description) description.textContent = `内容来自豆瓣真实 Explore 页面；筛选、分页和卡片都由统一界面承载。`;
    document.querySelectorAll("[data-douban-content-type]").forEach(button => {
      const active = button.getAttribute("data-douban-content-type") === currentContentType;
      button.classList.toggle("qb-active", active);
      button.setAttribute("aria-current", active ? "page" : "false");
    });
    document.querySelectorAll("[data-douban-personal-status]").forEach(button => {
      button.classList.remove("qb-active");
      button.setAttribute("aria-current", "false");
    });
    const watchlist = document.querySelector("[data-douban-watchlist]");
    watchlist?.classList.remove("qb-active");
    watchlist?.setAttribute("aria-current", "false");
  };

  const updatePersonalUi = statusValue => {
    viewKind = "personal";
    const shellRoot = root();
    shellRoot?.classList.add("qb-personal-view");
    if (shellRoot) shellRoot.dataset.viewKind = viewKind;
    grid()?.classList.remove("qb-watchlist-grid", "qb-watchlist-shell-grid");
    const nextPersonalStatus = Object.prototype.hasOwnProperty.call(personalStatusMeta, statusValue) ? statusValue : "collect";
    if (nextPersonalStatus !== personalStatus) {
      personalLocalFilterState = null;
      personalAdvancedFiltersOpen = false;
      personalAdvancedFilterSection = "";
      personalScorePopoverOpen = false;
    }
    personalStatus = nextPersonalStatus;
    const heading = document.getElementById("qb-douban-shell-heading");
    const description = document.getElementById("qb-douban-shell-description");
    if (heading) heading.textContent = `个人影片 · ${personalStatusLabel()}`;
    if (description) description.textContent = "内容来自豆瓣真实个人影片页面；状态、分页和卡片都由统一界面承载。";
    document.querySelectorAll("[data-douban-content-type]").forEach(button => {
      button.classList.remove("qb-active");
      button.setAttribute("aria-current", "false");
    });
    document.querySelectorAll("[data-douban-personal-status]").forEach(button => {
      const active = button.getAttribute("data-douban-personal-status") === personalStatus;
      button.classList.toggle("qb-active", active);
      button.setAttribute("aria-current", active ? "page" : "false");
    });
    const watchlist = document.querySelector("[data-douban-watchlist]");
    watchlist?.classList.remove("qb-active");
    watchlist?.setAttribute("aria-current", "false");
  };

  const updateWatchlistUi = () => {
    viewKind = "watchlist";
    const shellRoot = root();
    shellRoot?.classList.remove("qb-personal-view");
    if (shellRoot) shellRoot.dataset.viewKind = viewKind;
    const heading = document.getElementById("qb-douban-shell-heading");
    const description = document.getElementById("qb-douban-shell-description");
    if (heading) heading.textContent = "我的待看";
    if (description) description.textContent = "独立保存在本机的待看列表；不改变豆瓣官方状态或历史。";
    filtersHost()?.replaceChildren();
    pagingHost()?.replaceChildren();
    document.querySelectorAll("[data-douban-content-type], [data-douban-personal-status]").forEach(button => {
      button.classList.remove("qb-active");
      button.setAttribute("aria-current", "false");
    });
    const watchlist = document.querySelector("[data-douban-watchlist]");
    watchlist?.classList.add("qb-active");
    watchlist?.setAttribute("aria-current", "page");
  };

  const updateSearchUi = query => {
    viewKind = "search";
    searchQuery = value(query);
    searchPageUrl = "";
    const shellRoot = root();
    shellRoot?.classList.remove("qb-personal-view");
    if (shellRoot) shellRoot.dataset.viewKind = viewKind;
    if (searchInput()) searchInput().value = searchQuery;
    const heading = document.getElementById("qb-douban-shell-heading");
    const description = document.getElementById("qb-douban-shell-description");
    if (heading) heading.textContent = viewLabel();
    if (description) description.textContent = "内容来自豆瓣原生搜索页面；结果卡片沿用当前统一海报与文字样式。";
    filtersHost()?.replaceChildren();
    document.querySelectorAll("[data-douban-content-type], [data-douban-personal-status], [data-douban-watchlist]").forEach(button => {
      button.classList.remove("qb-active");
      button.setAttribute("aria-current", "false");
    });
  };

  const openWatchlist = () => {
    if (busy) return;
    beginListSwitch(false);
    updateWatchlistUi();
    setBusy(true);
    setStatus("正在读取本地待看…");
    const loader = window.QbDoubanWatchlist?.loadShell;
    if (typeof loader !== "function") {
      setBusy(false);
      setStatus("待看脚本尚未就绪，请刷新页面。", true);
      return;
    }
    Promise.resolve(loader()).then(result => {
      setStatus(`本地待看共 ${Array.isArray(result?.items) ? result.items.length : 0} 部`);
    }).catch(error => {
      setStatus(String(error?.message || error || "本地待看读取失败。"), true);
    }).finally(() => setBusy(false));
  };

  const addStyle = () => {
    if (document.getElementById("qb-douban-shell-style")) return;
    const style = document.createElement("style");
    style.id = "qb-douban-shell-style";
    style.textContent = cssText;
    (document.head || document.documentElement || document.body)?.append(style);
  };

  const setStatus = (text, error = false) => {
    const node = status();
    if (!node) return;
    node.textContent = text;
    node.classList.toggle("qb-error", error);
  };

  const normalize = item => {
    const year = value(item?.year);
    const countries = list(item?.countries || item?.country);
    const genres = list(item?.genres || item?.genre);
    const cast = list(item?.cast || item?.actors).slice(0, 2);
    const directors = list(item?.directors || item?.director).slice(0, 1);
    return {
      subjectId: value(item?.subjectId),
      subjectUrl: value(item?.subjectUrl),
      title: value(item?.title) || "未命名影片",
      posterUrl: value(item?.posterUrl || item?.poster),
      subtitle: value(item?.subtitle || item?.context),
      identity: value(item?.identity) || [year, countries.slice(0, 3).join(" / ")].filter(Boolean).join(" / "),
      genre: genres.join(" / "),
      countries,
      genres,
      cast,
      director: directors[0] || "",
      directors,
      year,
      score: value(item?.score || item?.rating),
      contentType: value(item?.contentType) || currentContentType,
      status: value(item?.status),
      statusLabel: value(item?.statusLabel),
      myRating: value(item?.myRating),
      markedDate: value(item?.markedDate),
      comment: value(item?.comment),
      facts: list(item?.facts),
      voteCount: value(item?.voteCount),
      castText: value(item?.castText)
    };
  };

  const formatPublicScore = raw => {
    const text = value(raw).replace(/^豆瓣\s*/u, "").trim();
    const numeric = Number(text);
    return Number.isFinite(numeric) && numeric > 0 ? numeric.toFixed(1) : text;
  };
  const openDetail = item => {
    const normalized = normalize(item);
    if (!/^\d+$/u.test(normalized.subjectId) || !/^https:\/\/movie\.douban\.com\/subject\/\d+\/?$/u.test(normalized.subjectUrl)) {
      setStatus("影片地址无效，无法打开详情。", true);
      return;
    }
    post("doubanShellOpenDetail", {
      subjectId: normalized.subjectId,
      subjectUrl: normalized.subjectUrl,
      mode: viewKind === "personal" ? `personal-${personalStatus}` : viewKind === "watchlist" ? "watchlist" : viewKind === "search" ? "search" : contentTypeMode()
    });
  };

  const render = (items, { append = false } = {}) => {
    const target = grid();
    if (!target) return;
    target.classList.remove("qb-watchlist-grid", "qb-watchlist-shell-grid");
    const normalizedItems = (Array.isArray(items) ? items : []).map(normalize).filter(item => /^\d+$/u.test(item.subjectId));
    if (!append) target.replaceChildren();
    if (!normalizedItems.length && !append) {
      target.innerHTML = `<div class="qb-shell-empty">豆瓣没有返回${viewKind === "search" ? `“${searchQuery}”` : viewKind === "personal" ? `“${personalStatusLabel()}”` : contentTypeLabel()}卡片。</div>`;
      return;
    }
    const existingIds = append
      ? new Set([...target.querySelectorAll("[data-subject-id]")].map(node => node.dataset.subjectId).filter(Boolean))
      : new Set();
    if (append) target.querySelector(".qb-shell-empty")?.remove();
    normalizedItems.forEach(item => {
      if (existingIds.has(item.subjectId)) return;
      const mediaTypeLabel = item.contentType === "tv" ? "电视剧" : viewKind === "personal" ? "电影" : contentTypeLabel();
      const publicScore = item.score && Number(item.score) > 0
        ? formatPublicScore(item.score)
        : "";
      const personalScore = viewKind === "personal" && item.myRating
        ? "★".repeat(Math.max(0, Math.min(5, Number(item.myRating))))
        : "";
      const cardModel = {
        subjectId: item.subjectId,
        subjectUrl: item.subjectUrl,
        title: item.title,
        posterUrl: item.posterUrl,
        identity: item.identity,
        infoRows: [
          item.genre ? { label: "类型", value: item.genre } : null,
          item.director ? { label: "导演", value: item.director } : null,
          item.cast.length ? { label: "主演", value: previewList(item.cast, 2) } : null
        ].filter(Boolean),
        context: item.subtitle,
        comment: item.comment,
        score: publicScore,
        myRating: personalScore,
      };
      const card = window.QbDoubanCard?.render({
        model: cardModel,
        onOpen: event => { event.preventDefault(); openDetail(item); },
        onPosterError: ({ subjectId, posterUrl }) => post("doubanShellPosterFailed", { subjectId, posterUrl }),
        onKeyDown: event => {
          if (event.key === "Enter" || event.key === " ") { event.preventDefault(); openDetail(item); }
        }
      });
      if (card) {
        target.append(card);
        existingIds.add(item.subjectId);
      }
    });
  };

  const makeFilterButton = (label, active, onClick, className = "qb-shell-filter-button") => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = className;
    button.textContent = value(label);
    button.classList.toggle("qb-active", Boolean(active));
    button.disabled = busy;
    button.addEventListener("click", onClick);
    return button;
  };

  const renderFilterOptions = message => {
    const host = filtersHost();
    if (!host) return;
    host.querySelector(".qb-shell-filter-options")?.remove();
    const options = Array.isArray(message?.options) ? message.options : [];
    if (!options.length) return;
    const row = document.createElement("div");
    row.className = "qb-shell-filter-options";
    options.forEach(option => {
      const label = value(option?.label || option);
      if (!label) return;
      row.append(makeFilterButton(label, option?.selected, () => {
        busy = true;
        post("doubanShellApplyFilter", {
          requestId: `shell-filter-${Date.now()}`,
          kind: "group",
          title: value(message.groupTitle),
          label
        });
        setStatus(`正在应用筛选：${value(message.groupTitle)} / ${label}`);
      }, "qb-shell-filter-option"));
    });
    host.append(row);
  };

  const makeFilterSelect = (options, selectedValue, onChange) => {
    const select = document.createElement("select");
    select.className = "qb-shell-filter-select";
    (Array.isArray(options) ? options : []).forEach(option => {
      const item = document.createElement("option");
      item.value = value(option?.value);
      item.textContent = value(option?.label);
      item.selected = item.value === value(selectedValue);
      select.append(item);
    });
    select.disabled = busy;
    select.addEventListener("change", () => onChange(select.value));
    return select;
  };

  const applyLocalPersonalFilter = patch => {
    if (busy || !personalLocalFilterState?.ready) return;
    const current = { ...defaultPersonalLocalCriteria(), ...(personalLocalFilterState.criteria || {}) };
    const next = { ...current, ...patch };
    if (patch.unrated === true) next.myRating = null;
    if (Object.prototype.hasOwnProperty.call(patch, "myRating") && patch.myRating !== null) next.unrated = false;
    personalLocalFilterState = { ...personalLocalFilterState, criteria: next };
    beginListSwitch();
    setBusy(true);
    setStatus("正在筛选完整个人库…");
    post("doubanShellApplyLocalPersonalFilter", {
      requestId: `shell-personal-local-${Date.now()}`,
      criteria: next
    });
  };

  const renderPersonalLocalFilters = (host, snapshot) => {
    document.querySelector(".qb-shell-score-popover")?.remove();
    personalLocalFilterState = snapshot && typeof snapshot === "object" ? snapshot : null;
    const criteria = { ...defaultPersonalLocalCriteria(), ...(personalLocalFilterState?.criteria || {}) };
    const ready = personalLocalFilterState?.ready === true;
    const facets = personalLocalFilterState?.facets || {};

    const primaryRow = document.createElement("div");
    primaryRow.className = "qb-shell-personal-primary-row";

    const addPrimaryGroup = (title, controls) => {
      const group = document.createElement("div");
      group.className = "qb-shell-personal-primary-group";
      if (title) {
        const label = document.createElement("span");
        label.className = "qb-shell-filter-label";
        label.textContent = title;
        group.append(label);
      }
      controls.forEach(control => group.append(control));
      primaryRow.append(group);
      return group;
    };

    const statusButtons = Object.entries(personalStatusMeta).map(([statusValue, labelText]) =>
      makeFilterButton(labelText, statusValue === personalStatus, () => {
        if (busy || statusValue === personalStatus) return;
        beginListSwitch();
        updatePersonalUi(statusValue);
        setBusy(true);
        setStatus(`正在读取“${labelText}”影片…`);
        post("doubanShellNavigatePersonal", { status: statusValue, requestId: `shell-personal-${Date.now()}` });
      }));
    addPrimaryGroup("状态", statusButtons);

    addPrimaryGroup("影片类型", [
      makeFilterButton("全部", !value(criteria.contentType), () => applyLocalPersonalFilter({ contentType: "" })),
      makeFilterButton("电影", value(criteria.contentType) === "movie", () => applyLocalPersonalFilter({ contentType: "movie" })),
      makeFilterButton("电视", value(criteria.contentType) === "tv", () => applyLocalPersonalFilter({ contentType: "tv" }))
    ]);

    addPrimaryGroup("排序", [
      makeFilterButton("按时间排序", value(criteria.sort) === "marked-desc" || !value(criteria.sort), () => applyLocalPersonalFilter({ sort: "marked-desc" })),
      makeFilterButton("按评价排序", value(criteria.sort) === "my-rating-desc", () => applyLocalPersonalFilter({ sort: "my-rating-desc" })),
      makeFilterButton("按标题排序", value(criteria.sort) === "title-asc", () => applyLocalPersonalFilter({ sort: "title-asc" }))
    ]);

    const playableButton = makeFilterButton("可播放", criteria.playableOnly === true, () => applyLocalPersonalFilter({ playableOnly: criteria.playableOnly !== true }));
    if (!ready) playableButton.disabled = true;
    addPrimaryGroup("", [playableButton]);

    const hasScoreMin = criteria.scoreMin !== null && criteria.scoreMin !== undefined && criteria.scoreMin !== "";
    const hasScoreMax = criteria.scoreMax !== null && criteria.scoreMax !== undefined && criteria.scoreMax !== "";
    const scoreMin = hasScoreMin && Number.isFinite(Number(criteria.scoreMin)) ? Number(criteria.scoreMin) : null;
    const scoreMax = hasScoreMax && Number.isFinite(Number(criteria.scoreMax)) ? Number(criteria.scoreMax) : null;
    const scoreActive = scoreMin !== null || scoreMax !== null;
    const scoreLabel = scoreActive
      ? `豆瓣评分 ${Math.round(scoreMin ?? 0)}–${Math.round(scoreMax ?? 10)}`
      : "豆瓣评分";
    const scoreButton = makeFilterButton(scoreLabel, scoreActive || personalScorePopoverOpen, () => {
      personalScorePopoverOpen = !personalScorePopoverOpen;
      renderFilters(personalLocalFilterState);
    });
    if (!ready) scoreButton.disabled = true;
    const scoreGroup = addPrimaryGroup("", [scoreButton]);
    scoreGroup.classList.add("qb-shell-score-control");
    host.append(primaryRow);

    if (personalScorePopoverOpen && ready) {
      const popover = document.createElement("div");
      popover.className = "qb-shell-score-popover";
      let draftMin = Math.max(0, Math.min(10, Math.round(scoreMin ?? 0)));
      let draftMax = Math.max(draftMin, Math.min(10, Math.round(scoreMax ?? 10)));
      let visualMin = draftMin;
      let visualMax = draftMax;
      let activeHandle = "";
      let activePointerId = null;

      const titleRow = document.createElement("div");
      titleRow.className = "qb-shell-score-title-row";
      const title = document.createElement("strong");
      title.textContent = "豆瓣评分区间";
      const rangeText = document.createElement("span");
      rangeText.className = "qb-shell-score-range-text";
      titleRow.append(title, rangeText);

      const slider = document.createElement("div");
      slider.className = "qb-shell-score-custom-slider";
      const rail = document.createElement("div");
      rail.className = "qb-shell-score-rail";
      const track = document.createElement("div");
      track.className = "qb-shell-score-track";
      const fill = document.createElement("div");
      fill.className = "qb-shell-score-track-fill";
      const minHandle = document.createElement("button");
      const maxHandle = document.createElement("button");
      const minBubble = document.createElement("span");
      const maxBubble = document.createElement("span");
      minHandle.type = maxHandle.type = "button";
      minHandle.className = "qb-shell-score-handle qb-shell-score-handle-min";
      maxHandle.className = "qb-shell-score-handle qb-shell-score-handle-max";
      minHandle.setAttribute("aria-label", "豆瓣最低评分");
      maxHandle.setAttribute("aria-label", "豆瓣最高评分");
      minBubble.className = "qb-shell-score-bubble qb-shell-score-bubble-min";
      maxBubble.className = "qb-shell-score-bubble qb-shell-score-bubble-max";
      const endpoints = document.createElement("div");
      endpoints.className = "qb-shell-score-endpoints";
      endpoints.innerHTML = "<span>0</span><span>10</span>";
      rail.append(track, fill, minBubble, maxBubble, minHandle, maxHandle);
      slider.append(rail, endpoints);

      const updateSlider = (animate = false) => {
        slider.classList.toggle("qb-snapping", animate);
        const minPercent = visualMin * 10;
        const maxPercent = visualMax * 10;
        minHandle.style.left = `${minPercent}%`;
        maxHandle.style.left = `${maxPercent}%`;
        minBubble.style.left = `${minPercent}%`;
        maxBubble.style.left = `${maxPercent}%`;
        fill.style.left = `${minPercent}%`;
        fill.style.width = `${Math.max(0, maxPercent - minPercent)}%`;
        minBubble.textContent = `${draftMin}分`;
        maxBubble.textContent = `${draftMax}分`;
        rangeText.textContent = draftMin <= 0 && draftMax >= 10 ? "全部" : `${draftMin}–${draftMax}分`;
        window.clearTimeout(updateSlider.snapTimer);
        if (animate) updateSlider.snapTimer = window.setTimeout(() => slider.classList.remove("qb-snapping"), 150);
      };

      const rawFromPointer = event => {
        const rect = rail.getBoundingClientRect();
        return Math.max(0, Math.min(10, ((event.clientX - rect.left) / Math.max(1, rect.width)) * 10));
      };

      const applyPointer = event => {
        if (!activeHandle) return;
        const raw = rawFromPointer(event);
        if (activeHandle === "min") {
          visualMin = Math.min(raw, visualMax);
          draftMin = Math.min(Math.round(visualMin), draftMax);
        } else {
          visualMax = Math.max(raw, visualMin);
          draftMax = Math.max(Math.round(visualMax), draftMin);
        }
        updateSlider(false);
      };

      const beginPointer = (handle, event) => {
        event.preventDefault();
        activeHandle = handle;
        activePointerId = event.pointerId;
        slider.setPointerCapture?.(event.pointerId);
        applyPointer(event);
      };
      minHandle.addEventListener("pointerdown", event => beginPointer("min", event));
      maxHandle.addEventListener("pointerdown", event => beginPointer("max", event));
      slider.addEventListener("pointerdown", event => {
        if (event.target === minHandle || event.target === maxHandle) return;
        const raw = rawFromPointer(event);
        beginPointer(Math.abs(raw - visualMin) <= Math.abs(raw - visualMax) ? "min" : "max", event);
      });
      slider.addEventListener("pointermove", event => {
        if (activePointerId !== event.pointerId) return;
        applyPointer(event);
      });
      const endPointer = event => {
        if (activePointerId !== event.pointerId) return;
        visualMin = draftMin;
        visualMax = draftMax;
        activeHandle = "";
        activePointerId = null;
        try { slider.releasePointerCapture?.(event.pointerId); } catch { }
        updateSlider(true);
      };
      slider.addEventListener("pointerup", endPointer);
      slider.addEventListener("pointercancel", endPointer);
      updateSlider(false);

      const actions = document.createElement("div");
      actions.className = "qb-shell-score-actions";
      const resetButton = makeFilterButton("全部", false, () => {
        draftMin = 0;
        draftMax = 10;
        visualMin = 0;
        visualMax = 10;
        updateSlider(true);
      }, "qb-shell-filter-option qb-shell-score-reset");
      actions.append(
        resetButton,
        makeFilterButton("取消", false, () => {
          personalScorePopoverOpen = false;
          renderFilters(personalLocalFilterState);
        }, "qb-shell-filter-option"),
        makeFilterButton("确定", true, () => {
          personalScorePopoverOpen = false;
          popover.remove();
          const fullRange = draftMin <= 0 && draftMax >= 10;
          applyLocalPersonalFilter({ scoreMin: fullRange ? null : draftMin, scoreMax: fullRange ? null : draftMax });
        }, "qb-shell-filter-option")
      );
      popover.append(titleRow, slider, actions);
      document.body.append(popover);
      const scoreRect = scoreButton.getBoundingClientRect();
      const popupWidth = popover.offsetWidth || 360;
      const popupLeft = Math.min(Math.max(12, scoreRect.left), Math.max(12, window.innerWidth - popupWidth - 12));
      popover.style.left = `${popupLeft}px`;
      popover.style.top = `${scoreRect.bottom + 10}px`;
    }
    const advancedCount = [
      criteria.unrated || criteria.myRating ? 1 : 0,
      value(criteria.period) ? 1 : 0,
      value(criteria.country) ? 1 : 0,
      value(criteria.genre) ? 1 : 0
    ].reduce((sum, item) => sum + item, 0);
    const advancedToggle = makeFilterButton(advancedCount ? `筛选 · ${advancedCount}` : "筛选", personalAdvancedFiltersOpen || advancedCount > 0, () => {
      personalAdvancedFiltersOpen = !personalAdvancedFiltersOpen;
      if (!personalAdvancedFiltersOpen) personalAdvancedFilterSection = "";
      personalScorePopoverOpen = false;
      renderFilters(personalLocalFilterState);
    });
    addPrimaryGroup("", [advancedToggle]);

    primaryRow.querySelectorAll("button").forEach(button => {
      if (!ready && !button.textContent?.match(/^(看过|想看|在看)$/u)) button.disabled = true;
    });
    if (!ready) {
      const hint = document.createElement("div");
      hint.className = "qb-shell-filter-hint qb-shell-personal-index-hint";
      if (personalLocalFilterState?.error) hint.textContent = `索引建立失败：${value(personalLocalFilterState.error)}`;
      else if (personalLocalFilterState?.building && Number(personalLocalFilterState.sourceTotal) > 0)
        hint.textContent = `正在建立完整个人库索引：已读取 ${Number(personalLocalFilterState.loaded) || 0} 部，源总量 ${Number(personalLocalFilterState.sourceTotal) || 0}`;
      else hint.textContent = "正在建立完整个人库索引…";
      host.append(hint);
      return;
    }

    if (!personalAdvancedFiltersOpen) return;

    const advanced = document.createElement("div");
    advanced.className = "qb-shell-advanced-filter-panel qb-shell-advanced-filter-compact";
    const categoryRow = document.createElement("div");
    categoryRow.className = "qb-shell-advanced-category-row";

    const ratingKey = criteria.unrated ? "unrated" : criteria.myRating ? String(criteria.myRating) : "";
    const categoryMeta = [
      { key: "rating", label: ratingKey ? `我的评分 · ${ratingKey === "unrated" ? "未评分" : `${ratingKey}星`}` : "我的评分" },
      { key: "period", label: value(criteria.period) ? `年代 · ${value(criteria.period).replace(/^year:/u, "").replace(/^decade:(\d{4})$/u, "$1年代")}` : "年代" },
      { key: "country", label: value(criteria.country) ? `地区 · ${value(criteria.country)}` : "地区" },
      { key: "genre", label: value(criteria.genre) ? `题材 · ${value(criteria.genre)}` : "题材" }
    ];
    categoryMeta.forEach(item => categoryRow.append(makeFilterButton(item.label, personalAdvancedFilterSection === item.key, () => {
      personalAdvancedFilterSection = personalAdvancedFilterSection === item.key ? "" : item.key;
      renderFilters(personalLocalFilterState);
    }, "qb-shell-filter-option")));
    const clearAdvanced = makeFilterButton("清除筛选", false, () => {
      personalAdvancedFilterSection = "";
      applyLocalPersonalFilter({ myRating: null, unrated: false, period: "", country: "", genre: "" });
    }, "qb-shell-filter-option");
    if (advancedCount === 0) clearAdvanced.disabled = true;
    categoryRow.append(clearAdvanced);
    advanced.append(categoryRow);

    const renderOptionPanel = (options, selected, apply) => {
      const panel = document.createElement("div");
      panel.className = "qb-shell-advanced-options-panel";
      options.forEach(option => panel.append(makeFilterButton(option.label, option.value === selected, () => apply(option.value), "qb-shell-filter-option")));
      advanced.append(panel);
    };

    if (personalAdvancedFilterSection === "rating") {
      renderOptionPanel([
        { label: "全部", value: "" },
        { label: "5星", value: "5" },
        { label: "4星", value: "4" },
        { label: "3星", value: "3" },
        { label: "2星", value: "2" },
        { label: "1星", value: "1" },
        { label: "未评分", value: "unrated" }
      ], ratingKey, rating => {
        if (rating === "unrated") applyLocalPersonalFilter({ myRating: null, unrated: true });
        else if (!rating) applyLocalPersonalFilter({ myRating: null, unrated: false });
        else applyLocalPersonalFilter({ myRating: Number(rating), unrated: false });
      });
    } else if (personalAdvancedFilterSection === "period") {
      const currentYear = new Date().getFullYear();
      const recentYears = Array.from({ length: 5 }, (_, index) => currentYear - index);
      const currentDecade = Math.floor((currentYear - 5) / 10) * 10;
      const decades = [];
      for (let decade = currentDecade; decade >= 1960; decade -= 10) decades.push(decade);
      const options = [
        { label: "全部", value: "" },
        ...recentYears.map(year => ({ label: String(year), value: `year:${year}` })),
        ...decades.map(decade => ({ label: decade >= 2000 ? `${decade}年代` : `${String(decade).slice(2)}年代`, value: `decade:${decade}` }))
      ];
      renderOptionPanel(options, value(criteria.period), period => applyLocalPersonalFilter({ period }));
    } else if (personalAdvancedFilterSection === "country") {
      const countries = Array.isArray(facets.countries) ? facets.countries.map(item => value(item)).filter(Boolean) : [];
      renderOptionPanel([{ label: "全部", value: "" }, ...countries.map(item => ({ label: item, value: item }))], value(criteria.country), country => applyLocalPersonalFilter({ country }));
    } else if (personalAdvancedFilterSection === "genre") {
      const genres = Array.isArray(facets.genres) ? facets.genres.map(item => value(item)).filter(Boolean) : [];
      renderOptionPanel([{ label: "全部", value: "" }, ...genres.map(item => ({ label: item, value: item }))], value(criteria.genre), genre => applyLocalPersonalFilter({ genre }));
    }

    host.append(advanced);
  };
  const renderFilters = snapshot => {
    document.querySelector(".qb-shell-score-popover")?.remove();
    if (viewKind !== "personal") personalScorePopoverOpen = false;
    const host = filtersHost();
    if (!host) return;
    host.replaceChildren();
    if (viewKind === "search") return;
    if (viewKind === "personal") {
      const capabilitySnapshot = value(snapshot?.source) === "frodo-local"
        ? snapshot
        : personalLocalFilterState;
      if (capabilitySnapshot) {
        renderPersonalLocalFilters(host, capabilitySnapshot);
        return;
      }
      const row = document.createElement("div");
      row.className = "qb-shell-filter-row qb-shell-personal-status-row";
      const label = document.createElement("span");
      label.className = "qb-shell-filter-label";
      label.textContent = "状态";
      row.append(label);
      Object.entries(personalStatusMeta).forEach(([statusValue, labelText]) => {
        row.append(makeFilterButton(labelText, statusValue === personalStatus, () => {
          if (busy || statusValue === personalStatus) return;
          beginListSwitch();
          updatePersonalUi(statusValue);
          setBusy(true);
          setStatus(`正在读取“${labelText}”影片…`);
          post("doubanShellNavigatePersonal", { status: statusValue, requestId: `shell-personal-${Date.now()}` });
        }));
      });
      host.append(row);
      const groups = Array.isArray(snapshot?.groups) ? snapshot.groups : [];
      groups.forEach(group => {
        const title = value(group?.title);
        if (!title) return;
        const groupRow = document.createElement("div");
        groupRow.className = "qb-shell-filter-row qb-shell-personal-filter-row";
        const groupLabel = document.createElement("span");
        groupLabel.className = "qb-shell-filter-label";
        groupLabel.textContent = title;
        groupRow.append(groupLabel);
        const options = Array.isArray(group?.options) ? group.options : [];
        options.forEach(option => {
          const optionLabel = value(option?.label || option);
          const optionUrl = value(option?.url);
          if (!optionLabel || !/^https:\/\/movie\.douban\.com\/people\/\d+\/(?:collect|wish|do)(?:\?|$)/u.test(optionUrl)) return;
          groupRow.append(makeFilterButton(optionLabel, option?.selected, () => {
            if (busy || option?.selected) return;
            busy = true;
            setBusy(true);
            setStatus(`正在应用筛选：${title} / ${optionLabel}`);
            post("doubanShellApplyPersonalFilter", {
              requestId: `shell-personal-filter-${Date.now()}`,
              url: optionUrl
            });
          }));
        });
        host.append(groupRow);
      });
      return;
    }
    const data = snapshot && typeof snapshot === "object" ? snapshot : {};
    const addRow = (title, values, kind) => {
      const list = Array.isArray(values) ? values : [];
      if (!list.length) return;
      const row = document.createElement("div");
      row.className = "qb-shell-filter-row";
      const label = document.createElement("span");
      label.className = "qb-shell-filter-label";
      label.textContent = title;
      row.append(label);
      list.forEach(item => {
        const itemLabel = value(item?.label || item);
        if (!itemLabel) return;
        row.append(makeFilterButton(itemLabel, item?.selected, () => {
          busy = true;
          post("doubanShellApplyFilter", {
            requestId: `shell-filter-${Date.now()}`,
            kind,
            label: itemLabel
          });
          setStatus(`正在应用筛选：${itemLabel}`);
        }));
      });
      host.append(row);
    };
    addRow("分类", data.primary, "primary");
    addRow("标签", data.secondary, "secondary");
    const groups = Array.isArray(data.groups) ? data.groups : [];
    groups.forEach(group => {
      const title = value(group?.title);
      if (!title) return;
      const row = document.createElement("div");
      row.className = "qb-shell-filter-row";
      const label = document.createElement("span");
      label.className = "qb-shell-filter-label";
      label.textContent = title;
      row.append(label);
      const display = value(group?.value) || "筛选";
      row.append(makeFilterButton(display, Boolean(group?.selected), () => {
        busy = true;
        post("doubanShellFilterGroup", {
          requestId: `shell-filter-group-${Date.now()}`,
          title
        });
        setStatus(`正在读取筛选：${title}`);
      }));
      host.append(row);
    });
    if (!host.children.length) {
      const hint = document.createElement("span");
      hint.className = "qb-shell-filter-hint";
      hint.textContent = "正在读取豆瓣原生筛选…";
      host.append(hint);
    }
  };

  const renderPaging = (paging, searchLinks = [], currentSearchPageUrl = "") => {
    const host = pagingHost();
    if (!host) return;
    pagingError = false;
    pagingObserver?.disconnect();
    pagingObserver = null;
    host.replaceChildren();
    if (viewKind === "search") {
      searchPageUrl = value(currentSearchPageUrl) || searchPageUrl;
      const links = (Array.isArray(searchLinks) ? searchLinks : Array.isArray(paging?.searchPageLinks) ? paging.searchPageLinks : [])
        .filter(link => /^https:\/\/search\.douban\.com\/movie\/subject_search\/?(?:\?|$)/iu.test(value(link?.url)));
      const pageStart = url => {
        try { return Number.parseInt(new URL(url).searchParams.get("start") || "0", 10) || 0; } catch { return 0; }
      };
      const currentStart = pageStart(searchPageUrl);
      const loadSearchPage = link => {
        const url = value(link?.url);
        if (!url || busy) return;
        beginListSwitch();
        setBusy(true);
        setStatus(`正在读取豆瓣搜索${value(link?.label) || "分页"}…`);
        post("doubanShellSearchPage", { requestId: `shell-search-page-${Date.now()}`, url, append: false });
      };
      if (!links.length) {
        const hint = document.createElement("span");
        hint.className = "qb-shell-filter-hint";
        hint.textContent = "当前搜索结果没有更多原生分页。";
        host.append(hint);
        return;
      }
      links.forEach(link => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "qb-shell-filter-button";
        button.textContent = value(link?.label);
        button.classList.toggle("qb-active", pageStart(link?.url) === currentStart);
        button.disabled = busy || pageStart(link?.url) === currentStart;
        button.addEventListener("click", () => {
          loadSearchPage(link);
        });
        host.append(button);
      });
      return;
    }
    if (paging?.hasMore) {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = value(paging.label) || "加载更多";
      button.disabled = busy;
      button.addEventListener("click", () => {
        if (busy || pendingPaging) return;
        const requestId = `shell-page-${Date.now()}`;
        pendingPaging = {
          requestId,
          epoch: pagingEpoch,
          viewKind,
          contentType: viewKind === "personal" ? "personal" : currentContentType
        };
        pagingError = false;
        busy = true;
        button.disabled = true;
        setStatus(`正在读取豆瓣下一页${viewKind === "personal" ? "个人影片" : contentTypeLabel()}…`);
        post("doubanShellLoadMore", { requestId });
      });
      host.append(button);
      if ((viewKind === "personal" || viewKind === "explore") && typeof IntersectionObserver === "function") {
        const sentinel = document.createElement("span");
        sentinel.className = "qb-shell-paging-sentinel";
        sentinel.setAttribute("aria-hidden", "true");
        host.append(sentinel);
        pagingObserver = new IntersectionObserver(entries => {
          if (!entries.some(entry => entry.isIntersecting) || busy || pendingPaging) return;
          button.click();
        }, {
          root: null,
          rootMargin: viewKind === "personal" ? "0px 0px 1200px 0px" : "0px 0px 720px 0px",
          threshold: 0.01
        });
        pagingObserver.observe(sentinel);
      }
    } else {
      const hint = document.createElement("span");
      hint.className = "qb-shell-filter-hint";
      hint.textContent = "已到豆瓣当前结果末页";
      host.append(hint);
    }
  };

  const setBusy = valueBusy => {
    busy = Boolean(valueBusy);
    filtersHost()?.querySelectorAll("button, select").forEach(control => { control.disabled = busy; });
    pagingHost()?.querySelectorAll("button").forEach(button => { button.disabled = busy; });
  };

  const markPagingError = () => {
    pagingError = true;
    const button = pagingHost()?.querySelector("button");
    if (button) {
      button.textContent = "重试加载更多";
      button.disabled = false;
    }
  };

  const applyPosterFallback = message => {
    const subjectId = value(message?.subjectId);
    const dataUri = value(message?.dataUri);
    if (!/^\d+$/u.test(subjectId) || !dataUri.startsWith("data:image/")) return;
    const posters = document.querySelectorAll(".qb-media-card-poster[data-subject-id]");
    for (const poster of posters) {
      if (poster.getAttribute("data-subject-id") !== subjectId) continue;
      const card = poster.closest(".qb-media-card");
      const title = value(card?.querySelector(".qb-media-card-title")?.textContent) || "影片";
      const overlays = [...poster.querySelectorAll(".qb-media-card-score, .qb-media-card-my-rating, .qb-media-card-comment")];
      const image = document.createElement("img");
      image.src = dataUri;
      image.alt = `${title}海报`;
      image.loading = "lazy";
      image.addEventListener("error", () => {
        const fallback = document.createElement("span");
        fallback.className = "qb-media-card-poster-fallback";
        fallback.textContent = "暂无海报";
        poster.replaceChildren(fallback);
        overlays.forEach(overlay => poster.append(overlay));
        poster.classList.add("qb-media-card-poster-placeholder");
      }, { once: true });
      poster.replaceChildren(image);
      overlays.forEach(overlay => poster.append(overlay));
      poster.classList.remove("qb-media-card-poster-placeholder");
      return;
    }
  };

  const handleMessage = event => {
    let message = event?.data || {};
    if (typeof message === "string") {
      try { message = JSON.parse(message); } catch { message = {}; }
    }
    if (message.type === "doubanShellOperationState") {
      setBusy(message.busy === true);
      return;
    }
    if (message.type === "doubanShellContentTypeState") {
      if (message.contentType) updateContentTypeUi(message.contentType);
      setBusy(message.busy === true);
      return;
    }
    if (message.type === "doubanShellContentTypeError") {
      setBusy(false);
      setStatus(value(message.error) || "豆瓣探索页面加载失败，请重试。", true);
      return;
    }
    if (message.type === "doubanShellPosterFallback") {
      applyPosterFallback(message);
      return;
    }
    if (message.type === "doubanShellWatchlistRefresh") {
      setBusy(true);
      setStatus("正在刷新本地待看…");
      Promise.resolve(window.QbDoubanWatchlist?.loadShell?.()).then(result => {
        setStatus(`本地待看共 ${Array.isArray(result?.items) ? result.items.length : 0} 部`);
      }).catch(error => {
        setStatus(String(error?.message || error || "本地待看刷新失败。"), true);
      }).finally(() => setBusy(false));
      return;
    }
    if (message.type === "doubanShellData" && viewKind === "watchlist") return;
    if (message.type === "doubanShellPersonalState") {
      updatePersonalUi(message.personalStatus);
      setBusy(message.busy === true);
      return;
    }
    if (message.type === "doubanShellPersonalItemMutation") {
      if (viewKind !== "personal") return;
      const subjectId = value(message.subjectId);
      if (!/^\d+$/u.test(subjectId)) return;
      const card = grid()?.querySelector(`[data-subject-id="${subjectId}"]`);
      if (!card) return;
      const fromStatus = value(message.fromStatus);
      const toStatus = value(message.toStatus);
      if (message.deleted === true || (fromStatus === personalStatus && toStatus !== personalStatus)) {
        card.remove();
        return;
      }
      if (toStatus !== personalStatus) return;
      const poster = card.querySelector(".qb-media-card-poster");
      if (!poster) return;
      const rating = Number(message.myRating) || 0;
      let ratingNode = poster.querySelector(".qb-media-card-my-rating");
      if (rating > 0) {
        if (!ratingNode) {
          ratingNode = document.createElement("strong");
          ratingNode.className = "qb-media-card-my-rating";
          poster.append(ratingNode);
        }
        ratingNode.textContent = "★".repeat(Math.max(0, Math.min(5, rating)));
      } else {
        ratingNode?.remove();
      }
      if (message.score !== null && message.score !== undefined && Number(message.score) > 0) {
        let scoreNode = poster.querySelector(".qb-media-card-score");
        if (!scoreNode) {
          scoreNode = document.createElement("strong");
          scoreNode.className = "qb-media-card-score";
          poster.append(scoreNode);
        }
        scoreNode.textContent = formatPublicScore(message.score);
      }
      return;
    }
    if (message.type === "doubanShellLocalPersonalFilters") {
      updatePersonalUi(message.personalStatus);
      renderFilters(message.filters);
      const filters = message.filters || {};
      if (filters.error) setStatus(`完整个人库筛选索引失败：${value(filters.error)}`, true);
      else if (filters.building && Number(filters.sourceTotal) > 0)
        setStatus(`正在建立完整个人库筛选索引：已读取 ${Number(filters.loaded) || 0} 部，源总量 ${Number(filters.sourceTotal) || 0}`);
      else if (filters.building) setStatus("正在建立完整个人库筛选索引…");
      else if (filters.ready) setStatus(`完整个人库筛选已就绪，共 ${Number(filters.total) || 0} 部`);
      return;
    }
    if (message.type === "doubanShellFilterOptions") {
      setBusy(false);
      renderFilters(message.filters);
      renderFilterOptions(message);
      setStatus(`请选择“${value(message.groupTitle)}”的豆瓣原生筛选项`);
      return;
    }
    if (message.type === "doubanShellFilterError") {
      setBusy(false);
      setStatus(value(message.error) || "豆瓣筛选失败。", true);
      return;
    }
    if (message.type === "doubanShellLoadMoreError") {
      const requestId = value(message.requestId);
      if (!pendingPaging || (requestId && pendingPaging.requestId !== requestId)) return;
      pendingPaging = null;
      setBusy(false);
      markPagingError();
      setStatus(value(message.error) || "豆瓣下一页读取失败。", true);
      return;
    }
    if (message.type !== "doubanShellData") return;

    const operation = value(message.operation);
    const pagingResponse = isPagingOperation(operation);
    const messageViewKind = message.contentType === "personal" ? "personal" : message.contentType === "search" ? "search" : "explore";
    const messageContentType = messageViewKind === "personal"
      ? "personal"
      : messageViewKind === "search" ? "search"
      : message.contentType === "tv" ? "tv" : "movie";
    const requestId = value(message.requestId);
    if (pagingResponse) {
      if (!pendingPaging || pendingPaging.requestId !== requestId || pendingPaging.epoch !== pagingEpoch ||
        pendingPaging.viewKind !== messageViewKind || pendingPaging.contentType !== messageContentType) return;
      pendingPaging = null;
    } else {
      pagingEpoch += 1;
      pendingPaging = null;
      pagingError = false;
    }
    setBusy(false);
    if (message.contentType === "personal") updatePersonalUi(message.personalStatus);
    else if (message.contentType === "search") updateSearchUi(message.query);
    else updateContentTypeUi(message.contentType);
    if (message.error) {
      setStatus(message.error, true);
      renderFilters(message.filters);
      if (!pagingResponse) {
        renderPaging(message.paging);
        render([]);
        settleListSwitch();
      } else {
        markPagingError();
      }
      post("doubanShellDataApplied", { requestId: value(message.requestId), itemCount: 0, error: message.error });
      return;
    }
    const appendPaging = pagingResponse && (
      messageViewKind === "explore" ||
      messageViewKind === "search" ||
      (messageViewKind === "personal" && ["frodo-api", "frodo-local-index"].includes(value(message?.dom?.source)))
    );
    render(message.items, { append: appendPaging });
    const filtersForRender = messageViewKind === "personal" && personalLocalFilterState
      ? personalLocalFilterState
      : message.filters;
    renderFilters(filtersForRender);
    renderPaging(message.paging, message.searchPageLinks, message.searchPageUrl);
    if (!pagingResponse) settleListSwitch();
    const resultLabel = viewKind === "search" ? "搜索结果" : viewKind === "personal" ? "个人影片" : contentTypeLabel();
    if (viewKind === "personal" && value(message?.filters?.source) === "frodo-local") {
      setStatus(`完整库筛选 ${Number(message.filters.matched) || 0} 部，已显示 ${Number(message.filters.shown) || 0} 部`);
    } else {
      setStatus(`${pagingResponse ? "已读取" : "已从豆瓣真实页面读取"} ${Array.isArray(message.items) ? message.items.length : 0} 部${resultLabel}`);
    }
    post("doubanShellDataApplied", {
      requestId: value(message.requestId),
      itemCount: Array.isArray(message.items) ? message.items.length : 0
    });
  };

  const boot = () => {
    if (!root()) return;
    addStyle();
    window.chrome?.webview?.addEventListener("message", handleMessage);
    document.getElementById("qb-douban-shell-login")?.addEventListener("click", () => post("doubanShellLogin"));
    searchForm()?.addEventListener("submit", event => {
      event.preventDefault();
      if (busy) return;
      const query = value(searchInput()?.value);
      if (!query) {
        setStatus("请输入要在豆瓣影视页面搜索的关键词。", true);
        searchInput()?.focus();
        return;
      }
      beginListSwitch();
      updateSearchUi(query);
      setBusy(true);
      setStatus(`正在打开豆瓣原生搜索：${query}…`);
      post("doubanShellSearch", { requestId: `shell-search-${Date.now()}`, query });
    });
    document.querySelector("[data-douban-watchlist]")?.addEventListener("click", openWatchlist);
    document.querySelectorAll("[data-douban-content-type]").forEach(button => {
      button.addEventListener("click", () => {
        const contentType = button.getAttribute("data-douban-content-type");
        if (!contentType || (viewKind === "explore" && contentType === currentContentType) || busy) return;
        beginListSwitch();
        updateContentTypeUi(contentType);
        setBusy(true);
        setStatus(`正在切换到探索${contentTypeLabel()}…`);
        post("doubanShellNavigateContentType", { contentType, requestId: `shell-content-type-${Date.now()}` });
      });
    });
    document.querySelectorAll("[data-douban-personal-status]").forEach(button => {
      button.addEventListener("click", () => {
        if (busy) return;
        const statusValue = button.getAttribute("data-douban-personal-status");
        if (!statusValue) return;
        beginListSwitch();
        updatePersonalUi(statusValue);
        setBusy(true);
        setStatus(`正在读取“${personalStatusLabel()}”影片…`);
        post("doubanShellNavigatePersonal", { status: statusValue, requestId: `shell-personal-${Date.now()}` });
      });
    });
    updateContentTypeUi(currentContentType);
    post("doubanShellReady", { version: "shell-0.1", mode: contentTypeMode(), contentType: currentContentType });
  };

  window.QbDoubanShell = Object.freeze({ render, setStatus });
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot, { once: true });
  else boot();
})();
