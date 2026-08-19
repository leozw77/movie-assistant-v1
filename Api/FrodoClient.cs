using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed class FrodoClient
{
    private const string AndroidUserAgent = "api-client/1 com.douban.frodo/7.0.1(204) Android/29 product/nitrogen vendor/Xiaomi model/MI MAX 3 rom/miui6 network/wifi platform/mobile nd/1";
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    });
    private static int _schemaLogged;

    private readonly FrodoOptions _options;

    internal FrodoClient(FrodoOptions options)
    {
        _options = options;
    }

    internal async Task<JsonElement> GetInterestsAsync(
        string userId,
        string shellStatus,
        int start,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !userId.All(char.IsDigit))
            throw new InvalidDataException("Frodo 用户 ID 无效。");
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));

        var path = $"/api/v2/user/{userId}/interests";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = FrodoSigner.SignGet(path, timestamp, _options.Secret);
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "movie",
            ["status"] = DoubanStatusMapper.ToFrodo(shellStatus),
            ["start"] = start.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["_ts"] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["_sig"] = signature,
            ["apikey"] = _options.ApiKey,
            ["os_rom"] = "android"
        };
        var queryText = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var url = $"{_options.BaseUrl.TrimEnd('/')}{path}?{queryText}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", AndroidUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var brief = body.Length <= 300 ? body : body[..300];
            throw new HttpRequestException($"Frodo interests 返回 HTTP {(int)response.StatusCode}：{brief}", null, response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("interests", out var interests) ||
                interests.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Frodo interests 响应缺少 interests 数组。");
            LogSchemaOnce(interests);
            return root.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Frodo interests 返回了无效 JSON。", ex);
        }
    }

    private static void LogSchemaOnce(JsonElement interests)
    {
        JsonElement firstInterest = default;
        foreach (var candidate in interests.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object) continue;
            firstInterest = candidate;
            break;
        }
        if (firstInterest.ValueKind != JsonValueKind.Object) return;
        if (Interlocked.CompareExchange(ref _schemaLogged, 1, 0) != 0) return;

        try
        {
            var interestKeys = PropertyNames(firstInterest);
            var subjectKeys = Array.Empty<string>();
            if (firstInterest.TryGetProperty("subject", out var subject) && subject.ValueKind == JsonValueKind.Object)
                subjectKeys = PropertyNames(subject);
            var hasTagLikeFields = interestKeys.Concat(subjectKeys).Any(name => name.Contains("tag", StringComparison.OrdinalIgnoreCase));
            var hasLinewatch = subjectKeys.Contains("has_linewatch", StringComparer.Ordinal);
            var hasActions = subjectKeys.Contains("actions", StringComparer.Ordinal);
            var hasLinewatches = subjectKeys.Contains("linewatches", StringComparer.Ordinal);
            var hasGuessedIsPlayable = subjectKeys.Contains("is_playable", StringComparer.Ordinal);
            DiagnosticLogger.Write(
                $"Frodo schema; InterestKeys={string.Join(',', interestKeys)}; SubjectKeys={string.Join(',', subjectKeys)}; HasTagLikeFields={hasTagLikeFields}; HasLinewatch={hasLinewatch}; HasActions={hasActions}; HasLinewatches={hasLinewatches}; HasIsPlayable={hasGuessedIsPlayable}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Write($"Frodo schema probe failed; Error={ex.Message}");
        }
    }

    private static string[] PropertyNames(JsonElement owner) =>
        owner.EnumerateObject()
            .Select(property => property.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
}
