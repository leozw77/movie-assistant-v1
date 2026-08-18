using Microsoft.Web.WebView2.Core;

namespace QbPotDoubanAi;

/// <summary>
/// Loads the official Douban Plus 1.8.1 userscript bundle directly into the
/// visible WebView2 document. The userscript runtime is kept local so the
/// WebView2 path does not depend on Tampermonkey, ScriptCat, or a CDN script.
/// </summary>
internal static class DoubanPlusWebView2Script
{
    internal const string Version = "1.8.1";
    internal const string SourceCommit = "0da5072d4636ae85f572ff1e673e27ad85d8d4dd";

    private static readonly Lazy<string> Bundle = new(LoadBundle);
    private static readonly Lazy<string> Card = new(LoadCard);
    private static readonly Lazy<string> PersonalPage = new(LoadPersonalPage);
    private static readonly Lazy<string> ExplorePage = new(LoadExplorePage);
    private static readonly Lazy<string> Shell = new(LoadShell);
    private static readonly Lazy<string> CountryLabels = new(LoadCountryLabels);
    private static readonly Lazy<string> SourceBridge = new(LoadSourceBridge);
    private static readonly Lazy<string> PersonalSourceBridge = new(LoadPersonalSourceBridge);

    internal static async Task InstallAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Bundle.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Card.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(CountryLabels.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PersonalPage.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ExplorePage.Value).ConfigureAwait(true);
    }

    internal static async Task InstallShellAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Card.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Shell.Value).ConfigureAwait(true);
    }

    internal static async Task InstallSourceBridgeAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(CountryLabels.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(SourceBridge.Value).ConfigureAwait(true);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PersonalSourceBridge.Value).ConfigureAwait(true);
    }

    internal static string GetSourceBridgeScript() => string.Join(Environment.NewLine, CountryLabels.Value, SourceBridge.Value);
    internal static string GetPersonalSourceBridgeScript() => string.Join(Environment.NewLine, CountryLabels.Value, PersonalSourceBridge.Value);

    internal static string GetShellDocument() => """
<!doctype html>
<html lang="zh-CN">
<head><meta charset="utf-8"><title>Douban Plus</title></head>
<body>
  <main id="qb-douban-shell-root" class="qb-shell">
    <header class="qb-shell-header">
      <div class="qb-shell-brand">
        <h1 class="qb-shell-title">Douban Plus</h1>
        <p class="qb-shell-subtitle">统一影视选择与维护界面</p>
      </div>
      <nav class="qb-shell-nav" aria-label="主导航">
        <button type="button" data-douban-personal-status="collect" aria-current="false">个人影片</button>
        <button type="button" class="qb-active" data-douban-content-type="movie" aria-current="page">探索电影</button>
        <button type="button" data-douban-content-type="tv" aria-current="false">探索电视剧</button>
        <button type="button" data-douban-watchlist aria-current="false">我的待看</button>
      </nav>
      <form id="qb-douban-shell-search" class="qb-shell-search" role="search">
        <label class="qb-shell-search-label" for="qb-douban-shell-search-input">豆瓣影视搜索</label>
        <input id="qb-douban-shell-search-input" class="qb-shell-search-input" type="search" maxlength="160" autocomplete="off" placeholder="搜索豆瓣影视" />
        <button class="qb-shell-search-submit" type="submit" aria-label="搜索豆瓣影视">搜索</button>
      </form>
      <button id="qb-douban-shell-login" class="qb-shell-login" type="button">豆瓣登录</button>
    </header>
    <section class="qb-shell-main">
      <div class="qb-shell-toolbar">
        <div><h2 id="qb-douban-shell-heading" class="qb-shell-heading">探索电影</h2><p id="qb-douban-shell-description" class="qb-shell-description">内容来自豆瓣真实 Explore 页面；筛选、分页和卡片都由统一界面承载。</p></div>
        <div id="qb-douban-shell-status" class="qb-shell-status">正在连接豆瓣…</div>
      </div>
      <section id="qb-douban-shell-filters" class="qb-shell-filters" aria-label="豆瓣原生筛选"></section>
      <section id="qb-douban-shell-grid" class="qb-shell-grid" aria-live="polite"><div class="qb-shell-loading">正在读取电影卡片…</div></section>
      <section id="qb-douban-shell-paging" class="qb-shell-paging" aria-label="分页"></section>
    </section>
  </main>
</body>
</html>
""";

    private static string LoadCard()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        var script = ReadRequired(Path.Combine(directory, "douban-card.js"));
        var css = ReadRequired(Path.Combine(directory, "douban-card.css"));
        const string placeholder = "__QB_DOUBAN_CARD_CSS__";
        var encodedCss = System.Text.Json.JsonSerializer.Serialize(css);
        if (!script.Contains(placeholder, StringComparison.Ordinal))
            throw new InvalidDataException("Douban card renderer does not contain its CSS placeholder.");
        return script.Replace(placeholder, encodedCss, StringComparison.Ordinal);
    }

    private static string LoadBundle()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        var system = ReadRequired(Path.Combine(directory, "system.min.js"));
        var namedRegister = ReadRequired(Path.Combine(directory, "named-register.min.js"));
        var userscript = ReadRequired(Path.Combine(directory, "douban-plus.user.js"));
        const string generatedImport = "System.import(\"./___monkey.entry.js\", \"./\");";
        var generatedImportIndex = userscript.LastIndexOf(generatedImport, StringComparison.Ordinal);
        if (generatedImportIndex < 0)
            throw new InvalidDataException("Douban Plus bundle does not contain its generated entry import.");

        // WebView2 document-created runs before the page has necessarily created
        // document.head. SystemJS loads the generated entry through a script tag,
        // so starting System.import immediately can fail before the DOM exists.
        var delayedImport = """
(() => {
  const state = window.__qbDoubanPlusProbe;
  const start = () => {
    if (state) {
      state.importStartedAt = Date.now();
      state.importReadyState = document.readyState;
    }
    try {
      const pending = System.import("./___monkey.entry.js", "./");
      if (state) {
        state.importStarted = true;
        Promise.resolve(pending).then(
          () => {
            state.importResolved = true;
            state.importResolvedAt = Date.now();
          },
          error => {
            state.importRejected = true;
            state.importError = String(error?.stack || error);
            state.importRejectedAt = Date.now();
          }
        );
      }
    } catch (error) {
      if (state) {
        state.importRejected = true;
        state.importError = String(error?.stack || error);
        state.importRejectedAt = Date.now();
      }
    }
  };

  const waitForHead = () => {
    if (document.head) {
      queueMicrotask(start);
      return;
    }
    setTimeout(waitForHead, 0);
  };
  waitForHead();
})();
""";
        userscript = userscript.Remove(generatedImportIndex, generatedImport.Length).Insert(generatedImportIndex, delayedImport);

        const string gmBridge = """
(() => {
  const state = window.__qbDoubanPlusProbe;
  const pending = new Map();
  let sequence = 0;
  const complete = event => {
    const message = event?.data;
    if (!message || message.type !== "doubanPlusGmResponse") return;
    const details = pending.get(message.id);
    if (!details) return;
    pending.delete(message.id);
    if (message.ok) {
      details.onload?.({
        responseText: message.responseText || "",
        status: message.status || 0,
        statusText: message.statusText || ""
      });
    } else {
      details.onerror?.({
        error: message.error || "GM_xmlhttpRequest failed",
        status: message.status || 0
      });
    }
  };
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener("message", complete);
  }
  const isSameOriginDoubanRequest = url => {
    try {
      const target = new URL(url, location.href);
      return target.protocol === "https:" &&
        target.hostname === "movie.douban.com" &&
        target.origin === location.origin;
    } catch {
      return false;
    }
  };
  const performSameOriginDoubanRequest = (id, details) => {
    const controller = new AbortController();
    const headers = { ...(details?.headers || {}) };
    delete headers.Referer;
    const method = String(details?.method || "GET").toUpperCase();
    const request = {
      credentials: "include",
      headers,
      method,
      signal: controller.signal
    };
    if (method !== "GET" && method !== "HEAD") request.body = details?.data ?? undefined;
    fetch(details?.url || "", request).then(async response => {
      const responseText = await response.text();
      pending.delete(id);
      details?.onload?.({
        responseText,
        status: response.status,
        statusText: response.statusText || ""
      });
    }).catch(error => {
      pending.delete(id);
      if (error?.name === "AbortError") return;
      details?.onerror?.({ error: String(error) });
    });
    return { abort: () => { controller.abort(); pending.delete(id); } };
  };
  window.GM_xmlhttpRequest = details => {
    details = details || {};
    const id = `dp-gm-${Date.now()}-${++sequence}`;
    if (isSameOriginDoubanRequest(details.url)) {
      pending.set(id, details);
      if (state) state.gmRequests.push(`${details.method || "GET"} ${details.url || ""} [same-origin-fetch]`);
      return performSameOriginDoubanRequest(id, details);
    }
    pending.set(id, details || {});
    if (state) state.gmRequests.push(`${details?.method || "GET"} ${details?.url || ""}`);
    try {
      window.chrome?.webview?.postMessage({
        type: "doubanPlusGmRequest",
        id,
        method: details?.method || "GET",
        url: details?.url || "",
        headers: details?.headers || {},
        data: details?.data ?? null
      });
    } catch (error) {
      pending.delete(id);
      details?.onerror?.({ error: String(error) });
    }
    return { abort: () => pending.delete(id) };
  };
})();
""";

        const string styleBridge = """
(() => {
  if (typeof window.GM_addStyle === "function") return;
  window.GM_addStyle = css => {
    const append = () => {
      const target = document.head || document.documentElement || document.body;
      if (!target) {
        setTimeout(append, 0);
        return;
      }
      const style = document.createElement("style");
      style.textContent = String(css ?? "");
      target.appendChild(style);
    };
    append();
  };
})();
""";

        // The official userscript declares SystemJS through @require. Direct
        // WebView2 injection has no userscript manager to process that header,
        // so reproduce only that runtime bootstrap locally before the unchanged
        // official generated bundle.
        return string.Join(
            Environment.NewLine,
            "(() => {",
            "  const state = window.__qbDoubanPlusProbe = { startedAt: Date.now(), warnings: [], errors: [], rejections: [], gmRequests: [], topFrame: window.top === window, href: location.href, readyState: document.readyState, systemLoaded: false, namedRegisterLoaded: false, importStarted: false, importResolved: false, importRejected: false };",
            "  const capture = (target, key) => { const original = target[key]; target[key] = (...args) => { try { state[key].push(args.map(String).join(' ')); } catch {} if (typeof original === 'function') original.apply(target, args); }; };",
            "  capture(console, 'warn'); capture(console, 'error');",
            "  window.addEventListener('error', event => state.errors.push(String(event.error?.stack || event.message || 'window error')));",
            "  window.addEventListener('unhandledrejection', event => state.rejections.push(String(event.reason?.stack || event.reason || 'unhandled rejection')));",
            "  const markReady = () => { state.domContentLoadedAt = Date.now(); state.readyStateAtDomContentLoaded = document.readyState; };",
            "  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', markReady, { once: true }); else markReady();",
            "})();",
            system,
            namedRegister,
            "if (window.__qbDoubanPlusProbe) { window.__qbDoubanPlusProbe.systemLoaded = typeof System !== 'undefined' && !!System; window.__qbDoubanPlusProbe.namedRegisterLoaded = typeof System !== 'undefined' && typeof System.register === 'function'; }",
            "if (typeof System !== \"undefined\" && System.constructor) System = new System.constructor();",
            styleBridge,
            gmBridge,
            userscript);
    }

    private static string LoadPersonalPage()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        var script = ReadRequired(Path.Combine(directory, "douban-personal-page.js"));
        var css = ReadRequired(Path.Combine(directory, "douban-personal-page.css"));
        const string placeholder = "__QB_DOUBAN_PERSONAL_CSS__";
        var encodedCss = System.Text.Json.JsonSerializer.Serialize(css);
        if (!script.Contains(placeholder, StringComparison.Ordinal))
            throw new InvalidDataException("Douban personal page adapter does not contain its CSS placeholder.");
        return script.Replace(placeholder, encodedCss, StringComparison.Ordinal);
    }

    private static string LoadShell()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        var script = ReadRequired(Path.Combine(directory, "douban-shell.js"));
        var css = ReadRequired(Path.Combine(directory, "douban-shell.css"));
        const string placeholder = "__QB_DOUBAN_SHELL_CSS__";
        var encodedCss = System.Text.Json.JsonSerializer.Serialize(css);
        if (!script.Contains(placeholder, StringComparison.Ordinal))
            throw new InvalidDataException("Douban Shell does not contain its CSS placeholder.");
        return script.Replace(placeholder, encodedCss, StringComparison.Ordinal);
    }

    private static string LoadSourceBridge()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        return ReadRequired(Path.Combine(directory, "douban-source-bridge.js"));
    }

    private static string LoadCountryLabels()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        return ReadRequired(Path.Combine(directory, "douban-country-labels.js"));
    }

    private static string LoadPersonalSourceBridge()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        return ReadRequired(Path.Combine(directory, "douban-personal-source-bridge.js"));
    }

    private static string LoadExplorePage()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "WebAssets", "DoubanPlus");
        var script = ReadRequired(Path.Combine(directory, "douban-explore-page.js"));
        var css = ReadRequired(Path.Combine(directory, "douban-explore-page.css"));
        const string placeholder = "__QB_DOUBAN_EXPLORE_CSS__";
        var encodedCss = System.Text.Json.JsonSerializer.Serialize(css);
        if (!script.Contains(placeholder, StringComparison.Ordinal))
            throw new InvalidDataException("Douban explore page adapter does not contain its CSS placeholder.");
        return script.Replace(placeholder, encodedCss, StringComparison.Ordinal);
    }

    private static string ReadRequired(string path)
    {
        return DoubanPlusAssetStore.ReadText(path, "Douban Plus WebView2 asset is missing or empty.");
    }
}
