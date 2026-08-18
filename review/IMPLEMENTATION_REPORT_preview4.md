# 0.9.0-preview.4 implementation report

## Scope

- Repair the deleted-entry tombstone deadlock without allowing background synchronization to silently revive deleted records.
- Reject Douban verification badges and UI assets as cast avatars, then retry through the official cast/person profile flow.
- Retain the preview.2 status capability/write-settlement changes and the preview.3 frontend status-selection behavior.
- Keep diagnostic and crash logging available for later AI modification.

## Deleted-entry lifecycle

`saveDoubanEntry` now explicitly allows a tombstoned record to enter the official save flow. The tombstone remains set while the request is pending. It is cleared only by `ApplyConfirmedDoubanEntry`, after the official form has submitted and the readback matches. Delete still rejects an already tombstoned record, and `MergeLiveRecord` still refuses to revive tombstones during background history reads.

## Avatar validation

`NormalizeAvatarUrl` rejects `ic_verify`, `/f/shire/`, and `personage-default` assets in addition to the prior default-image patterns. Cast extraction and enrichment treat any URL that normalizes to empty as missing. Merge logic replaces a rejected current avatar with a valid enriched avatar. Cached full-cast data containing a rejected non-empty URL is refreshed from the official cast page.

## Validation

- Source/static boundary checks: 45 passed.
- Chromium DOM/UI fixtures: 26 passed.
- Windows launcher-format checks: 3 passed.
- This environment cannot execute the Windows GUI or perform a normal `dotnet publish`; Windows real-machine verification remains required.
