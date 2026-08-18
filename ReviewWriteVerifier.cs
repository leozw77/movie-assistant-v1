using System;
using System.Collections.Generic;
namespace QbPotDoubanAi;

internal static class ReviewWriteVerifier
{
    public static ReviewVerificationResult Verify(
        ResolvedReviewTarget target,
        OfficialReviewSnapshot official)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(official);

        var mismatches = new List<string>();

        if (!official.ExistsKnown)
            mismatches.Add("评价是否存在：官方回读未知");
        else if (!official.Exists)
            mismatches.Add("评价是否存在：官方回读为不存在");

        if (!official.StatusKnown)
            mismatches.Add("状态：官方回读未知");
        else if (!string.Equals(
                     ReviewTargetResolver.NormalizeStatus(official.Status),
                     target.Status,
                     StringComparison.Ordinal))
            mismatches.Add($"状态：目标 {target.Status}，官方 {official.Status ?? "<空>"}");

        if (!official.RatingKnown)
            mismatches.Add("评分：官方回读未知");
        else if (official.Rating != target.Rating)
            mismatches.Add($"评分：目标 {FormatRating(target.Rating)}，官方 {FormatRating(official.Rating)}");

        if (!official.CommentKnown)
            mismatches.Add("短评：官方回读未知");
        else if (!string.Equals(
                     ReviewTargetResolver.NormalizeComment(official.Comment),
                     target.Comment,
                     StringComparison.Ordinal))
            mismatches.Add("短评：官方回读与目标不一致");

        return new ReviewVerificationResult(mismatches.Count == 0, mismatches);
    }

    private static string FormatRating(int? value) => value is null ? "无评分" : $"{value} 星";
}
