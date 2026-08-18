using System.Text.Json;

namespace QbPotDoubanAi;

/// <summary>
/// Deterministic release gate for the preview.6 review write pipeline.
/// It deliberately excludes machine-dependent browser/WebView2 checks.
/// </summary>
internal static class ReviewPipelineSelfTest
{
    public static string Run()
    {
        var lines = new List<string>();
        var passed = 0;
        var total = 0;

        void Check(string name, bool ok)
        {
            total++;
            if (ok) passed++;
            lines.Add($"{(ok ? "通过" : "失败")}：{name}");
        }

        Check(
            "评价字段动作顺序为 Keep、Set、Clear",
            Enum.GetValues<ReviewFieldAction>().SequenceEqual(
                [ReviewFieldAction.Keep, ReviewFieldAction.Set, ReviewFieldAction.Clear]));

        var jsonOptions = new JsonSerializerOptions();
        jsonOptions.Converters.Add(new ReviewFieldActionJsonConverter());
        var serializedSet = JsonSerializer.Serialize(ReviewFieldAction.Set, jsonOptions);
        var deserializedClear = JsonSerializer.Deserialize<ReviewFieldAction>("\"clear\"", jsonOptions);
        Check(
            "三态协议 JSON 使用小写 keep/set/clear",
            serializedSet == "\"set\"" && deserializedClear == ReviewFieldAction.Clear);

        var official = new OfficialReviewSnapshot(
            ExistsKnown: true,
            Exists: true,
            StatusKnown: true,
            Status: "collect",
            RatingKnown: true,
            Rating: 5,
            CommentKnown: true,
            Comment: "网页短评",
            CapabilitiesKnown: true,
            SupportedStatuses: ["wish", "do", "collect"],
            CanSetRating: true,
            CanClearRating: true,
            CanEditComment: true,
            Source: "review-self-test",
            Error: null);

        var keepIntent = new DoubanEntryWriteRequestV2(
            "do",
            ReviewFieldAction.Keep,
            null,
            ReviewFieldAction.Keep,
            null);
        var keepTarget = ReviewTargetResolver.Resolve(official, keepIntent);
        Check(
            "只改状态时保留网页最新评分和短评",
            keepTarget.Status == "do" && keepTarget.Rating == 5 && keepTarget.Comment == "网页短评");

        var clearIntent = new DoubanEntryWriteRequestV2(
            "collect",
            ReviewFieldAction.Clear,
            null,
            ReviewFieldAction.Clear,
            null);
        var clearTarget = ReviewTargetResolver.Resolve(official, clearIntent);
        Check(
            "评分和短评可明确清除",
            clearTarget.Rating is null && clearTarget.Comment == string.Empty);

        var setIntent = new DoubanEntryWriteRequestV2(
            "collect",
            ReviewFieldAction.Set,
            4,
            ReviewFieldAction.Set,
            "新短评");
        var setTarget = ReviewTargetResolver.Resolve(official, setIntent);
        Check(
            "评分和短评可明确设置",
            setTarget.Rating == 4 && setTarget.Comment == "新短评");

        var wishRatingBlocked = false;
        try
        {
            ReviewTargetResolver.Resolve(
                official,
                new DoubanEntryWriteRequestV2(
                    "wish",
                    ReviewFieldAction.Set,
                    5,
                    ReviewFieldAction.Keep,
                    null));
        }
        catch (ReviewWriteBlockedException)
        {
            wishRatingBlocked = true;
        }
        Check("想看状态禁止设置评分", wishRatingBlocked);

        var unknownBlocked = false;
        try
        {
            ReviewTargetResolver.Resolve(
                OfficialReviewSnapshot.Unknown("review-self-test", "unknown"),
                keepIntent);
        }
        catch (ReviewWriteBlockedException)
        {
            unknownBlocked = true;
        }
        Check("官方状态读取不完整时阻止写入", unknownBlocked);

        var noChangeTarget = ReviewTargetResolver.Resolve(
            official,
            new DoubanEntryWriteRequestV2(
                "collect",
                ReviewFieldAction.Keep,
                null,
                ReviewFieldAction.Keep,
                null));
        Check("目标与网页一致时识别为无变化", ReviewTargetResolver.IsNoChange(official, noChangeTarget));

        var noChangeResult = new ReviewWriteResultV2(ReviewWritePhase.NoChange, "no-change", true, true, noChangeTarget.Intent, noChangeTarget, official, official, null, null)
        { NoChange = true, Changed = false, Submitted = false, SubmitEventObserved = false };
        Check("NoChange 不伪装成真实提交成功", noChangeResult.NoChange && !noChangeResult.Submitted && !noChangeResult.OfficialConfirmed);

        Check("官方回读完全匹配时验证成功", ReviewWriteVerifier.Verify(noChangeTarget, official).Matches);

        var mismatchOfficial = official with { Rating = 3 };
        var mismatch = ReviewWriteVerifier.Verify(noChangeTarget, mismatchOfficial);
        Check("官方回读不一致时验证失败并返回差异", !mismatch.Matches && mismatch.Mismatches.Count > 0);

        var settlementTarget = noChangeTarget;
        var settlementPolicy = new ReviewSettlementPolicy();
        var settlementProbe = new ReviewSettlementProbe(
            Url: "https://movie.douban.com/subject/1/",
            ReadyState: "complete",
            NavigationStarted: true,
            NavigationCompleted: true,
            OfficialFormVisible: false,
            Captcha: false,
            LoginPage: false,
            InlineReadback: official,
            Diagnostic: null);
        var firstSettlement = settlementPolicy.Observe(settlementProbe, settlementTarget);
        var secondSettlement = settlementPolicy.Observe(settlementProbe, settlementTarget);
        Check(
            "提交结算要求连续两次稳定采样",
            !firstSettlement.Settled && secondSettlement.Settled && secondSettlement.StableSamples == 2);

        Check(
            "结算超时标记为不确定而非成功",
            new ReviewSettlementPolicy().Timeout(null) is { Settled: false, TerminalFailure: false, State: "timeout-uncertain" });

        var submitScript = DoubanOfficialFormScripts.BuildSubmitScript("1", setTarget);
        Check(
            "官方提交脚本使用 requestSubmit 且不直接 fetch/cookie",
            submitScript.Contains("requestSubmit", StringComparison.Ordinal) &&
            !submitScript.Contains("fetch(", StringComparison.OrdinalIgnoreCase) &&
            !submitScript.Contains("document.cookie", StringComparison.OrdinalIgnoreCase));

        var clearSubmitScript = DoubanOfficialFormScripts.BuildSubmitScript(
            "1",
            new ResolvedReviewTarget(
                "collect",
                null,
                "网页短评",
                false,
                new DoubanEntryWriteRequestV2("collect", ReviewFieldAction.Clear, null, ReviewFieldAction.Keep, null)));
        Check(
            "目标想看时允许服务器结算后清除评分",
            clearSubmitScript.Contains("wish-server-submit", StringComparison.Ordinal) &&
            clearSubmitScript.Contains("wishServerClearPending", StringComparison.Ordinal) &&
            !clearSubmitScript.Contains("ratingHidden.value =", StringComparison.Ordinal));

        Check(
            "在看或看过清分要求先完成独立想看事务",
            clearSubmitScript.Contains("requiresWishClearTransaction", StringComparison.Ordinal) &&
            clearSubmitScript.Contains("清除评分需要先完成想看状态的官方结算", StringComparison.Ordinal) &&
            !clearSubmitScript.Contains("wish-roundtrip", StringComparison.Ordinal));

        var confirmedDeferredResult = new ReviewWriteResultV2(
            Phase: ReviewWritePhase.Confirmed,
            Stage: "readback",
            Settled: true,
            LocalUpdated: false,
            Requested: noChangeTarget.Intent,
            Target: noChangeTarget,
            Before: official,
            Official: official,
            Error: null,
            Diagnostic: new { cacheUpdate = "deferred" })
        {
            Submitted = true,
            Changed = true,
            SubmitEventObserved = true
        };
        Check(
            "官方确认结果不依赖连接器层的延迟缓存标记",
            confirmedDeferredResult.OfficialConfirmed && !confirmedDeferredResult.LocalUpdated);

        Check(
            "删除 v2 已开放且旧 delete 别名仍禁止",
            HtmlMediaLibraryForm.IsAllowedOperation("saveDoubanEntry") &&
            HtmlMediaLibraryForm.IsAllowedOperation("deleteEntry") &&
            !HtmlMediaLibraryForm.IsAllowedOperation("delete"));

        lines.Insert(0, $"评价管线专项自检：{passed}/{total} 项通过");
        return string.Join(Environment.NewLine, lines);
    }
}
