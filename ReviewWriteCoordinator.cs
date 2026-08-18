using System;
using System.Threading;
using System.Threading.Tasks;

namespace QbPotDoubanAi;

internal interface IDoubanOfficialReviewGateway
{
    Task<OfficialReviewSnapshot> ReadOfficialAsync(
        string subjectId,
        string subjectUrl,
        CancellationToken cancellationToken);

    Task<ReviewSubmitReceipt> SubmitTargetAsync(
        string subjectId,
        string subjectUrl,
        ResolvedReviewTarget target,
        CancellationToken cancellationToken);

    Task<ReviewSettlementResult> WaitForSettlementAsync(
        string subjectId,
        string subjectUrl,
        ResolvedReviewTarget target,
        ReviewSubmitReceipt receipt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Four-stage pipeline: intent -> official submit -> official readback -> local cache.
/// No click result, confirm dialog, URL change or fixed delay can independently produce success.
///
/// Rating clear has one additional server-confirmed path:
/// - target wish: submit the official wish form once and verify the server readback has no rating;
/// - target do/collect while a rating exists: first complete and verify an official wish transaction,
///   then submit and verify the final target state.
/// The hidden rating input is never written directly.
/// </summary>
internal sealed class ReviewWriteCoordinator
{
    private readonly IDoubanOfficialReviewGateway _gateway;
    private readonly IOfficialReviewCacheWriter _cache;

    public ReviewWriteCoordinator(
        IDoubanOfficialReviewGateway gateway,
        IOfficialReviewCacheWriter cache)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<ReviewWriteResultV2> SaveAsync(
        string subjectId,
        string subjectUrl,
        DoubanEntryWriteRequestV2 request,
        CancellationToken cancellationToken = default,
        OfficialReviewSnapshot? freshOfficialBefore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectUrl);
        ArgumentNullException.ThrowIfNull(request);

        OfficialReviewSnapshot? before = null;
        ResolvedReviewTarget? target = null;
        OfficialReviewSnapshot? intermediateOfficial = null;
        object? intermediateDiagnostic = null;
        var preflightLocalUpdated = false;

        try
        {
            before = freshOfficialBefore ?? await _gateway.ReadOfficialAsync(subjectId, subjectUrl, cancellationToken)
                .ConfigureAwait(true);

            if (before.Error is not null || !before.ExistsKnown || !before.StatusKnown)
            {
                return ReviewWriteResultV2.Blocked(
                    "official-read",
                    request,
                    before,
                    before.Error ?? "豆瓣官方当前评价未读取完整。");
            }

            // The web is authoritative even before this save. Refresh the local cache from the
            // successful complete read rather than preserving stale local values.
            if (before.ExistsKnown && (!before.Exists ||
                (before.StatusKnown && before.RatingKnown && before.CommentKnown)))
            {
                preflightLocalUpdated = await _cache.OverwriteFromOfficialAsync(subjectId, subjectUrl, before, cancellationToken)
                    .ConfigureAwait(true);
            }

            target = ReviewTargetResolver.Resolve(before, request);

            if (ReviewTargetResolver.IsNoChange(before, target))
            {
                return new ReviewWriteResultV2(
                    Phase: ReviewWritePhase.NoChange,
                    Stage: "no-change",
                    Settled: true,
                    LocalUpdated: preflightLocalUpdated,
                    Requested: request,
                    Target: target,
                    Before: before,
                    Official: before,
                    Error: null,
                    Diagnostic: new { reason = "official-already-matches-target" })
                {
                    Submitted = false,
                    NoChange = true,
                    Changed = false,
                    SubmitEventObserved = false
                };
            }

            // Douban does not expose a reliable visible "clear rating" control in the current form.
            // When the final state is do/collect and an existing rating must be removed, perform a
            // separate official wish transaction first. The server, not local JavaScript, clears it.
            if (NeedsIntermediateWishClear(before, target))
            {
                var wishTarget = new ResolvedReviewTarget(
                    Status: "wish",
                    Rating: null,
                    Comment: target.Comment,
                    RatingWasImplicitlyClearedByWish: true,
                    Intent: request);

                var wishTransaction = await ExecuteTransactionAsync(
                        subjectId,
                        subjectUrl,
                        wishTarget,
                        cancellationToken)
                    .ConfigureAwait(true);

                intermediateOfficial = wishTransaction.Official;
                intermediateDiagnostic = new
                {
                    purpose = "server-confirmed-rating-clear",
                    target = wishTarget,
                    transaction = wishTransaction.Diagnostic
                };

                if (!wishTransaction.Success)
                {
                    return new ReviewWriteResultV2(
                        Phase: wishTransaction.Phase,
                        Stage: $"clear-rating-{wishTransaction.Stage}",
                        Settled: wishTransaction.Settled,
                        LocalUpdated: false,
                        Requested: request,
                        Target: target,
                        Before: before,
                        Official: wishTransaction.Official,
                        Error: wishTransaction.Error,
                        Diagnostic: intermediateDiagnostic)
                    {
                        Submitted = wishTransaction.Submitted,
                        Changed = wishTransaction.Submitted,
                        SubmitEventObserved = wishTransaction.Submitted
                    };
                }

                // If the final transaction later fails, this complete intermediate official snapshot
                // is still authoritative and must be available to the outer cache owner.
                if (intermediateOfficial is not null)
                {
                    await _cache.OverwriteFromOfficialAsync(
                            subjectId,
                            subjectUrl,
                            intermediateOfficial,
                            cancellationToken)
                        .ConfigureAwait(true);
                }
            }

            var finalTransaction = await ExecuteTransactionAsync(
                    subjectId,
                    subjectUrl,
                    target,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!finalTransaction.Success)
            {
                return new ReviewWriteResultV2(
                    Phase: finalTransaction.Phase,
                    Stage: intermediateOfficial is null
                        ? finalTransaction.Stage
                        : $"final-after-clear-{finalTransaction.Stage}",
                    Settled: finalTransaction.Settled,
                    LocalUpdated: false,
                    Requested: request,
                    Target: target,
                    Before: before,
                    Official: finalTransaction.Official ?? intermediateOfficial,
                    Error: finalTransaction.Error,
                    Diagnostic: new
                    {
                        intermediate = intermediateDiagnostic,
                        final = finalTransaction.Diagnostic
                    })
                {
                    Submitted = finalTransaction.Submitted || intermediateOfficial is not null,
                    Changed = finalTransaction.Submitted || intermediateOfficial is not null,
                    SubmitEventObserved = finalTransaction.Submitted || intermediateOfficial is not null
                };
            }

            var official = finalTransaction.Official
                ?? throw new InvalidOperationException("官方事务成功但缺少回读快照。");

            var localUpdated = await _cache.OverwriteFromOfficialAsync(
                    subjectId,
                    subjectUrl,
                    official,
                    cancellationToken)
                .ConfigureAwait(true);

            return new ReviewWriteResultV2(
                Phase: ReviewWritePhase.Confirmed,
                Stage: intermediateOfficial is null ? "readback" : "readback-after-rating-clear",
                Settled: true,
                LocalUpdated: localUpdated,
                Requested: request,
                Target: target,
                Before: before,
                Official: official,
                Error: null,
                Diagnostic: new
                {
                    intermediate = intermediateDiagnostic,
                    final = finalTransaction.Diagnostic
                })
            {
                Submitted = true,
                NoChange = false,
                Changed = true,
                SubmitEventObserved = true
            };
        }
        catch (ReviewWriteBlockedException ex)
        {
            return ReviewWriteResultV2.Blocked("resolve-target", request, before, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReviewWriteResultV2(
                Phase: ReviewWritePhase.Failed,
                Stage: "exception",
                Settled: false,
                LocalUpdated: false,
                Requested: request,
                Target: target,
                Before: before,
                Official: intermediateOfficial,
                Error: ex.Message,
                Diagnostic: new
                {
                    intermediate = intermediateDiagnostic,
                    exception = ex.ToString()
                });
        }
    }

    private static bool NeedsIntermediateWishClear(
        OfficialReviewSnapshot before,
        ResolvedReviewTarget target) =>
        before.RatingKnown &&
        before.Rating is not null &&
        target.Rating is null &&
        !string.Equals(target.Status, "wish", StringComparison.OrdinalIgnoreCase);

    private async Task<TransactionOutcome> ExecuteTransactionAsync(
        string subjectId,
        string subjectUrl,
        ResolvedReviewTarget target,
        CancellationToken cancellationToken)
    {
        var receipt = await _gateway.SubmitTargetAsync(
                subjectId,
                subjectUrl,
                target,
                cancellationToken)
            .ConfigureAwait(true);

        if (!receipt.SubmitEventObserved || receipt.Error is not null)
        {
            return new TransactionOutcome(
                Success: false,
                Phase: ReviewWritePhase.Failed,
                Stage: "submit",
                Settled: false,
                Submitted: receipt.SubmitEventObserved,
                Official: null,
                Error: receipt.Error ?? "官方表单未观察到 submit 事件。",
                Diagnostic: receipt.Diagnostic);
        }

        var settlement = await _gateway.WaitForSettlementAsync(
                subjectId,
                subjectUrl,
                target,
                receipt,
                cancellationToken)
            .ConfigureAwait(true);

        if (!settlement.Settled)
        {
            return new TransactionOutcome(
                Success: false,
                Phase: settlement.TerminalFailure ? ReviewWritePhase.Failed : ReviewWritePhase.Uncertain,
                Stage: "settlement",
                Settled: false,
                Submitted: true,
                Official: null,
                Error: settlement.Error ?? "豆瓣提交结果尚未确认。",
                Diagnostic: new { receipt, settlement });
        }

        var official = await _gateway.ReadOfficialAsync(subjectId, subjectUrl, cancellationToken)
            .ConfigureAwait(true);
        var verification = ReviewWriteVerifier.Verify(target, official);

        if (!verification.Matches)
        {
            return new TransactionOutcome(
                Success: false,
                Phase: ReviewWritePhase.Uncertain,
                Stage: "readback",
                Settled: true,
                Submitted: true,
                Official: official,
                Error: string.Join("；", verification.Mismatches),
                Diagnostic: new { receipt, settlement, verification });
        }

        return new TransactionOutcome(
            Success: true,
            Phase: ReviewWritePhase.Confirmed,
            Stage: "readback",
            Settled: true,
            Submitted: true,
            Official: official,
            Error: null,
            Diagnostic: new { receipt, settlement, verification });
    }

    private sealed record TransactionOutcome(
        bool Success,
        ReviewWritePhase Phase,
        string Stage,
        bool Settled,
        bool Submitted,
        OfficialReviewSnapshot? Official,
        string? Error,
        object? Diagnostic);
}
