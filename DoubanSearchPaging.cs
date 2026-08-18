namespace QbPotDoubanAi;

internal static class DoubanSearchPaging
{
    private const int SearchPageSize = 15;

    internal static int StartForPage(int pageIndex) => Math.Max(0, pageIndex) * SearchPageSize;
}
