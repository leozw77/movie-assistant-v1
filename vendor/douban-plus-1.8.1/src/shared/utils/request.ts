import { GM_xmlhttpRequest } from "$";

const delay = (ms: number): Promise<void> =>
  // oxlint-disable-next-line promise/avoid-new
  new Promise((resolve) => {
    setTimeout(resolve, ms);
  });

const getXhr = () => {
  const g = typeof GM_xmlhttpRequest === "function" ? GM_xmlhttpRequest : undefined;
  return g;
};

const fetchRequest = async (
  method: "GET" | "POST",
  url: string,
  extraHeaders: Record<string, string>,
  data?: string
): Promise<string> => {
  // WebView2 direct injection has no userscript host. Keep the official GM path
  // when available and use a credentialed same-origin fetch as the fallback.
  const headers = { ...extraHeaders };
  delete headers.Referer;
  const response = await fetch(url, {
    body: data,
    credentials: "include",
    headers,
    method,
  });
  if (!response.ok) {
    throw new Error(`[DOUBAN-PLUS] fetch failed: ${response.status} ${url}`);
  }
  return response.text();
};

const gmRequest = (
  method: "GET" | "POST",
  url: string,
  referer?: string,
  extraHeaders?: Record<string, string>,
  data?: string
): Promise<string> => {
  const headers: Record<string, string> = {
    "Content-Type": "application/x-www-form-urlencoded",
    ...extraHeaders,
  };
  if (referer) {
    headers.Referer = referer;
  }
  const xhr = getXhr();
  if (!xhr) {
    return fetchRequest(method, url, headers, data);
  }
  // oxlint-disable-next-line promise/avoid-new
  return new Promise((resolve, reject) => {
    xhr({
      ...(data === undefined ? {} : { data }),
      headers,
      method,
      onerror: () => reject(new Error("GM_xmlhttpRequest failed")),
      onload: (r) => resolve(r.responseText),
      url,
    });
  });
};

const gmPostOnce = (
  url: string,
  data: string,
  referer?: string,
  extraHeaders?: Record<string, string>
): Promise<string> => gmRequest("POST", url, referer, extraHeaders, data);

const RETRY_DELAYS = [300, 800, 2000];

const gmPost = async (
  url: string,
  data: string,
  referer?: string,
  extraHeaders?: Record<string, string>
): Promise<string> => {
  for (let attempt = 0; attempt <= RETRY_DELAYS.length; attempt += 1) {
    try {
      // oxlint-disable-next-line no-await-in-loop
      return await gmPostOnce(url, data, referer, extraHeaders);
    } catch (error) {
      if (attempt < RETRY_DELAYS.length) {
        console.warn(
          "[GM] POST failed (attempt",
          attempt + 1,
          "), retrying in",
          RETRY_DELAYS[attempt] ?? 0,
          "ms —",
          (error as Error).message
        );
        // oxlint-disable-next-line no-await-in-loop
        await delay(RETRY_DELAYS[attempt] ?? 0);
      } else {
        throw error;
      }
    }
  }
  throw new Error("[GM] POST failed after all retries");
};

const gmGet = (url: string, referer?: string): Promise<string> =>
  gmRequest("GET", url, referer);

const getCk = (): string =>
  (document.cookie.match(/\bck=(?<ck>[^;]+)/u) || [])[1] || "";

export { getCk, gmGet, gmPost };
