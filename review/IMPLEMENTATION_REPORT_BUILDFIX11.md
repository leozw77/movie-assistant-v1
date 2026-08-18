# BuildFix11 R1 Implementation Report

## Result

BuildFix11 was implemented on top of BuildFix10 R1. The active HTML library now owns two persistent Douban WebView2 controllers using the same environment and profile. Detail navigation is isolated from all worker navigation, and the existing web-authoritative review transaction remains intact.

## Main implementation

- Added `WorkerJobQueue.cs` with priority order: save, official review, full cast, avatar enrichment, background list/search.
- Added `PersonAvatarCache.cs` with a 90-day TTL and validated-only writes.
- Split `HtmlMediaLibraryForm` into Detail/Worker connectors and preheated both at startup.
- Started metadata and official review reads concurrently in the front end.
- Added SubjectId/RequestId stale-result checks and incremental `castAvatarUpdated` messages.
- Replaced person-level home-cast deduplication with card-order extraction.
- Replaced full-cast ancestor scoring with card-scoped `li.celebrity` parsing.
- Added complete-state stable sampling using card and avatar signatures.
- Added parser-version migration that preserves review and base metadata.
- Added role/job/timing/failure-recovery diagnostics.
- Added fixtures and BuildFix11-specific validation.

## Review protocol preservation

No direct POST, fetch-based write, cookie export or private write endpoint was added. Review save still uses the official form, a single `requestSubmit`, settlement observation, authoritative official readback and field-by-field confirmation before local update. NoChange remains separate. Delete remains disabled.

## Validation

- `tests/review-protocol.test.js`: 6/6 passed.
- `tests/validate_embedded_scripts.py`: 16/16 passed.
- `tests/validate_review_pipeline.py`: 92/92 passed.
- `tests/validate_buildfix11.py`: 29/29 passed.
- `scripts/Validate-Launcher.py`: 3/3 passed.

## Environment boundary

`dotnet` is not installed in the current environment, and the host is not Windows. Therefore no Windows compilation, EXE publication, WinForms launch, WebView2 runtime test, real Douban navigation or real review submission was performed. The package is a complete source delivery, not a verified Windows binary delivery.
