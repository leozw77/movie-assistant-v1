# Poster refresh no-flicker optimization — 2026-08-24

## Baseline

- Source baseline: `chatgpt/frodo-filter-only-20260819`
- Baseline commit: `741cab168a601f63de06353a1cdc616067f061a0`
- Main remains unchanged.

## Root cause

The shell performs a full card re-render for non-paging refreshes. Existing card DOM is removed and `QbDoubanCard.render()` creates a new `<img>` for the same poster URL. Even when Chromium/WebView2 has the resource cached, the newly created image node may need to be decoded/painted again. For poster URLs that use the native fallback path, the refresh also discards the already recovered data-URI image and starts the original-image failure/fallback cycle again.

This produces the visible poster blink during refresh.

## Fix

The shared card renderer keeps a small bounded poster-node cache keyed by subject ID and poster URL.

When the same movie is rendered again with the same poster URL:

1. A fresh card/poster container is still created, so card text and event state remain current.
2. The already loaded media child (`img` or poster fallback node) is moved from the previous poster container into the new one.
3. The actual decoded image node is therefore preserved instead of recreated.
4. A poster that has already been replaced by `doubanShellPosterFallback` is preserved as well.
5. If the poster URL changes, a new image node is created normally.
6. The cache is bounded to avoid retaining poster DOM indefinitely while browsing large libraries.

## Expected effect

- Repeated refresh of the same visible list should no longer make all posters blink.
- Public score, personal rating, comment and text card content can still be rebuilt normally.
- First-time poster loading behavior is unchanged.
- A genuinely changed poster URL still reloads as expected.
