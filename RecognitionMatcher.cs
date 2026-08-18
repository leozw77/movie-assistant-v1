using System.Text.RegularExpressions;

namespace QbPotDoubanAi;

public static class RecognitionCache
{
    public const string AutoBindingVersion = "auto-binding-v3";
    public const string ConfirmedBindingVersion = "confirmed-binding-v1";
}

public sealed class RecognitionEvaluation
{
    public int Score { get; init; }
    public bool HasTitleMatch { get; init; }
    public bool IsStrongMatch { get; init; }
    public string MatchedAlias { get; init; } = "";
    public string Evidence { get; init; } = "";
}

public static class RecognitionMatcher
{
    private static readonly HashSet<string> EnglishStopWords = new(StringComparer.OrdinalIgnoreCase) { "a", "an", "the", "film", "movie" };

    public static RecognitionEvaluation Evaluate(MovieTitleParts input, string? candidateTitle, string? candidateOriginalTitle, int? candidateYear)
    {
        var candidateText = string.Join(" ", new[] { candidateTitle, candidateOriginalTitle }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var best = input.Aliases.Select(alias => (Alias: alias, Score: ScoreAlias(alias, candidateText)))
            .OrderByDescending(x => x.Score).FirstOrDefault();
        var hasMatch = best.Score >= 50;
        var yearConflict = input.Year is not null && candidateYear is not null && input.Year != candidateYear;
        var score = yearConflict ? Math.Max(0, best.Score - 40) : best.Score;
        var strong = hasMatch && score >= 80 && !yearConflict;
        var evidence = hasMatch
            ? $"候选标题与别名“{best.Alias}”相似度 {score} 分" + (yearConflict ? $"；年份冲突：输入 {input.Year}，候选 {candidateYear}" : "")
            : "候选标题与所有输入别名均无明显重合；年份和 IMDb 格式不能单独通过";
        return new RecognitionEvaluation { Score = score, HasTitleMatch = hasMatch, IsStrongMatch = strong, MatchedAlias = hasMatch ? best.Alias : "", Evidence = evidence };
    }

    public static bool IsStrongMatch(MovieTitleParts input, string? candidateTitle, string? candidateOriginalTitle, int? candidateYear) =>
        Evaluate(input, candidateTitle, candidateOriginalTitle, candidateYear).IsStrongMatch;

    private static int ScoreAlias(string alias, string candidateText)
    {
        var aliasNormalized = MovieTitle.Normalize(alias);
        var candidateNormalized = MovieTitle.Normalize(candidateText);
        if (string.IsNullOrWhiteSpace(aliasNormalized) || string.IsNullOrWhiteSpace(candidateNormalized)) return 0;
        if (candidateNormalized == aliasNormalized || candidateNormalized.Contains(aliasNormalized, StringComparison.Ordinal) || aliasNormalized.Contains(candidateNormalized, StringComparison.Ordinal)) return 100;
        if (ContainsChinese(alias) && candidateNormalized.Contains(aliasNormalized, StringComparison.Ordinal)) return 95;

        var aliasTokens = Tokens(alias).Where(x => !EnglishStopWords.Contains(x)).ToArray();
        var candidateTokens = Tokens(candidateText).Where(x => !EnglishStopWords.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aliasTokens.Length >= 2)
        {
            var shared = aliasTokens.Count(candidateTokens.Contains);
            if (shared == aliasTokens.Length) return 90;
            if (shared * 4 >= aliasTokens.Length * 3) return 75;
        }
        else if (aliasTokens.Length == 1 && aliasTokens[0].Length >= 4 && candidateTokens.Contains(aliasTokens[0]))
        {
            return 65;
        }
        return 0;
    }

    private static string[] Tokens(string value) => Regex.Matches(value ?? "", @"[\p{L}\p{N}]+").Select(x => x.Value.ToLowerInvariant()).ToArray();
    private static bool ContainsChinese(string value) => value.Any(ch => ch is >= '\u3400' and <= '\u9fff');
}
