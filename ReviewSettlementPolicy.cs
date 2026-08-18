using System;
namespace QbPotDoubanAi;

internal sealed record ReviewSettlementProbe(
    string Url,
    string ReadyState,
    bool NavigationStarted,
    bool NavigationCompleted,
    bool OfficialFormVisible,
    bool Captcha,
    bool LoginPage,
    OfficialReviewSnapshot? InlineReadback,
    object? Diagnostic);

/// <summary>
/// Stateful settlement evaluator. It requires two consecutive stable samples and never treats
/// elapsed time alone as success. The caller may poll, but timeout produces Uncertain, not Confirmed.
/// </summary>
internal sealed class ReviewSettlementPolicy
{
    private int _stableSamples;
    private string _lastSignature = string.Empty;

    public ReviewSettlementDecision Observe(
        ReviewSettlementProbe probe,
        ResolvedReviewTarget target)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(target);

        if (probe.Captcha)
            return Terminal("captcha", "豆瓣要求验证码，提交无法确认。");
        if (probe.LoginPage)
            return Terminal("login", "豆瓣登录状态已失效，提交无法确认。");

        var readbackMatches = probe.InlineReadback is not null &&
                              ReviewWriteVerifier.Verify(target, probe.InlineReadback).Matches;
        var navigationSettled = probe.NavigationStarted &&
                                probe.NavigationCompleted &&
                                string.Equals(probe.ReadyState, "complete", StringComparison.OrdinalIgnoreCase) &&
                                !probe.OfficialFormVisible;

        var state = readbackMatches ? "official-readback-matches"
            : navigationSettled ? "navigation-complete-form-closed"
            : "pending";
        var signature = string.Join('|',
            state,
            probe.Url,
            probe.ReadyState,
            probe.OfficialFormVisible,
            probe.InlineReadback?.Status,
            probe.InlineReadback?.Rating,
            probe.InlineReadback?.Comment);

        if (state != "pending" && string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            _stableSamples++;
        else
            _stableSamples = state == "pending" ? 0 : 1;

        _lastSignature = signature;

        return new ReviewSettlementDecision(
            Settled: _stableSamples >= 2,
            TerminalFailure: false,
            State: state,
            StableSamples: _stableSamples,
            Error: null,
            Probe: probe);
    }

    public ReviewSettlementDecision Timeout(ReviewSettlementProbe? lastProbe) => new(
        Settled: false,
        TerminalFailure: false,
        State: "timeout-uncertain",
        StableSamples: _stableSamples,
        Error: "豆瓣提交在限定探测次数内未形成稳定结算信号；结果标记为不确定，本地不写入目标值。",
        Probe: lastProbe);

    private ReviewSettlementDecision Terminal(string state, string error) => new(
        Settled: false,
        TerminalFailure: true,
        State: state,
        StableSamples: _stableSamples,
        Error: error,
        Probe: null);
}

internal sealed record ReviewSettlementDecision(
    bool Settled,
    bool TerminalFailure,
    string State,
    int StableSamples,
    string? Error,
    ReviewSettlementProbe? Probe);
