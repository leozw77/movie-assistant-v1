import { render } from "preact";

import { SubjectSwitcher } from "../search/subject-switcher";

const SEARCH_HOST_SELECTOR = "[data-qb-douban-plus-search-host]";

const mountIntoHosts = (doc: Document): void => {
  for (const host of doc.querySelectorAll<HTMLElement>(SEARCH_HOST_SELECTOR)) {
    if (host.dataset.qbDoubanPlusSearchMounted === "true") {
      continue;
    }

    const wrapper = doc.createElement("div");
    wrapper.className = "atv-stickynav-subject-switcher qb-global-search-switcher";
    host.append(wrapper);
    render(<SubjectSwitcher />, wrapper);
    host.dataset.qbDoubanPlusSearchMounted = "true";
  }
};

const mountPersonalSearch = (doc: Document = document): void => {
  if (!doc.body) {
    return;
  }

  mountIntoHosts(doc);
  const syncVisibility = (): void => {
    const visible = window.scrollY > 120;
    for (const host of doc.querySelectorAll<HTMLElement>(SEARCH_HOST_SELECTOR)) {
      host.dataset.qbGlobalSearchVisible = visible ? "true" : "false";
    }
  };
  const observer = new MutationObserver(() => {
    mountIntoHosts(doc);
    syncVisibility();
  });
  observer.observe(doc.body, { childList: true, subtree: true });
  window.addEventListener("scroll", syncVisibility, { passive: true });
  syncVisibility();
  window.setTimeout(() => observer.disconnect(), 15_000);
};

export { mountPersonalSearch };
