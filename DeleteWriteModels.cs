namespace QbPotDoubanAi;

internal enum DeleteWritePhase
{
    ReadingOfficial,
    NoChange,
    Preparing,
    Submitting,
    WaitingForSettlement,
    ReadingBack,
    VerifyingLists,
    Confirmed,
    Blocked,
    Failed,
    Uncertain
}

internal sealed record DeleteHistoryCheck(
    string Status,
    bool Ready,
    bool Contains,
    int PagesScanned,
    bool HasMore,
    string Scope,
    string Error = "");

internal sealed record DeleteEntryResult(
    DeleteWritePhase Phase,
    string Stage,
    bool Settled,
    bool Submitted,
    bool NoChange,
    bool OfficialConfirmed,
    bool LocalUpdated,
    OfficialReviewSnapshot? Before,
    OfficialReviewSnapshot? Official,
    IReadOnlyList<DeleteHistoryCheck> ListChecks,
    string? Error,
    object? Diagnostic,
    string Route = "SubjectDetail")
{
    internal static DeleteEntryResult Blocked(string stage, OfficialReviewSnapshot? before, string error, object? diagnostic = null, string route = "SubjectDetail") =>
        new(DeleteWritePhase.Blocked, stage, false, false, false, false, false, before, null,
            Array.Empty<DeleteHistoryCheck>(), error, diagnostic, route);

    internal static DeleteEntryResult Failed(string stage, bool submitted, OfficialReviewSnapshot? before, string error, object? diagnostic = null, string route = "SubjectDetail") =>
        new(DeleteWritePhase.Failed, stage, false, submitted, false, false, false, before, null,
            Array.Empty<DeleteHistoryCheck>(), error, diagnostic, route);
}
