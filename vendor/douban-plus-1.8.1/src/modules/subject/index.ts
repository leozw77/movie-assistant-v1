import type { PageMount } from "@/shared/runtime/page-mount";

import { mountSubject as mountSubjectPage } from "./runtime/mount";
import { mountPersonalSearch } from "./runtime/personal-search-mount";
import { mountSearchPage } from "./runtime/search-page-mount";

const subjectPage: PageMount = {
  matches: (location) =>
    location.hostname === "movie.douban.com" &&
    /^\/subject\/[^/]+\/?$/u.test(location.pathname),
  mount: mountSubjectPage,
};

const personalSearchPage: PageMount = {
  matches: (location) =>
    location.hostname === "movie.douban.com" &&
    (/^\/people\/\d+\/(?:collect|wish|do)\/?$/u.test(location.pathname) ||
      /^\/(?:explore|tv)\/?$/u.test(location.pathname)),
  mount: mountPersonalSearch,
};

const searchPage: PageMount = {
  matches: (location) =>
    location.hostname === "search.douban.com" &&
    /^\/movie\/subject_search\/?$/u.test(location.pathname),
  mount: mountSearchPage,
};

export { mountSubject } from "./runtime/mount";
export {
  mountPersonalSearch,
  mountSearchPage,
  personalSearchPage,
  searchPage,
  subjectPage,
};
