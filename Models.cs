using System.Text.Json;

namespace QbPotDoubanAi;

public sealed class AppSettings
{
    public string PreferredBrowser { get; set; } = "Chrome";
    public string VideoDirectory { get; set; } = @"D:\video";
    public double CompletionThreshold { get; set; } = 0.92;
    public int MinimumWatchMinutes { get; set; } = 10;
    public string DeepSeekApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-v4-flash";
}

public sealed class VideoRecord
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "Browser";
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string Genre { get; set; } = "";
    public string Url { get; set; } = "";
    public double DurationSeconds { get; set; }
    public double HighestRatio { get; set; }
    public double WatchedSeconds { get; set; }
    public bool Reminded { get; set; }
    public DateTime? SnoozedUntil { get; set; }
}

public sealed class PersistentState
{
    public Dictionary<string, VideoRecord> Videos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record DoubanPlusOpenTarget(
    string SearchTitle,
    int? Year,
    string ImdbId,
    List<string> Aliases,
    string PreferredStatus,
    string SourceDescription);

public sealed class DoubanHistoryState
{
    public DateTime? LastImportedAt { get; set; }
    public Dictionary<string, DoubanHistoryRecord> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DoubanSessionState
{
    public string ProfileId { get; set; } = "";
    public string LoginState { get; set; } = "unknown";
    public DateTime? LastVerifiedAt { get; set; }
    public string LastError { get; set; } = "";
}

// Online-search choices are deliberately kept out of the user's Douban
// "watched / wish / watching" history.  They only cache details the user has
// explicitly opened from a search result.
public sealed class DoubanSearchCacheState
{
    public Dictionary<string, DoubanHistoryRecord> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record DoubanStatusOption(string Text, bool Selected);

public sealed record DoubanSearchCandidate(
    string SubjectId,
    string SubjectUrl,
    string PosterUrl,
    string VisibleText,
    List<DoubanStatusOption>? StatusOptions = null);

public sealed class DoubanHistoryRecord
{
    public string SubjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string SubjectUrl { get; set; } = "";
    public string Status { get; set; } = "";
    public List<DoubanStatusOption> DoubanStatusOptions { get; set; } = [];
    public bool DoubanStatusCapabilitiesKnown { get; set; }
    public string DoubanStatusCapabilitySource { get; set; } = "";
    public string DoubanStatusCapabilityError { get; set; } = "";
    public int? Rating { get; set; }
    public double? DoubanScore { get; set; }
    public int ScoreFetchAttempts { get; set; }
    public DateTime? ScoreLastAttemptAt { get; set; }
    public string ScoreFetchStatus { get; set; } = "pending";
    public string ScoreLastError { get; set; } = "";
    public bool DetailMetadataFetched { get; set; }
    public string Year { get; set; } = "";
    public string Genres { get; set; } = "";
    public string Directors { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Countries { get; set; } = "";
    public string ImdbId { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime? FullDetailsFetchedAt { get; set; }
    public DateTime? FullDetailsLastAttemptAt { get; set; }
    public string FullDetailsLastError { get; set; } = "";
    public string PosterUrl { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Tags { get; set; } = "";
    public string MarkedDate { get; set; } = "";
    public DateTime ImportedAt { get; set; }
    // A delete is represented as a durable tombstone instead of removing the
    // record.  This prevents a stale history page from resurrecting it during
    // the next mirror/sync pass.
    public bool Tombstoned { get; set; }
    public DateTime? TombstonedAt { get; set; }
    public string TombstoneReason { get; set; } = "";
}

public sealed class MovieIdentity
{
    public string Title { get; set; } = ""; public string OriginalTitle { get; set; } = ""; public int? Year { get; set; }
    public string ImdbId { get; set; } = ""; public string Confidence { get; set; } = "低"; public string Summary { get; set; } = "";
    public string Evidence { get; set; } = ""; public string SourceUrl { get; set; } = "";
    public string CacheVersion { get; set; } = "";
    public string ConfirmationMethod { get; set; } = "";
    public string RecognitionSource { get; set; } = "";
    public string InputFileName { get; set; } = "";
    public List<string> InputAliases { get; set; } = [];
    public int MatchScore { get; set; }
    public string MatchEvidence { get; set; } = "";
}

public sealed class Store
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dir;
    public Store(string? dataDirectory = null)
    {
        _dir = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoubanBrowserReminder")
            : Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(_dir);
    }
    public string DataDirectory => _dir;
    public AppSettings LoadSettings() => Load<AppSettings>("settings.json") ?? new();
    public PersistentState LoadState() => Load<PersistentState>("state.json") ?? new();
    public DoubanSessionState LoadDoubanSession() => Load<DoubanSessionState>("douban-session.json") ?? new();
    public void Save(AppSettings settings, PersistentState state)
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), JsonSerializer.Serialize(settings, JsonOptions));
        File.WriteAllText(Path.Combine(_dir, "state.json"), JsonSerializer.Serialize(state, JsonOptions));
    }
    public void SaveDoubanSession(DoubanSessionState session)
    {
        var path = Path.Combine(_dir, "douban-session.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session, JsonOptions));
        File.Move(temp, path, true);
    }
    public string? LoadCache(string key) { var p = Path.Combine(_dir, "cache", SafeName(key) + ".txt"); return File.Exists(p) ? File.ReadAllText(p) : null; }
    public void SaveCache(string key, string value) { var d = Path.Combine(_dir, "cache"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, SafeName(key) + ".txt"), value); }
    public void DeleteCache(string key)
    {
        var path = Path.Combine(_dir, "cache", SafeName(key) + ".txt");
        if (File.Exists(path)) File.Delete(path);
    }
    private T? Load<T>(string name) { try { var p = Path.Combine(_dir, name); return File.Exists(p) ? JsonSerializer.Deserialize<T>(File.ReadAllText(p)) : default; } catch { return default; } }
    private static string SafeName(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
