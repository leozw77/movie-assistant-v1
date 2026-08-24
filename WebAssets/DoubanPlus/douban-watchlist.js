(() => {
  "use strict";

  const WATCHLIST_QUERY = "qb_watchlist";
  const WATCHLIST_ROOT_ID = "qb-douban-watchlist-content";
  const MENU_ID = "qb-douban-watchlist-menu";
  const TOAST_ID = "qb-douban-watchlist-toast";
  const POSTER_TARGET_SELECTOR = [
    ".qb-media-card-poster",
    ".qb-personal-poster",
    ".qb-explore-poster",
    ".atv-search-page-card-poster",
    ".qb-watchlist-poster",
    ".atv-poster-card",
    ".atv-rec-card",
    ".atv-rec-poster"
  ].join(", ");
  const cssText = __QB_DOUBAN_WATCHLIST_CSS__;
  const pending = new Map();
  let sequence = 0;
  let contextItem = null;

  const text = element => String(element?.textContent || "").replace(/\s+/gu, " ").trim();
  const subjectUrl = subjectId => `https://movie.douban.com/subject/${subjectId}/`;
  const validSubjectUrl = value => {
    try {
      const url = new URL(value, location.href);
      return url.protocol === "https:" && url.hostname === "movie.douban.com" && /^\/subject\/\d+\/?$/u.test(url.pathname)
        ? `https://movie.douban.com${url.pathname.replace(/\/$/u, "")}/`
        : "";
    } catch {
      return "";
    }
  };
  const validPosterSourceUrl = value => {
    try {
      const url = new URL(value, location.href);
      const host = url.hostname.toLowerCase();
      return url.protocol === "https:" && (
        host === "doubanio.com" || host.endsWith(".doubanio.com") ||
        host === "douban.com" || host.endsWith(".douban.com")
      ) ? url.href : "";
    } catch {
      return "";
    }
  };
  const posterSourceUrlFromTarget = target => {
    const poster = target?.closest?.(".qb-media-card-poster, .atv-poster-card, .atv-rec-card, .atv-rec-poster");
    const image = poster?.querySelector("img");
    const candidates = [
      poster?.dataset.posterUrl,
      poster?.dataset.posterSourceUrl,
      image?.currentSrc,
      image?.getAttribute("src"),
      image?.getAttribute("data-src"),
      image?.getAttribute("data-original")
    ];
    return candidates.map(validPosterSourceUrl).find(Boolean) || "";
  };
  const request = (type, payload = {}, timeoutMs = 8000) => new Promise((resolve, reject) => {
    if (!window.chrome?.webview) {
      reject(new Error("待看宿主桥接不可用。"));
      return;
    }
    const requestId = `qb-watchlist-${Date.now()}-${++sequence}`;
    const timer = setTimeout(() => {
      pending.delete(requestId);
      reject(new Error("待看操作超时。"));
    }, timeoutMs);
    pending.set(requestId, { resolve, reject, timer });
    try {
      window.chrome.webview.postMessage({ type, requestId, ...payload });
    } catch (error) {
      clearTimeout(timer);
      pending.delete(requestId);
      reject(error);
    }
  });
  const post = (type, payload = {}) => {
    if (!window.chrome?.webview) return false;
    window.chrome.webview.postMessage({ type, ...payload });
    return true;
  };

  const isDoubanPage = () => location.protocol === "https:" && /(?:^|\.)douban\.com$/u.test(location.hostname);
  const isShellPage = () => document.getElementById("qb-douban-shell-root") !== null;
  const isSupportedPage = () => isDoubanPage() || isShellPage();
  const cardSnapshot = target => {
    const card = target?.closest?.(".qb-media-card");
    if (!card) return {};
    const info = new Map([...card.querySelectorAll(".qb-media-card-info-row")].map(row => [
      text(row.querySelector(".qb-media-card-info-label")),
      text(row.querySelector(".qb-media-card-info-value"))
    ]));
    return {
      identity: text(card.querySelector(".qb-media-card-identity")),
      genre: info.get("类型") || "",
      director: info.get("导演") || "",
      cast: info.get("主演") || "",
      score: text(card.querySelector(".qb-media-card-score")),
      comment: text(card.querySelector(".qb-media-card-comment-popover"))
    };
  };
  const refreshPage = () => {
    if (!isSupportedPage()) return;
    window.chrome?.webview?.postMessage({
      type: "doubanPageRefresh",
      requestId: `refresh-${Date.now()}-${++sequence}`,
      url: location.href,
      viewKind: document.getElementById("qb-douban-shell-root")?.dataset.viewKind || ""
    });
    hideMenu();
  };
  const goHome = () => {
    if (!isSupportedPage()) return;
    window.chrome?.webview?.postMessage({
      type: "doubanPageHome",
      requestId: `home-${Date.now()}-${++sequence}`,
      url: location.href
    });
    hideMenu();
  };

  const receive = event => {
    const message = event?.data;
    if (!message || message.type !== "doubanWatchlistResponse") return;
    const entry = pending.get(message.requestId);
    if (!entry) return;
    pending.delete(message.requestId);
    clearTimeout(entry.timer);
    const payload = message.payload || {};
    if (message.ok) entry.resolve(payload);
    else entry.reject(new Error(payload.error || "待看操作失败。"));
  };
  window.chrome?.webview?.addEventListener("message", receive);

  const style = document.createElement("style");
  style.id = "qb-douban-watchlist-style";
  style.textContent = cssText;
  const mountStyle = () => {
    const target = document.head || document.documentElement || document.body;
    if (!target) return false;
    if (!document.getElementById(style.id)) target.append(style);
    return true;
  };
  if (!mountStyle()) document.addEventListener("DOMContentLoaded", mountStyle, { once: true });

  const currentPersonalUrl = () => {
    const url = new URL(location.href);
    url.searchParams.delete(WATCHLIST_QUERY);
    return url.href;
  };
  const personalWatchlistUrl = () => {
    let raw = currentPersonalUrl();
    try {
      const stored = sessionStorage.getItem("qb-douban-personal-url-v1") || "";
      if (/^https:\/\/movie\.douban\.com\/people\/\d+\/(?:collect|wish|do)\/?(?:\?.*)?$/u.test(stored)) raw = stored;
    } catch { }
    const url = new URL(raw, location.href);
    url.searchParams.set(WATCHLIST_QUERY, "1");
    return url.href;
  };
  const isWatchlistPage = () => new URL(location.href).searchParams.get(WATCHLIST_QUERY) === "1";
  const isShellWatchlistPage = () => isShellPage() && document.getElementById("qb-douban-shell-root")?.dataset.viewKind === "watchlist";

  const postWatchlistDetail = (event, subjectId, subjectHref, shellTarget) => {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    if (shellTarget) {
      post("doubanShellOpenDetail", { subjectId, subjectUrl: subjectHref, mode: "watchlist" });
      return;
    }
    post("doubanPersonalOpenSubject", {
      subjectId,
      subjectUrl: subjectHref,
      personalUrl: currentPersonalUrl(),
      source: "watchlist"
    });
  };

  const detailItemFromPage = () => {
    const match = location.pathname.match(/^\/subject\/(\d+)\/?$/u);
    if (!match) return null;
    return {
      subjectId: match[1],
      subjectUrl: subjectUrl(match[1]),
      title: text(document.querySelector(".atv-hero-title, .qb-douban-plus-detail h1, h1")) || `豆瓣条目 ${match[1]}`,
      source: "detail-page"
    };
  };

  const itemFromTarget = target => {
    const personalPoster = target.closest?.(".qb-personal-poster");
    if (personalPoster) {
      const card = personalPoster.closest(".qb-personal-card");
      const subjectId = card?.dataset.subjectId || "";
      if (/^\d+$/u.test(subjectId)) {
        return {
          ...cardSnapshot(target),
          subjectId,
          subjectUrl: subjectUrl(subjectId),
          title: text(card.querySelector(".qb-personal-card-title")) || `豆瓣条目 ${subjectId}`,
          year: text(card.querySelector(".qb-personal-card-year")).replace(/^上映年份\s*/u, ""),
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "personal"
        };
      }
    }

    const explorePoster = target.closest?.(".qb-explore-poster");
    if (explorePoster) {
      const card = explorePoster.closest(".qb-explore-card");
      const subjectId = explorePoster.dataset.subjectId || card?.dataset.subjectId || "";
      const url = validSubjectUrl(explorePoster.dataset.subjectUrl || card?.dataset.subjectUrl || "");
      if (/^\d+$/u.test(subjectId) && url) {
        return {
          ...cardSnapshot(target),
          subjectId,
          subjectUrl: url,
          title: text(card?.querySelector(".qb-explore-card-title")) || `豆瓣条目 ${subjectId}`,
          year: text(card?.querySelector(".qb-explore-chip")),
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "explore"
        };
      }
    }

    const searchPoster = target.closest?.(".atv-search-page-card-poster");
    if (searchPoster) {
      const card = searchPoster.closest(".atv-search-page-card");
      const url = validSubjectUrl(card?.getAttribute("href") || "");
      const match = url.match(/\/subject\/(\d+)\//u);
      if (url && match) {
        return {
          ...cardSnapshot(target),
          subjectId: match[1],
          subjectUrl: url,
          title: text(card.querySelector("h2")) || `豆瓣条目 ${match[1]}`,
          year: text(card.querySelector(".atv-search-page-card-subtitle")),
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "search"
        };
      }
    }

    const localPoster = target.closest?.(".qb-watchlist-poster");
    if (localPoster) {
      const subjectId = localPoster.dataset.subjectId || "";
      const url = validSubjectUrl(localPoster.dataset.subjectUrl || "");
      if (/^\d+$/u.test(subjectId) && url) {
        return {
          ...cardSnapshot(target),
          subjectId,
          subjectUrl: url,
          title: text(localPoster.closest(".qb-watchlist-card")?.querySelector(".qb-watchlist-card-title")) || `豆瓣条目 ${subjectId}`,
          source: "watchlist"
        };
      }
    }

    const shellPoster = target.closest?.(".qb-media-card-poster");
    if (shellPoster) {
      const card = shellPoster.closest(".qb-media-card");
      const subjectId = shellPoster.dataset.subjectId || card?.dataset.subjectId || "";
      const url = validSubjectUrl(shellPoster.dataset.subjectUrl || card?.dataset.subjectUrl || "");
      if (/^\d+$/u.test(subjectId) && url) {
        return {
          ...cardSnapshot(target),
          subjectId,
          subjectUrl: url,
          title: text(card?.querySelector(".qb-media-card-title")) || `豆瓣条目 ${subjectId}`,
          year: text(card?.querySelector(".qb-media-card-identity")),
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "shell"
        };
      }
    }

    const recommendationCard = target.closest?.(".atv-rec-card, .atv-rec-poster");
    if (recommendationCard) {
      const card = recommendationCard.matches?.(".atv-rec-card")
        ? recommendationCard
        : recommendationCard.closest?.(".atv-rec-card");
      const linkedSubject = card?.matches?.('a[href*="/subject/"]')
        ? card
        : (card?.closest?.('a[href*="/subject/"]') || card?.querySelector?.('a[href*="/subject/"]'));
      const linkedUrl = validSubjectUrl(linkedSubject?.getAttribute?.("href") || linkedSubject?.href || "");
      const linkedMatch = linkedUrl.match(/\/subject\/(\d+)\//u);
      if (linkedUrl && linkedMatch) {
        return {
          subjectId: linkedMatch[1],
          subjectUrl: linkedUrl,
          title: text(card?.querySelector?.(".atv-rec-title")) ||
            String(card?.querySelector?.("img")?.alt || linkedSubject?.getAttribute?.("title") || "").trim() ||
            `豆瓣条目 ${linkedMatch[1]}`,
          posterSourceUrl: posterSourceUrlFromTarget(target),
          source: "detail-recommendation"
        };
      }
      // A recommendation without its own subject link must never fall back to
      // the enclosing detail page subject.
      return null;
    }

    const heroPoster = target.closest?.(".atv-poster-card");
    if (heroPoster) {
      // The hero poster is a button without a subject link. Only this poster may
      // fall back to the current detail page subject.
      const linkedSubject = heroPoster.matches?.('a[href*="/subject/"]')
        ? heroPoster
        : (heroPoster.closest?.('a[href*="/subject/"]') || heroPoster.querySelector?.('a[href*="/subject/"]'));
      const linkedUrl = validSubjectUrl(linkedSubject?.getAttribute?.("href") || linkedSubject?.href || "");
      const linkedMatch = linkedUrl.match(/\/subject\/(\d+)\//u);
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
    return null;
  };

  const hideMenu = () => {
    const menu = document.getElementById(MENU_ID);
    if (menu) menu.remove();
    contextItem = null;
  };
  const showToast = message => {
    let toast = document.getElementById(TOAST_ID);
    if (!toast) {
      toast = document.createElement("div");
      toast.id = TOAST_ID;
      document.body.append(toast);
    }
    toast.textContent = message;
    toast.classList.add("is-visible");
    setTimeout(() => toast?.classList.remove("is-visible"), 2200);
  };
  const menuButton = (label, handler, disabled = false) => {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    button.disabled = disabled;
    button.addEventListener("click", handler);
    return button;
  };
  const appendPtSearchAction = (menu, item) => {
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
  const appendPageActions = menu => {
    menu.append(menuButton("刷新页面", refreshPage));
    menu.append(menuButton("返回首页", goHome));
  };
  const showPageMenu = (x, y) => {
    hideMenu();
    const menu = document.createElement("div");
    menu.id = MENU_ID;
    menu.className = "qb-douban-watchlist-menu";
    menu.style.left = `${Math.min(x, Math.max(8, window.innerWidth - 210))}px`;
    menu.style.top = `${Math.min(y, Math.max(8, window.innerHeight - 100))}px`;
    appendPageActions(menu);
    document.body.append(menu);
  };
  const showMenu = async (x, y, item) => {
    hideMenu();
    contextItem = item;
    const menu = document.createElement("div");
    menu.id = MENU_ID;
    menu.className = "qb-douban-watchlist-menu";
    menu.style.left = `${Math.min(x, Math.max(8, window.innerWidth - 210))}px`;
    menu.style.top = `${Math.min(y, Math.max(8, window.innerHeight - 150))}px`;
    menu.append(menuButton("正在检查待看状态…", () => {}, true));
    appendPtSearchAction(menu, item);
    appendPageActions(menu);
    document.body.append(menu);
    try {
      const result = await request("doubanWatchlistStateRequest", item);
      if (contextItem !== item || !document.getElementById(MENU_ID)) return;
      menu.replaceChildren();
      if (result.item) {
        menu.append(menuButton("已在待看 · 移出待看", async () => {
          hideMenu();
          try {
            const response = await request("doubanWatchlistDelete", { subjectId: item.subjectId });
            showToast(response.removed ? "已移出本地待看" : "该影片不在本地待看");
            refreshWatchlistList();
          } catch (error) { showToast(String(error.message || error)); }
        }));
      } else {
        menu.append(menuButton("加入待看", async () => {
          hideMenu();
          try {
            const response = await request("doubanWatchlistAdd", item);
            if (response.duplicate) showToast(response.posterSaved ? "已在本地待看，海报已保存" : "已在本地待看");
            else showToast(response.posterSaved ? "已加入待看，海报已保存" : "已加入待看，海报稍后可重试");
          } catch (error) { showToast(String(error.message || error)); }
        }));
      }
      appendPtSearchAction(menu, item);
      appendPageActions(menu);
    } catch (error) {
      if (contextItem === item && document.getElementById(MENU_ID)) {
        menu.replaceChildren(menuButton(String(error.message || error), hideMenu));
        appendPtSearchAction(menu, item);
        appendPageActions(menu);
      }
    }
  };

  document.addEventListener("contextmenu", event => {
    // Poster identity has priority over the enclosing detail page. This is critical
    // for recommendation posters shown inside another movie's subject page.
    const posterItem = itemFromTarget(event.target);
    if (posterItem) {
      event.preventDefault();
      event.stopPropagation();
      showMenu(event.clientX, event.clientY, posterItem);
      return;
    }

    if (event.target?.closest?.(POSTER_TARGET_SELECTOR)) {
      event.preventDefault();
      event.stopPropagation();
      showToast("这张海报没有可用的豆瓣条目链接，无法进行 PT 搜索。");
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
  document.addEventListener("click", event => {
    if (!event.target.closest?.(`#${MENU_ID}`)) hideMenu();
  }, true);
  document.addEventListener("click", event => {
    if (event.defaultPrevented) return;
    const target = event.target;
    if (target?.closest?.(".qb-media-card-comment, .qb-watchlist-remove")) return;
    const poster = target?.closest?.(".qb-watchlist-poster");
    const card = poster?.closest?.(".qb-watchlist-card");
    const subjectId = card?.dataset.subjectId || poster?.dataset.subjectId || "";
    const subjectHref = validSubjectUrl(card?.dataset.subjectUrl || poster?.dataset.subjectUrl || "");
    if (!poster || !card || !/^\d+$/u.test(subjectId) || !subjectHref) return;
    postWatchlistDetail(event, subjectId, subjectHref, isShellWatchlistPage());
  }, true);
  document.addEventListener("keydown", event => { if (event.key === "Escape") hideMenu(); }, true);

  const appendWatchlistAction = root => {
    const header = root.querySelector(".qb-personal-header, .qb-explore-header");
    if (!header || header.querySelector(".qb-watchlist-action")) return;
    const link = document.createElement("a");
    link.className = "qb-watchlist-action";
    link.href = personalWatchlistUrl();
    link.textContent = "我的待看";
    link.setAttribute("aria-current", isWatchlistPage() ? "page" : "false");
    if (isWatchlistPage()) link.classList.add("qb-active");
    const search = header.querySelector(".qb-personal-search-host, .qb-explore-search-host");
    if (search) search.before(link);
    else header.append(link);
  };

  const requestWatchlistList = (target = document.getElementById(WATCHLIST_ROOT_ID)) => request("doubanWatchlistListRequest")
    .then(result => { renderWatchlistItems(result.items || [], target); return result; })
    .catch(error => { renderWatchlistError(error, target); throw error; });
  const refreshWatchlistList = () => {
    const target = isShellWatchlistPage()
      ? document.getElementById("qb-douban-shell-grid")
      : document.getElementById(WATCHLIST_ROOT_ID);
    return target ? requestWatchlistList(target) : Promise.resolve(null);
  };
  const formatAddedAt = value => {
    const raw = String(value || "").trim();
    if (!raw) return "未记录";
    const match = raw.match(/^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2}:\d{2})/u);
    return match ? `${match[1]} ${match[2]}` : raw.replace("T", " ").slice(0, 19);
  };
  const renderWatchlistError = (error, target = document.getElementById(WATCHLIST_ROOT_ID)) => {
    if (!target) return;
    target.classList.remove("qb-watchlist-grid", "qb-watchlist-shell-grid");
    target.innerHTML = `<div class="qb-watchlist-empty qb-watchlist-error">${String(error.message || error).replace(/[&<>]/gu, value => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" })[value])}</div>`;
  };
  const renderWatchlistItems = (items, target = document.getElementById(WATCHLIST_ROOT_ID)) => {
    if (!target) return;
    const shellTarget = target.id === "qb-douban-shell-grid";
    target.classList.toggle("qb-watchlist-grid", shellTarget);
    target.classList.toggle("qb-watchlist-shell-grid", shellTarget);
    target.replaceChildren();
    if (!items.length) {
      const empty = document.createElement("div");
      empty.className = shellTarget ? "qb-watchlist-empty qb-shell-empty" : "qb-watchlist-empty";
      empty.textContent = "本地待看暂无影片。可在个人页、搜索结果或详情页海报上右键加入。";
      target.append(empty);
      return;
    }
    const grid = shellTarget ? target : document.createElement("section");
    if (!shellTarget) grid.className = "qb-watchlist-grid";
    grid.setAttribute("aria-label", "本地待看列表");
    items.forEach(item => {
      const remove = menuButton("移出待看", async event => {
        event.preventDefault();
        event.stopPropagation();
        try {
          const response = await request("doubanWatchlistDelete", { subjectId: item.subjectId });
          showToast(response.removed ? "已移出本地待看" : "该影片不在本地待看");
          if (response.items) renderWatchlistItems(response.items, target);
        } catch (error) { showToast(String(error.message || error)); }
      });
      remove.className = "qb-watchlist-remove";
      const subjectHref = validSubjectUrl(item.subjectUrl) || subjectUrl(item.subjectId);
      const openDetailFromPoster = event => {
        postWatchlistDetail(event, item.subjectId, subjectHref, shellTarget);
      };
      const card = QbDoubanCard.render({
        model: {
          subjectId: item.subjectId,
          subjectUrl: subjectHref,
          title: item.title || `豆瓣条目 ${item.subjectId}`,
          // Watchlist cards must use the locally saved poster only. The source
          // URL is an add-time transport value and must never become a remote
          // image fallback while rendering the local list.
          posterUrl: item.posterUrl,
          identity: item.identity || (item.year ? `上映年份 ${item.year}` : ""),
          infoRows: [
            item.genre ? { label: "类型", value: item.genre } : null,
            item.director ? { label: "导演", value: item.director } : null,
            item.cast ? { label: "主演", value: item.cast } : null
          ].filter(Boolean),
          comment: item.comment,
          score: item.score
        },
        cardClass: "qb-personal-card qb-watchlist-card",
        posterClass: "qb-personal-poster qb-watchlist-poster",
        posterTag: shellTarget ? "div" : "a",
        posterHref: shellTarget ? undefined : subjectHref,
        bodyClass: "qb-personal-card-body qb-watchlist-card-body",
        titleClass: "qb-personal-card-title qb-watchlist-card-title",
        titleTag: shellTarget ? "h2" : "a",
        titleHref: shellTarget ? undefined : subjectHref,
        action: remove,
        onPosterOpen: openDetailFromPoster,
        onOpen: shellTarget ? openDetailFromPoster : undefined
      });
      grid.append(card);
    });
    if (!shellTarget) target.append(grid);
  };

  const loadShellWatchlist = () => {
    const target = document.getElementById("qb-douban-shell-grid");
    if (!target) return Promise.reject(new Error("统一 Shell 待看列表容器不存在。"));
    target.classList.remove("qb-watchlist-grid", "qb-watchlist-shell-grid");
    target.replaceChildren(Object.assign(document.createElement("div"), {
      className: "qb-shell-loading",
      textContent: "正在读取本地待看…"
    }));
    return request("doubanWatchlistListRequest")
      .then(result => { renderWatchlistItems(result.items || [], target); return result; })
      .catch(error => { renderWatchlistError(error, target); throw error; });
  };

  window.QbDoubanWatchlist = Object.freeze({ loadShell: loadShellWatchlist });

  const mountWatchlistPage = root => {
    if (!isWatchlistPage()) return;
    appendWatchlistAction(root);
    root.querySelector(".qb-personal-filter-bar")?.classList.add("qb-watchlist-hidden");
    root.querySelector(".qb-personal-pager")?.classList.add("qb-watchlist-hidden");
    root.querySelector(".qb-personal-content")?.classList.add("qb-watchlist-hidden");
    let content = root.querySelector(`#${WATCHLIST_ROOT_ID}`);
    if (!content) {
      content = document.createElement("section");
      content.id = WATCHLIST_ROOT_ID;
      content.className = "qb-watchlist-content";
      root.append(content);
      const loading = document.createElement("div");
      loading.className = "qb-watchlist-empty";
      loading.textContent = "正在读取本地待看…";
      content.append(loading);
      requestWatchlistList(content);
    }
  };

  const observe = () => {
    const personalRoot = document.getElementById("qb-douban-personal-root");
    const exploreRoot = document.getElementById("qb-douban-explore-root");
    const root = personalRoot || exploreRoot;
    if (root) {
      appendWatchlistAction(root);
      if (personalRoot) mountWatchlistPage(root);
      return true;
    }
    return false;
  };
  if (!observe()) {
    const observer = new MutationObserver(() => { if (observe()) observer.disconnect(); });
    observer.observe(document, { childList: true, subtree: true });
  }
})();
