(() => {
  "use strict";

  const STYLE_ID = "qb-douban-card-style";
  const cssText = __QB_DOUBAN_CARD_CSS__;
  const POSTER_CACHE_LIMIT = 256;
  const posterNodeCache = new Map();

  const addStyle = () => {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = cssText;
    const target = document.head || document.documentElement || document.body;
    if (target) target.append(style);
    else setTimeout(addStyle, 0);
  };

  const value = raw => String(raw ?? "").replace(/\s+/gu, " ").trim();
  const normalizeScore = raw => value(raw).replace(/^豆瓣\s*/u, "").replace(/^我的\s*/u, "");
  const appendClass = (specific, shared) => [specific, shared].filter(Boolean).join(" ");

  const makeNode = (tagName, className, textContent = "") => {
    const node = document.createElement(tagName);
    if (className) node.className = className;
    if (textContent) node.textContent = textContent;
    return node;
  };

  const makeChip = chip => {
    const node = makeNode("span", appendClass(chip.className, "qb-media-card-chip"), chip.text);
    if (chip.accent) node.classList.add("qb-media-card-chip-accent");
    return node;
  };

  const normalizeModel = input => {
    const model = input || {};
    return {
      title: value(model.title) || "未命名影片",
      posterUrl: value(model.posterUrl || model.poster),
      identity: value(model.identity),
      genre: value(model.genre || model.genres),
      context: value(model.context || model.subtitle),
      infoRows: Array.isArray(model.infoRows)
        ? model.infoRows.map(row => ({ label: value(row?.label), value: value(row?.value) })).filter(row => row.label && row.value)
        : [],
      chips: Array.isArray(model.chips) ? model.chips.filter(chip => value(chip?.text)) : [],
      score: normalizeScore(model.score),
      myRating: value(model.myRating),
      comment: value(model.comment),
      subjectId: value(model.subjectId),
      subjectUrl: value(model.subjectUrl)
    };
  };

  const rememberPosterNode = (subjectId, posterUrl, poster) => {
    if (!subjectId || !poster) return;
    posterNodeCache.delete(subjectId);
    posterNodeCache.set(subjectId, { posterUrl, poster });
    while (posterNodeCache.size > POSTER_CACHE_LIMIT) {
      const oldest = posterNodeCache.keys().next().value;
      if (!oldest) break;
      posterNodeCache.delete(oldest);
    }
  };

  const restorePosterMedia = (model, poster) => {
    if (!model.subjectId) return false;
    const cached = posterNodeCache.get(model.subjectId);
    if (!cached || cached.posterUrl !== model.posterUrl || !cached.poster || cached.poster.isConnected) return false;
    const media = cached.poster.querySelector(":scope > img, :scope > .qb-media-card-poster-fallback");
    if (!media) return false;
    if (media.tagName === "IMG") {
      media.alt = `${model.title}海报`;
      poster.classList.remove("qb-media-card-poster-placeholder");
    } else {
      poster.classList.add("qb-media-card-poster-placeholder");
    }
    poster.append(media);
    return true;
  };

  const render = options => {
    addStyle();
    const settings = options || {};
    const model = normalizeModel(settings.model);
    const card = makeNode("article", appendClass(settings.cardClass, "qb-media-card"));
    if (model.subjectId) card.dataset.subjectId = model.subjectId;
    if (model.subjectUrl) card.dataset.subjectUrl = model.subjectUrl;
    if (settings.tabIndex !== false) card.tabIndex = settings.tabIndex ?? 0;

    const posterTag = settings.posterTag || "div";
    const poster = makeNode(posterTag, appendClass(settings.posterClass, "qb-media-card-poster"));
    if (model.subjectId) poster.dataset.subjectId = model.subjectId;
    if (model.subjectUrl) poster.dataset.subjectUrl = model.subjectUrl;
    if (model.posterUrl) poster.dataset.posterUrl = model.posterUrl;
    if (settings.posterHref) poster.href = settings.posterHref;
    if (settings.posterAttributes) Object.entries(settings.posterAttributes).forEach(([key, item]) => poster.setAttribute(key, item));
    if (typeof settings.onPosterOpen === "function") poster.addEventListener("click", settings.onPosterOpen);

    const posterMediaRestored = restorePosterMedia(model, poster);
    if (!posterMediaRestored && model.posterUrl) {
      const image = makeNode("img");
      image.src = model.posterUrl;
      image.alt = `${model.title}海报`;
      image.loading = "lazy";
      image.addEventListener("error", () => {
        if (typeof settings.onPosterError === "function") {
          settings.onPosterError({
            subjectId: model.subjectId,
            posterUrl: model.posterUrl,
            title: model.title
          });
        }
        image.remove();
        const fallback = makeNode("span", "qb-media-card-poster-fallback", settings.posterFallback || "暂无海报");
        poster.append(fallback);
        poster.classList.add("qb-media-card-poster-placeholder");
      }, { once: true });
      poster.append(image);
    } else if (!posterMediaRestored) {
      poster.append(makeNode("span", "qb-media-card-poster-fallback", settings.posterFallback || "暂无海报"));
      poster.classList.add("qb-media-card-poster-placeholder");
    }
    rememberPosterNode(model.subjectId, model.posterUrl, poster);

    let comment = null;
    if (model.comment) {
      comment = makeNode("button", "qb-media-card-comment", "短评");
      comment.type = "button";
      comment.setAttribute("aria-label", "查看短评");
      const popover = makeNode("span", "qb-media-card-comment-popover", model.comment);
      popover.setAttribute("role", "tooltip");
      comment.append(popover);
      comment.addEventListener("click", event => event.stopPropagation());
      comment.addEventListener("keydown", event => {
        if (event.key === "Enter" || event.key === " ") event.stopPropagation();
      });
      poster.append(comment);
    }
    if (model.myRating) poster.append(makeNode("strong", "qb-media-card-my-rating", model.myRating));
    if (model.score) poster.append(makeNode("strong", "qb-media-card-score", model.score));

    const body = makeNode("div", appendClass(settings.bodyClass, "qb-media-card-body"));
    const titleTag = settings.titleTag || "h2";
    const title = makeNode(titleTag, appendClass(settings.titleClass, "qb-media-card-title"), model.title);
    if (settings.titleHref) title.href = settings.titleHref;
    if (settings.titleAttributes) Object.entries(settings.titleAttributes).forEach(([key, item]) => title.setAttribute(key, item));
    body.append(title);
    if (model.identity) {
      const identity = makeNode("div", appendClass(settings.identityClass, "qb-media-card-identity"));
      if (model.identity) identity.append(document.createTextNode(model.identity));
      body.append(identity);
    }
    if (model.infoRows.length) {
      const info = makeNode("div", "qb-media-card-info");
      model.infoRows.forEach(row => {
        const rowNode = makeNode("div", "qb-media-card-info-row");
        rowNode.append(makeNode("div", "qb-media-card-info-label", row.label));
        rowNode.append(makeNode("div", "qb-media-card-info-value", row.value));
        info.append(rowNode);
      });
      body.append(info);
    } else if (model.context) {
      body.append(makeNode("p", appendClass(settings.contextClass, "qb-media-card-context"), model.context));
    }

    const footer = makeNode("div", appendClass(settings.metaClass, "qb-media-card-footer"));
    model.chips.forEach(chip => footer.append(makeChip(chip)));
    if (model.chips.length) body.append(footer);
    if (settings.action) {
      settings.action.classList.add("qb-media-card-action");
      body.append(settings.action);
    }
    card.append(poster, body);
    if (typeof settings.onOpen === "function") card.addEventListener("click", settings.onOpen);
    if (typeof settings.onKeyDown === "function") card.addEventListener("keydown", settings.onKeyDown);
    return card;
  };

  window.QbDoubanCard = Object.freeze({ normalizeModel, render });
  addStyle();
})();
