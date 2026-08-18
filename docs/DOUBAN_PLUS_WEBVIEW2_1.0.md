# v1.0 Douban Plus WebView2 direct-injection trial

This document describes the isolated v1.0 implementation. The formal v0.9.0
BuildFix12 R11 release remains unchanged.

## Implemented path

1. The existing `WebAssets/MediaLibrary` library view remains the list, filter,
   cache, and state owner.
2. A movie card sends `openDoubanPlusDetail` through the existing local bridge.
3. `HtmlMediaLibraryForm` shows a third, visible WebView2 in the same window and
   navigates it to the validated `https://movie.douban.com/subject/{id}/` URL.
4. The official Douban Plus 1.8.1 generated bundle is injected at
   `document-created` with local SystemJS runtime assets.
5. The host-level return button hides the plugin view and restores the original
   local WebView2 without reloading it, preserving list position and filters.
6. Bridge or initialization failures fall back to the retained local detail
   renderer.

The existing Douban Plus page mounts cover the subject detail, all comments,
all reviews, all photos, celebrities, and personage routes. Same-window
navigation is limited to HTTPS `douban.com` hosts; new-window requests are
redirected into the same visible WebView2.

## Source and build provenance

- Source review copy: `D:\chatgpt\_source-review\douban-plus-master-20260810\douban-plus-master`
- Version: `1.8.1`
- Commit: `0da5072d4636ae85f572ff1e673e27ad85d8d4dd`
- Independent build copy: `vendor/douban-plus-1.8.1`
- Only the direct-injection request fallback and Windows Vite path resolution
  are changed in the build copy; page components, styles, extractors, and
  Cookie behavior remain from the official source.

## Validation boundary

Static checks, `node --check`, the existing review pipeline, and a successful
.NET build are complete. They do not prove that a real Douban page, login
Cookie, cross-domain request, comment/review page, photo page, cast page, or
personage page rendered successfully. Those require a real WebView2 run with
network access and, where applicable, an existing Douban login session.
