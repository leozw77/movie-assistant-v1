# BuildFix11 Acceptance Checklist

## Source implementation

- [x] Shared `CoreWebView2Environment` and Douban profile.
- [x] Long-lived `DetailWebView` and `WorkerWebView` initialized at startup.
- [x] Detail metadata routed only through DetailWebView.
- [x] Review reads/writes, full cast and avatar fallback routed through WorkerWebView.
- [x] Single-consumer priority queue with save preemption.
- [x] Review transaction semantics unchanged; delete remains disabled.
- [x] Home cast keeps official card order and repeated person/different-role cards.
- [x] Full cast is scoped to `li.celebrity` cards.
- [x] Full-cast DOM uses complete-state stable sampling.
- [x] Unified avatar validity filtering and persistent person-avatar cache.
- [x] Incremental avatar updates include SubjectId and RequestId.
- [x] Stale-result UI protection for avatar, full-cast and save results.
- [x] Cast/FullCast parser versions invalidate only cast caches.
- [x] Detail/Worker role, job and timing diagnostics.
- [x] Isolated controller recovery is scheduled after WebView2 process failure.

## Automated checks available in this environment

- [x] Review protocol: 6/6.
- [x] Embedded Douban JavaScript syntax: 16/16.
- [x] Review/source gate: 92/92.
- [x] BuildFix11 specialized gate: 29/29.
- [x] Launcher encoding/line-ending gate: 3/3.

## Windows runtime checks

- [ ] .NET 8 Windows build.
- [ ] WinForms/WebView2 startup.
- [ ] Shared-login verification in both WebViews.
- [ ] Real Douban detail/full-cast/avatar regression.
- [ ] Real official review save/readback regression.

These Windows items were not run because the current environment has no `dotnet` SDK and is not Windows.
