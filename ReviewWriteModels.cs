using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QbPotDoubanAi;

/// <summary>
/// Explicit mutation semantics. Null is a value; it is no longer overloaded to mean "do not change".
/// </summary>
[JsonConverter(typeof(ReviewFieldActionJsonConverter))]
internal enum ReviewFieldAction
{
    Keep,
    Set,
    Clear
}


internal sealed class ReviewFieldActionJsonConverter : JsonConverter<ReviewFieldAction>
{
    public ReviewFieldActionJsonConverter()
    {
    }

    public override ReviewFieldAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Review field action must be a string.");

        return (reader.GetString() ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "keep" => ReviewFieldAction.Keep,
            "set" => ReviewFieldAction.Set,
            "clear" => ReviewFieldAction.Clear,
            _ => throw new JsonException("Review field action must be keep, set, or clear.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ReviewFieldAction value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ReviewFieldAction.Keep => "keep",
            ReviewFieldAction.Set => "set",
            ReviewFieldAction.Clear => "clear",
            _ => throw new JsonException("Unknown review field action.")
        });
    }
}

internal sealed record DoubanEntryWriteRequestV2(
    string Status,
    ReviewFieldAction RatingAction,
    int? Rating,
    ReviewFieldAction CommentAction,
    string? Comment);

/// <summary>
/// An authoritative snapshot read from Douban's official edit form.
/// Every nullable value has a separate Known flag so "known empty" is not confused with "read failed".
/// </summary>
internal sealed record OfficialReviewSnapshot(
    bool ExistsKnown,
    bool Exists,
    bool StatusKnown,
    string? Status,
    bool RatingKnown,
    int? Rating,
    bool CommentKnown,
    string? Comment,
    bool CapabilitiesKnown,
    IReadOnlyList<string> SupportedStatuses,
    bool CanSetRating,
    bool CanClearRating,
    bool CanEditComment,
    string Source,
    string? Error)
{
    public bool MarkedDateKnown { get; init; }
    public string? MarkedDate { get; init; }
    public string OfficialTitle { get; init; } = string.Empty;
    public string OfficialSubjectId { get; init; } = string.Empty;

    public static OfficialReviewSnapshot Unknown(string source, string error) => new(
        ExistsKnown: false,
        Exists: false,
        StatusKnown: false,
        Status: null,
        RatingKnown: false,
        Rating: null,
        CommentKnown: false,
        Comment: null,
        CapabilitiesKnown: false,
        SupportedStatuses: Array.Empty<string>(),
        CanSetRating: false,
        CanClearRating: false,
        CanEditComment: false,
        Source: source,
        Error: error);
}

/// <summary>
/// Fully resolved final state. The submitter receives this object, not the ambiguous UI mutation request.
/// </summary>
internal sealed record ResolvedReviewTarget(
    string Status,
    int? Rating,
    string Comment,
    bool RatingWasImplicitlyClearedByWish,
    DoubanEntryWriteRequestV2 Intent);

internal enum ReviewWritePhase
{
    ReadingOfficial,
    ResolvingTarget,
    NoChange,
    FillingForm,
    Submitting,
    WaitingForSettlement,
    ReadingBack,
    Verifying,
    UpdatingLocalCache,
    Confirmed,
    Blocked,
    Failed,
    Uncertain
}

internal sealed record ReviewSubmitReceipt(
    bool SubmitEventObserved,
    bool SubmitDefaultPrevented,
    string InitialUrl,
    string? FormAction,
    string? Error,
    object? Diagnostic);

internal sealed record ReviewSettlementResult(
    bool Settled,
    bool TerminalFailure,
    string State,
    int Attempts,
    string? Error,
    object? LastProbe);

internal sealed record ReviewVerificationResult(
    bool Matches,
    IReadOnlyList<string> Mismatches);

internal sealed record ReviewWriteResultV2(
    ReviewWritePhase Phase,
    string Stage,
    bool Settled,
    bool LocalUpdated,
    DoubanEntryWriteRequestV2 Requested,
    ResolvedReviewTarget? Target,
    OfficialReviewSnapshot? Before,
    OfficialReviewSnapshot? Official,
    string? Error,
    object? Diagnostic)
{
    /// <summary>
    /// True only after the official submit settled and the authoritative readback matched.
    /// Local cache ownership is intentionally separate from official confirmation.
    /// </summary>
    public bool Submitted { get; init; }
    public bool NoChange { get; init; }
    public bool Changed { get; init; }
    public bool SubmitEventObserved { get; init; }

    public bool OfficialConfirmed =>
        Phase == ReviewWritePhase.Confirmed &&
        Submitted &&
        Settled &&
        Error is null &&
        Official is not null;

    public static ReviewWriteResultV2 Blocked(
        string stage,
        DoubanEntryWriteRequestV2 request,
        OfficialReviewSnapshot? before,
        string error,
        object? diagnostic = null) => new(
            Phase: ReviewWritePhase.Blocked,
            Stage: stage,
            Settled: false,
            LocalUpdated: false,
            Requested: request,
            Target: null,
            Before: before,
            Official: null,
            Error: error,
            Diagnostic: diagnostic);
}
