import { render } from "preact";

import { installEnhancedRoot } from "@/shared/runtime/enhanced-document";
import { Stars } from "@/shared/components/common/stars";

import { SubjectSwitcher } from "../search/subject-switcher";

type SearchResult = {
  cast: string;
  facts: string[];
  poster: string;
  score: number | null;
  subjectId: string;
  subjectUrl: string;
  title: string;
  voteCount: number | null;
  year: string;
};

type SearchPageLink = {
  href: string;
  label: string;
};

const cleanText = (value: string | null | undefined): string =>
  String(value ?? "")
    .replace(/\s+/gu, " ")
    .trim();

const pageUrl = (doc: Document): string =>
  doc.baseURI || doc.defaultView?.location.href || doc.location?.href || location.href;

const resultNodeFor = (link: HTMLAnchorElement): HTMLElement =>
  link.closest<HTMLElement>(".item-root, .item, .result, .search-result") ??
  link.parentElement?.parentElement ??
  link.parentElement ??
  link;

const resultTitleFor = (link: HTMLAnchorElement, node: HTMLElement): string => {
  const titleNode = node.querySelector<HTMLElement>(
    ".title a, .title, h3 a, h3, h2 a, h2"
  );
  const title = cleanText(titleNode?.textContent || link.textContent);
  if (title) {
    return title;
  }

  const firstLine = (node.innerText || "")
    .split("\n")
    .map(cleanText)
    .find(Boolean);
  return firstLine || "未命名作品";
};

const titleForDisplay = (value: string): string =>
  value
    .replace(/\s*\[(?:可播放|在线|豆瓣推荐)[^\]]*\]\s*$/u, "")
    .replace(/\s*[（(](?:19|20)\d{2}[）)]\s*$/u, "")
    .trim();

const textFromSelectors = (
  node: HTMLElement,
  selectors: string
): string[] =>
  [...node.querySelectorAll<HTMLElement>(selectors)]
    .map((element) => cleanText(element.textContent))
    .filter(Boolean);

const scoreFor = (node: HTMLElement, lines: string[]): number | null => {
  const direct = textFromSelectors(
    node,
    ".rating_nums, .rating_num, .rating-info .rating_nums, [property='v:average']"
  )[0];
  const directScore = Number.parseFloat(direct ?? "");
  if (Number.isFinite(directScore) && directScore >= 0 && directScore <= 10) {
    return directScore;
  }

  const source = lines.join(" ");
  const match = source.match(
    /(?:^|[^\d])((?:10(?:\.0)?|[0-9](?:\.\d)?))\s*(?=\(|人评价|$)/u
  );
  if (!match) {
    return null;
  }

  const scoreText = match[1];
  if (!scoreText) {
    return null;
  }
  const parsed = Number.parseFloat(scoreText);
  return Number.isFinite(parsed) && parsed >= 0 && parsed <= 10 ? parsed : null;
};

const voteCountFor = (node: HTMLElement, lines: string[]): number | null => {
  const direct = textFromSelectors(
    node,
    ".rating_people, .rating-info .pl, [property='v:votes'], [class*='rating-people']"
  ).join(" ");
  const source = `${direct} ${lines.join(" ")}`;
  const match = source.match(/([\d,]+)\s*人评价/u);
  if (!match) {
    return null;
  }

  const countText = match[1];
  if (!countText) {
    return null;
  }
  const parsed = Number.parseInt(countText.replace(/,/gu, ""), 10);
  return Number.isFinite(parsed) ? parsed : null;
};

const yearFor = (title: string, lines: string[]): string => {
  const match = `${title} ${lines.join(" ")}`.match(/[（(]((?:19|20)\d{2})[）)]/u);
  return match?.[1] ?? "";
};

const factsFor = (
  node: HTMLElement,
  lines: string[],
  title: string,
  score: number | null,
  voteCount: number | null,
  year: string
): string[] => {
  const structured = textFromSelectors(
    node,
    ".abstract, .extra, [class*='abstract'], [class*='metadata']"
  );
  const source = [...structured, ...lines];
  const titleText = titleForDisplay(title);
  const scoreText = score === null ? "" : score.toFixed(1);
  const countText = voteCount === null ? "" : String(voteCount);
  const cast = castFor(node);
  const facts: string[] = [];

  for (const value of source) {
    if (cast && value.includes(cast)) {
      continue;
    }
    for (const part of value.split(/[\/|·]/u)) {
      const fact = cleanText(part)
        .replace(title, "")
        .replace(titleText, "")
        .replace(scoreText, "")
        .replace(countText, "")
        .replace(/\(?[\d,]+\s*人评价\)?/gu, "")
        .replace(/\[(?:可播放|在线|豆瓣推荐)[^\]]*\]/gu, "")
        .trim();
      if (
        !fact ||
        fact === year ||
        fact === title ||
        fact === titleText ||
        /^(?:可播放|在线)$/u.test(fact) ||
        /^\d+(?:\.\d+)?$/u.test(fact) ||
        facts.includes(fact)
      ) {
        continue;
      }
      facts.push(fact);
    }
  }

  return facts.slice(0, 8);
};

const castFor = (node: HTMLElement): string =>
  textFromSelectors(
    node,
    ".subject-cast, .cast, .director, [class*='cast'], [class*='director']"
  )[0] ?? "";

const subjectUrlFor = (
  link: HTMLAnchorElement,
  doc: Document
): URL | undefined => {
  const rawHref = link.getAttribute("href")?.trim();
  if (!rawHref) {
    return undefined;
  }

  try {
    const url = new URL(rawHref, pageUrl(doc));
    const subjectId = url.pathname.match(/^\/subject\/(\d+)\/?$/u)?.[1];
    const supportedHost = [
      "movie.douban.com",
      "search.douban.com",
      "www.douban.com",
    ].includes(url.hostname.toLowerCase());
    if (!subjectId || !supportedHost) {
      return undefined;
    }

    return new URL(`/subject/${subjectId}/`, "https://movie.douban.com");
  } catch {
    return undefined;
  }
};

const extractSearchResults = (doc: Document): SearchResult[] => {
  const seen = new Set<string>();
  const results: SearchResult[] = [];
  for (const link of doc.querySelectorAll<HTMLAnchorElement>("a[href]")) {
    const subjectUrl = subjectUrlFor(link, doc);
    if (!subjectUrl) {
      continue;
    }

    const subjectId = subjectUrl.pathname.match(/^\/subject\/(\d+)/u)?.[1] ?? "";
    if (!subjectId || seen.has(subjectId)) {
      continue;
    }

    const node = resultNodeFor(link);
    const lines = (node.innerText || "")
      .split("\n")
      .map(cleanText)
      .filter(Boolean);
    const rawTitle = resultTitleFor(link, node);
    const title = titleForDisplay(rawTitle);
    const score = scoreFor(node, lines);
    const voteCount = voteCountFor(node, lines);
    const year = yearFor(rawTitle, lines);
    const image = node.querySelector<HTMLImageElement>("img");
    const poster =
      image?.currentSrc ||
      image?.src ||
      image?.dataset.src ||
      image?.getAttribute("data-original") ||
      "";

    seen.add(subjectId);
    results.push({
      cast: castFor(node),
      facts: factsFor(node, lines, rawTitle, score, voteCount, year),
      poster,
      score,
      subjectId,
      subjectUrl: subjectUrl.href,
      title,
      voteCount,
      year,
    });
  }
  return results;
};

const searchResultSignature = (results: SearchResult[]): string =>
  results.map((result) => `${result.subjectId}:${result.title}`).join("|");

const extractSearchPageLinks = (doc: Document): SearchPageLink[] =>
  [...doc.querySelectorAll<HTMLAnchorElement>(".paginator a[href], .pagination a[href]")]
    .map((anchor) => ({
      href: new URL(anchor.href, pageUrl(doc)).href,
      label: cleanText(anchor.textContent),
    }))
    .filter((link) => link.label && link.href.includes("search.douban.com"));


const searchQuery = (doc: Document): string => {
  try {
    return new URL(doc.defaultView?.location.href ?? location.href).searchParams
      .get("search_text")
      ?.trim() ?? "";
  } catch {
    return "";
  }
};

const SearchCard = ({ result }: { result: SearchResult }) => (
  <a class="atv-search-page-card" href={result.subjectUrl}>
    <div class="atv-search-page-card-poster">
      {result.poster ? (
        <img alt={result.title} loading="lazy" src={result.poster} />
      ) : (
        <span>暂无海报</span>
      )}
    </div>
    <div class="atv-search-page-card-body">
      <div class="atv-search-page-card-title-row">
        <h2>{result.title}</h2>
      </div>
      <div class="atv-search-page-card-subtitle">
        {result.year ? <span>{result.year}</span> : null}
      </div>
      <div class="atv-search-page-card-rating" aria-label="豆瓣评分">
        {result.score !== null ? (
          <>
            <strong>{result.score.toFixed(1)}</strong>
            <Stars score={result.score} />
          </>
        ) : (
          <span class="atv-search-page-card-rating-empty">暂无评分</span>
        )}
      </div>
      {result.facts.length ? (
        <div class="atv-search-page-card-facts" aria-label="影片信息">
          {result.facts.map((fact) => (
            <span key={fact}>{fact}</span>
          ))}
        </div>
      ) : null}
      {result.cast ? (
        <p class="atv-search-page-card-cast">{result.cast}</p>
      ) : null}
      <span class="atv-search-page-card-action">打开详情 →</span>
    </div>
  </a>
);

const SearchPage = ({
  links,
  query,
  results,
}: {
  links: SearchPageLink[];
  query: string;
  results: SearchResult[];
}) => (
  <div class="atv-search-page">
    <header class="atv-search-page-header">
      <div>
        <p class="atv-search-page-kicker">Douban Plus / Search</p>
        <h1 class="atv-search-page-title">
          {query ? `搜索「${query}」` : "搜索作品"}
        </h1>
      </div>
      <div class="atv-stickynav-subject-switcher qb-global-search-host">
        <SubjectSwitcher />
      </div>
    </header>
    <main class="atv-search-page-content">
      {results.length ? (
        <section class="atv-search-page-results" aria-label="豆瓣搜索结果">
          {results.map((result) => (
            <SearchCard key={result.subjectId} result={result} />
          ))}
        </section>
      ) : (
        <section class="atv-search-page-empty">
          <p class="atv-search-page-section-label">统一搜索</p>
          <h2>没有找到相关作品</h2>
          <p>可以尝试更换关键词，或使用上方同一个搜索框重新提交。</p>
        </section>
      )}
      {links.length ? (
        <nav class="atv-search-page-pagination" aria-label="搜索结果分页">
          {links.map((link) => (
            <a href={link.href} key={link.href}>
              {link.label}
            </a>
          ))}
        </nav>
      ) : null}
    </main>
  </div>
);

const mountSearchPage = (doc: Document = document): void => {
  if (!doc.body || doc.querySelector("#atv-douban-root")) {
    return;
  }

  installEnhancedRoot(doc, (root) => {
    let lastSignature = "";
    let observer: MutationObserver | undefined;
    let stopTimer: number | undefined;
    const syncSearchVisibility = (): void => {
      const host = doc.querySelector<HTMLElement>(".qb-global-search-host");
      if (host) {
        host.dataset.qbGlobalSearchVisible = window.scrollY > 120 ? "true" : "false";
      }
    };

    const renderCurrentPage = (): void => {
      const results = extractSearchResults(doc);
      const signature = searchResultSignature(results);
      if (signature === lastSignature) {
        return;
      }

      lastSignature = signature;
      render(
        <SearchPage
          links={extractSearchPageLinks(doc)}
          query={searchQuery(doc)}
          results={results}
        />,
        root
      );
      syncSearchVisibility();

      if (results.length > 0) {
        observer?.disconnect();
        if (stopTimer !== undefined) {
          window.clearTimeout(stopTimer);
        }
      }
    };

    renderCurrentPage();
    window.addEventListener("scroll", syncSearchVisibility, { passive: true });
    syncSearchVisibility();
    const observeTarget = doc.querySelector("#wrapper") ?? doc.body;
    observer = new MutationObserver(() => renderCurrentPage());
    observer.observe(observeTarget, { childList: true, subtree: true });
    stopTimer = window.setTimeout(() => observer?.disconnect(), 10_000);
  });
};

export { extractSearchResults, mountSearchPage };
