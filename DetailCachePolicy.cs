namespace QbPotDoubanAi;

internal static class DetailCachePolicy
{
    internal static readonly TimeSpan BasicMetadataTtl = TimeSpan.FromHours(24);

    internal static bool HasFreshBasicMetadata(DoubanHistoryRecord record, DateTime now) =>
        DoubanMediaParser.IsValidDoubanTitle(record.Title) &&
        IsFresh(record.FullDetailsFetchedAt, BasicMetadataTtl, now);

    internal static bool NeedsMetadataRefresh(DoubanHistoryRecord record, DateTime now) =>
        !HasFreshBasicMetadata(record, now);

    private static bool IsFresh(DateTime? fetchedAt, TimeSpan ttl, DateTime now) =>
        fetchedAt is { } value && now >= value && now - value <= ttl;
}
