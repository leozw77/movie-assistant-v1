namespace QbPotDoubanAi;

internal sealed record FrodoPersonalSkip(
    int Index,
    string SubjectId,
    string FrodoStatus,
    string Reason);

internal sealed record FrodoPersonalPage(
    int Start,
    int Count,
    int Total,
    int RawCount,
    IReadOnlyList<FrodoPersonalItem> Items,
    IReadOnlyList<FrodoPersonalSkip> Skipped);

internal sealed record FrodoPersonalItem(
    string SubjectId,
    string SubjectUrl,
    string PosterUrl,
    string Title,
    string Year,
    string Identity,
    string Genre,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Cast,
    string Director,
    IReadOnlyList<string> Directors,
    string ContentType,
    double? Score,
    int? RatingCount,
    int? MyRating,
    string Status,
    string StatusLabel,
    string MarkedDate,
    string Comment,
    string Intro);
