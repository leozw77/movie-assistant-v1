namespace QbPotDoubanAi;

internal sealed record FrodoOptions(
    string BaseUrl,
    string ApiKey,
    string Secret,
    int PageSize,
    TimeSpan RequestTimeout)
{
    public static FrodoOptions CreateDefault()
    {
        var apiKey = Environment.GetEnvironmentVariable("DOUBAN_FRODO_API_KEY");
        var secret = Environment.GetEnvironmentVariable("DOUBAN_FRODO_API_SECRET");
        return new FrodoOptions(
            "https://frodo.douban.com",
            string.IsNullOrWhiteSpace(apiKey) ? "0dad551ec0f84ed02907ff5c42e8ec70" : apiKey.Trim(),
            string.IsNullOrWhiteSpace(secret) ? "bf7dddc7c9cfe6f7" : secret.Trim(),
            20,
            TimeSpan.FromSeconds(10));
    }
}
