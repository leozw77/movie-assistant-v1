"""Static guardrails for the HtmlMediaLibraryForm partial-class split."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]

EXPECTED_MARKERS = {
    "HtmlMediaLibraryForm.Layout.cs": [
        "ConfigureDoubanNavigationContextMenu",
        "RecoverDoubanWebViewsAsync",
    ],
    "HtmlMediaLibraryForm.Startup.cs": [
        "RebuildFrodoPersonalCacheAsync",
        "DispatchAsync",
    ],
    "HtmlMediaLibraryForm.Reviews.cs": [
        "DetailDto",
        "FindOrCreateRecord",
        "ApplyMetadata",
    ],
    "HtmlMediaLibraryForm.WebViews.cs": [
        "EnsureDoubanPlusViewAsync",
        "HandleDoubanSourceWebMessageReceivedAsync",
        "EnsureDoubanSubjectViewAsync",
    ],
    "HtmlMediaLibraryForm.Personal.cs": [
        "PrefetchMissingPublicScoresFromDetailDomAsync",
        "EnsureFrodoPersonalIndexAsync",
        "HandleDoubanShellApplyLocalPersonalFilterAsync",
    ],
    "HtmlMediaLibraryForm.Shell.cs": [
        "PostShellMessage",
        "HandleDoubanPlusWebMessageReceivedAsync",
    ],
    "HtmlMediaLibraryForm.Watchlist.cs": [
        "HandleWatchlistMessageAsync",
        "SaveWatchlistPosterAsync",
        "IsAllowedWatchlistSubjectSource",
    ],
    "HtmlMediaLibraryForm.Navigation.cs": [
        "OpenDoubanPlusDetailAsync",
        "NavigateDoubanPlusToUrl",
        "GetWritableRecord",
    ],
}


def brace_balance(text: str) -> int:
    return text.count("{") - text.count("}")


def main() -> None:
    main_file = (ROOT / "HtmlMediaLibraryForm.cs").read_text(encoding="utf-8")
    assert "internal sealed partial class HtmlMediaLibraryForm" in main_file
    assert "HandleWatchlistMessageAsync" not in main_file
    assert "DispatchAsync" not in main_file

    for filename, markers in EXPECTED_MARKERS.items():
        path = ROOT / filename
        assert path.exists(), f"missing partial file: {filename}"
        text = path.read_text(encoding="utf-8")
        assert "internal sealed partial class HtmlMediaLibraryForm" in text
        assert brace_balance(text) == 0, f"unbalanced braces: {filename}"
        for marker in markers:
            assert marker in text, f"missing {marker} in {filename}"

    print("HtmlMediaLibraryForm partial split validation: PASS")


if __name__ == "__main__":
    main()
