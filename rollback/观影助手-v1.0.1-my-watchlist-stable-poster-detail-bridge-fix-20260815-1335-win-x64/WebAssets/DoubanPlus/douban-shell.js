(() => {
  "use strict";

  const cssText = __QB_DOUBAN_SHELL_CSS__;
  const root = () => document.getElementById("qb-douban-shell-root");
  const grid = () => document.getElementById("qb-douban-shell-grid");
  const status = () => document.getElementById("qb-douban-shell-status");
  const filtersHost = () => document.getElementById("qb-douban-shell-filters");
  const pagingHost = () => document.getElementById("qb-douban-shell-paging");
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
  let pagingObserver = null;
  let pagingEpoch = 0;
  let pendingPaging = null;
  let pagingError = false;
  let resetViewportOnNextData = false;
  const personalStatusMeta = { collect: "看过", wish: "想看", do: "在看" };

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
    personalStatus = Object.prototype.hasOwnProperty.call(personalStatusMeta, statusValue) ? statusValue : "collect";
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
      comment: value(item?.comment)
    };
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
      mode: viewKind === "personal" ? `personal-${personalStatus}` : viewKind === "watchlist" ? "watchlist" : contentTypeMode()
    });
  };

  const render = (items, { append = false } = {}) => {
    const target = grid();
    if (!target) return;
    target.classList.remove("qb-watchlist-grid", "qb-watchlist-shell-grid");
    const normalizedItems = (Array.isArray(items) ? items : []).map(normalize).filter(item => /^\d+$/u.test(item.subjectId));
    if (!append) target.replaceChildren();
    if (!normalizedItems.length && !append) {
      target.innerHTML = `<div class="qb-shell-empty">豆瓣没有返回${viewKind === "personal" ? `“${personalStatusLabel()}”` : contentTypeLabel()}卡片。</div>`;
      return;
    }
    const existingIds = append
      ? new Set([...target.querySelectorAll("[data-subject-id]")].map(node => node.dataset.subjectId).filter(Boolean))
      : new Set();
    if (append) target.querySelector(".qb-shell-empty")?.remove();
    normalizedItems.forEach(item => {
      if (existingIds.has(item.subjectId)) return;
      const mediaTypeLabel = item.contentType === "tv" ? "电视剧" : viewKind === "personal" ? "电影" : contentTypeLabel();
      const publicScore = viewKind === "explore" && item.score
        ? value(item.score).replace(/^豆瓣\s*/u, "")
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
        score: publicScore || personalScore,
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

  const renderFilters = snapshot => {
    const host = filtersHost();
    if (!host) return;
    host.replaceChildren();
    if (viewKind === "personal") {
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
          personalStatus = statusValue;
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

  const renderPaging = paging => {
    const host = pagingHost();
    if (!host) return;
    pagingError = false;
    pagingObserver?.disconnect();
    pagingObserver = null;
    host.replaceChildren();
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
        }, { root: null, rootMargin: "0px 0px 720px 0px", threshold: 0.01 });
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
    filtersHost()?.querySelectorAll("button").forEach(button => { button.disabled = busy; });
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
      const overlays = [...poster.querySelectorAll(".qb-media-card-score, .qb-media-card-comment")];
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
    const messageViewKind = message.contentType === "personal" ? "personal" : "explore";
    const messageContentType = messageViewKind === "personal"
      ? "personal"
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
    render(message.items, { append: pagingResponse && messageViewKind === "explore" });
    renderFilters(message.filters);
    renderPaging(message.paging);
    if (!pagingResponse) settleListSwitch();
    setStatus(`${pagingResponse ? "已追加" : "已从豆瓣真实页面读取"} ${Array.isArray(message.items) ? message.items.length : 0} 部${viewKind === "personal" ? "个人影片" : contentTypeLabel()}`);
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
