using System;
using System.Collections.Generic;
using System.Linq;
namespace QbPotDoubanAi;

internal static class ReviewTargetResolver
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "wish", "do", "collect"
    };

    public static ResolvedReviewTarget Resolve(
        OfficialReviewSnapshot official,
        DoubanEntryWriteRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(official);
        ArgumentNullException.ThrowIfNull(request);

        var status = NormalizeStatus(request.Status);
        if (!AllowedStatuses.Contains(status))
        {
            throw new ReviewWriteBlockedException("请选择想看、在看或看过状态。");
        }

        if (!official.ExistsKnown || !official.StatusKnown)
        {
            throw new ReviewWriteBlockedException("豆瓣官方当前状态未读取完整，禁止提交。");
        }

        if (official.CapabilitiesKnown &&
            !official.SupportedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            throw new ReviewWriteBlockedException($"豆瓣官方编辑表单不支持目标状态：{status}。");
        }

        ValidateRequestShape(request);

        bool implicitWishClear = false;
        int? rating;
        if (status.Equals("wish", StringComparison.OrdinalIgnoreCase))
        {
            if (request.RatingAction == ReviewFieldAction.Set)
            {
                throw new ReviewWriteBlockedException("想看状态不能设置评分。");
            }

            // Douban's final wish state must have no rating. This is a final-state rule,
            // not an assumption that a click succeeded; readback must still verify null.
            rating = null;
            implicitWishClear = official.RatingKnown && official.Rating is not null;
        }
        else
        {
            rating = request.RatingAction switch
            {
                ReviewFieldAction.Keep when official.RatingKnown => official.Rating,
                ReviewFieldAction.Keep => throw new ReviewWriteBlockedException("豆瓣官方评分未读取完整，无法安全保持原评分。"),
                ReviewFieldAction.Set => request.Rating,
                ReviewFieldAction.Clear => null,
                _ => throw new ReviewWriteBlockedException("评分操作无效。")
            };
        }

        string comment = request.CommentAction switch
        {
            ReviewFieldAction.Keep when official.CommentKnown => NormalizeComment(official.Comment),
            ReviewFieldAction.Keep => throw new ReviewWriteBlockedException("豆瓣官方短评未读取完整，无法安全保持原短评。"),
            ReviewFieldAction.Set => NormalizeComment(request.Comment),
            ReviewFieldAction.Clear => string.Empty,
            _ => throw new ReviewWriteBlockedException("短评操作无效。")
        };

        if (comment.Length > 330)
        {
            throw new ReviewWriteBlockedException("短评不能超过 330 字。");
        }

        return new ResolvedReviewTarget(status, rating, comment, implicitWishClear, request);
    }

    public static bool IsNoChange(OfficialReviewSnapshot official, ResolvedReviewTarget target)
    {
        return official.ExistsKnown && official.Exists &&
               official.StatusKnown && string.Equals(NormalizeStatus(official.Status), target.Status, StringComparison.Ordinal) &&
               official.RatingKnown && official.Rating == target.Rating &&
               official.CommentKnown && string.Equals(NormalizeComment(official.Comment), target.Comment, StringComparison.Ordinal);
    }

    private static void ValidateRequestShape(DoubanEntryWriteRequestV2 request)
    {
        if (request.RatingAction == ReviewFieldAction.Set &&
            (request.Rating is < 1 or > 5 || request.Rating is null))
        {
            throw new ReviewWriteBlockedException("设置评分时必须提供 1 到 5 星。");
        }

        if (request.RatingAction != ReviewFieldAction.Set && request.Rating is not null)
        {
            throw new ReviewWriteBlockedException("只有 Set 评分操作可以携带评分值。");
        }

        if (request.CommentAction == ReviewFieldAction.Set && NormalizeComment(request.Comment).Length > 330)
        {
            throw new ReviewWriteBlockedException("短评不能超过 330 字。");
        }

        if (request.CommentAction != ReviewFieldAction.Set && request.Comment is not null)
        {
            throw new ReviewWriteBlockedException("只有 Set 短评操作可以携带短评内容。");
        }
    }

    internal static string NormalizeStatus(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    internal static string NormalizeComment(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}

internal sealed class ReviewWriteBlockedException : Exception
{
    public ReviewWriteBlockedException(string message) : base(message)
    {
    }
}
