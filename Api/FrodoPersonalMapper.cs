using System.Globalization;
using System.Text.Json;

namespace QbPotDoubanAi;

internal static class FrodoPersonalMapper
{
    internal static FrodoPersonalPage Map(JsonElement root, string expectedShellStatus)
    {
        var start = ReadInt(root, "start") ?? 0;
        var count = ReadInt(root, "count") ?? 0;
        var total = ReadInt(root, "total") ?? 0;
        var items = new List<FrodoPersonalItem>();
        var skipped = new List<FrodoPersonalSkip>();
        if (!root.TryGetProperty("interests", out var interests) || interests.ValueKind != JsonValueKind.Array)
            return new FrodoPersonalPage(start, count, total, 0, items, skipped);

        var rawInterestCount = interests.GetArrayLength();
        var index = 0;
        foreach (var interest in interests.EnumerateArray())
        {
            var subjectId = "";
            var frodoStatus = "";
            try
            {
                if (interest.ValueKind != JsonValueKind.Object)
                {
                    skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, "interest-not-object"));
                    continue;
                }

                frodoStatus = ReadString(interest, "status");
                if (!interest.TryGetProperty("subject", out var subject) || subject.ValueKind != JsonValueKind.Object)
                {
                    skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, "missing-subject"));
                    continue;
                }

                subjectId = ReadStringLike(subject, "id");
                if (subjectId.Length == 0 || !subjectId.All(char.IsDigit))
                {
                    skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, "invalid-subject-id"));
                    continue;
                }

                if (frodoStatus.Length == 0)
                {
                    skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, "missing-status"));
                    continue;
                }

                string status;
                try
                {
                    status = DoubanStatusMapper.ToShell(frodoStatus);
                }
                catch (InvalidDataException)
                {
                    skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, $"unknown-status:{CompactReason(frodoStatus)}"));
                    continue;
                }

                if (!string.Equals(status, expectedShellStatus, StringComparison.Ordinal))
                {
                    skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, $"status-mismatch:{CompactReason(frodoStatus)}->{status}"));
                    continue;
                }

                items.Add(MapKnownItem(interest, subject, subjectId, status));
            }
            catch (InvalidDataException ex)
            {
                skipped.Add(new FrodoPersonalSkip(index, subjectId, frodoStatus, $"invalid-data:{CompactReason(ex.Message)}"));
            }
            finally
            {
                index++;
            }
        }

        if (rawInterestCount > 0 && items.Count == 0)
            throw new InvalidDataException("Frodo interests 返回了记录，但没有一条能映射到当前个人页；已交给 DOM fallback。");
        return new FrodoPersonalPage(start, count, total, rawInterestCount, items, skipped);
    }

    private static FrodoPersonalItem MapKnownItem(
        JsonElement interest,
        JsonElement subject,
        string subjectId,
        string status)
    {
        var title = ReadString(subject, "title");
        if (title.Length == 0) title = $"豆瓣条目 {subjectId}";
        var year = ReadStringLike(subject, "year");
        var genres = ReadStringArray(subject, "genres");
        var directors = ReadNameArray(subject, "directors");
        var cast = ReadNameArray(subject, "actors").Take(2).ToList();
        var cardSubtitle = ReadString(subject, "card_subtitle");
        var countries = ReadStringArray(subject, "countries");
        if (countries.Count == 0) countries = CountriesFromCardSubtitle(cardSubtitle, year);
        var subjectUrl = ReadString(subject, "url");
        if (!IsSubjectUrl(subjectUrl, subjectId)) subjectUrl = $"https://movie.douban.com/subject/{subjectId}/";

        var posterUrl = ReadString(subject, "cover_url");
        if (posterUrl.Length == 0 && subject.TryGetProperty("pic", out var pic) && pic.ValueKind == JsonValueKind.Object)
        {
            posterUrl = ReadString(pic, "large");
            if (posterUrl.Length == 0) posterUrl = ReadString(pic, "normal");
        }

        double? score = null;
        int? ratingCount = null;
        if (subject.TryGetProperty("rating", out var publicRating) && publicRating.ValueKind == JsonValueKind.Object)
        {
            score = ReadDouble(publicRating, "value");
            ratingCount = ReadInt(publicRating, "count");
        }

        var contentType = ReadString(subject, "type");
        if (contentType.Length == 0) contentType = ReadString(subject, "subtype");
        contentType = contentType.Equals("tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
        var playable = ReadBool(subject, "is_playable");
        var identity = string.Join(" / ", new[]
        {
            year,
            string.Join(" / ", countries.Take(3))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new FrodoPersonalItem(
            subjectId,
            subjectUrl,
            posterUrl,
            title,
            year,
            identity,
            string.Join(" / ", genres),
            countries,
            genres,
            cast,
            directors.FirstOrDefault() ?? "",
            directors,
            contentType,
            playable,
            score,
            ratingCount,
            ReadMyRating(interest),
            status,
            DoubanStatusMapper.Label(status),
            ReadString(interest, "create_time"),
            ReadString(interest, "comment"),
            cardSubtitle);
    }

    private static int? ReadMyRating(JsonElement interest)
    {
        if (!interest.TryGetProperty("rating", out var rating) || rating.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        double? raw = rating.ValueKind switch
        {
            JsonValueKind.Number when rating.TryGetDouble(out var number) => number,
            JsonValueKind.Object => ReadDouble(rating, "value") ?? ReadDouble(rating, "star_count"),
            _ => null
        };
        if (raw is null || raw <= 0) return null;
        if (raw <= 5) return Math.Clamp((int)Math.Round(raw.Value, MidpointRounding.AwayFromZero), 1, 5);
        if (raw <= 10) return Math.Clamp((int)Math.Round(raw.Value / 2d, MidpointRounding.AwayFromZero), 1, 5);
        return null;
    }

    private static List<string> CountriesFromCardSubtitle(string subtitle, string year)
    {
        if (string.IsNullOrWhiteSpace(subtitle)) return [];
        var parts = subtitle.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return [];
        var index = parts[0].Equals(year, StringComparison.OrdinalIgnoreCase) ||
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? 1
            : 0;
        if (index >= parts.Length) return [];
        var candidate = parts[index];
        if (candidate.Any(char.IsDigit) || candidate.Length > 80) return [];
        return candidate.Split(new[] { ' ', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ReadNameArray(JsonElement owner, string name)
    {
        var result = new List<string>();
        if (!owner.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in array.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.Object ? ReadString(item, "name") : item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
            value = value.Trim();
            if (value.Length > 0 && !result.Contains(value, StringComparer.OrdinalIgnoreCase)) result.Add(value);
        }
        return result;
    }

    private static List<string> ReadStringArray(JsonElement owner, string name)
    {
        var result = new List<string>();
        if (!owner.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = (item.GetString() ?? "").Trim();
            if (value.Length > 0 && !result.Contains(value, StringComparer.OrdinalIgnoreCase)) result.Add(value);
        }
        return result;
    }

    private static string CompactReason(string value)
    {
        var compact = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 160 ? compact : compact[..160];
    }

    private static string ReadString(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    private static string ReadStringLike(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => (value.GetString() ?? "").Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static int? ReadInt(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static double? ReadDouble(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;

    private static bool ReadBool(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number != 0;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = (value.GetString() ?? "").Trim();
            if (bool.TryParse(text, out var boolean)) return boolean;
            return text == "1";
        }
        return false;
    }

    private static bool IsSubjectUrl(string url, string subjectId) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.TrimEnd('/').Equals($"/subject/{subjectId}", StringComparison.Ordinal);
}
