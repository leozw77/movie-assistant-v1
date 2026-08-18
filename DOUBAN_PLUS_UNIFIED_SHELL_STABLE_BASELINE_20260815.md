# Douban Plus unified Shell stable baseline — 2026-08-15

## Baseline

- Baseline: `观影助手-v1.0.1-unified-shell-fix-20260815-020000-win-x64`
- Frozen stable copy: `artifacts/观影助手-v1.0.1-unified-shell-stable-20260815-020000-win-x64`
- EXE SHA256: `8CE6A2B0CCE0BDFBC7B1C8DEBF41BB0C3C81C4A89D6DC8EF96A88B1B6512503A`
- Stable ZIP SHA256: `60FACCB641C740B2939EDF888033B386AB5B4EAFC85779349EA56A470F3FD974`

## Frozen scope

- Unified `DoubanShell + DOM Source WebView + C# JSON message bridge`.
- Explore movie/TV source reading, filters, paging, detail navigation and return.
- Personal movie list source reading, paging and return.
- Direct poster URL first; single-poster fallback only after direct load failure.
- Explore navigation timeout recovery so the Shell can be retried.

## Known boundary before UI work

- This baseline is frozen before the next card-text UI redesign.
- Personal list DOM does not reliably expose the public Douban score; the baseline does not fabricate one.
- Real logged-in WebView2 interaction remains the acceptance boundary and must be manually tested.
- Formal earlier stable releases remain untouched.

All subsequent card UI and personal-field parsing changes are development changes after this baseline.
