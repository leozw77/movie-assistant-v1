using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace QbPotDoubanAi;

// The former cache-backed AI review/question flow was retired. This service is
// intentionally limited to the existing filename-recognition and API test
// capabilities until the new live Douban-page AI pipeline is implemented.
public sealed class DeepSeekService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task TestAsync(AppSettings settings) => _ = await CompleteAsync(settings, "只回复：连接成功", false, 30);

    public async Task<MovieIdentity> ResolveMovieFromFileNameAsync(AppSettings settings, string fileName)
    {
        var prompt = $$"""
你是影视文件名识别助手。仅根据下面这个 PT/BT 影视文件名，识别对应作品：
{{fileName}}

输出严格 JSON：
{"title":"常用中文片名","originalTitle":"原始片名","year":2024,"imdbId":"tt1234567","confidence":"高/中/低","summary":"用于观后提问的剧情与人物摘要","evidence":"你从文件名中识别到的关键依据"}

规则：
1. 去掉分辨率、片源、编码、音轨、压制组、季集编号等发布信息。
2. imdbId 必须是 tt 加数字。只有你对具体作品有可靠把握时才填写；不确定就返回空字符串，严禁编造。
3. 电影填写电影信息；剧集文件识别剧名和所属年份，summary 可概括剧集主要设定。
4. summary 使用中文，最多1200字，包含主要人物、核心冲突和关键剧情，允许剧透；不确定的细节不要写。
5. confidence 反映仅凭文件名识别的可靠程度。只输出 JSON。
""";
        var json = await CompleteAsync(settings, prompt, true, 1800, 0.1);
        var identity = JsonSerializer.Deserialize<MovieIdentity>(json, JsonOptions) ?? throw new InvalidOperationException("影片识别结果为空。");
        if (string.IsNullOrWhiteSpace(identity.Title)) throw new InvalidOperationException("DeepSeek 未能识别影片名称。");
        if (!string.IsNullOrWhiteSpace(identity.ImdbId) && !System.Text.RegularExpressions.Regex.IsMatch(identity.ImdbId, @"^tt\d{5,10}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            identity.ImdbId = "";
        return identity;
    }

    private async Task<string> CompleteAsync(AppSettings settings, string prompt, bool json, int maxTokens, double temperature = 0.7)
    {
        if (string.IsNullOrWhiteSpace(settings.DeepSeekApiKey)) throw new InvalidOperationException("请先在设置中填写 DeepSeek API Key。");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeepSeekApiKey.Trim());
        var body = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(settings.Model) ? "deepseek-v4-flash" : settings.Model.Trim(),
            ["messages"] = new[] { new { role = "system", content = "你是严谨的中文电影助手。" }, new { role = "user", content = prompt } },
            ["thinking"] = new { type = "disabled" }, ["temperature"] = temperature, ["max_tokens"] = maxTokens
        };
        if (json) body["response_format"] = new { type = "json_object" };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(req);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                using var err = JsonDocument.Parse(raw);
                var msg = err.RootElement.GetProperty("error").GetProperty("message").GetString();
                throw new InvalidOperationException("DeepSeek：" + msg);
            }
            catch (JsonException) { throw new InvalidOperationException($"DeepSeek 请求失败（{(int)response.StatusCode}）。"); }
        }
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("DeepSeek 返回了空内容。");
    }
}
