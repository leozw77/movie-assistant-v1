using System.Text.Json;
using System.Text.Json.Serialization;

namespace QbPotDoubanAi;

public sealed class LocalWatchlistItem
{
    public string SubjectId { get; set; } = "";
    public string SubjectUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string Year { get; set; } = "";
    public string Identity { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Director { get; set; } = "";
    public string Cast { get; set; } = "";
    public string Score { get; set; } = "";
    public string Comment { get; set; } = "";
    public string PosterPath { get; set; } = "";
    public string PosterSourceUrl { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Note { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class LocalWatchlistState
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, LocalWatchlistItem> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Owns the application's local watchlist. It deliberately has no reference
/// to the official Douban history model or any official Douban status.
/// </summary>
public sealed class LocalWatchlistStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _jsonPath;
    private readonly string _postersDirectory;
    private LocalWatchlistState _state;

    public LocalWatchlistStore(string? dataDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoubanBrowserReminder")
            : dataDirectory;
        _directory = Path.Combine(root, "watchlist");
        _jsonPath = Path.Combine(_directory, "watchlist.json");
        _postersDirectory = Path.Combine(_directory, "posters");
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(_postersDirectory);
        _state = LoadFromDisk();
    }

    public string DirectoryPath => _directory;
    public string JsonPath => _jsonPath;
    public string PostersDirectory => _postersDirectory;

    public IReadOnlyList<LocalWatchlistItem> Snapshot()
    {
        lock (_gate)
        {
            return _state.Items.Values
                .OrderByDescending(item => item.AddedAt)
                .ThenBy(item => item.SubjectId, StringComparer.Ordinal)
                .Select(Clone)
                .ToList();
        }
    }

    public LocalWatchlistItem? Find(string subjectId)
    {
        lock (_gate)
            return _state.Items.TryGetValue(subjectId, out var item) ? Clone(item) : null;
    }

    public bool HasPoster(LocalWatchlistItem item)
    {
        lock (_gate)
        {
            var path = ResolvePosterPath(item.PosterPath);
            return path is not null && File.Exists(path);
        }
    }

    public LocalWatchlistItem SetPosterPath(string subjectId, string posterPath)
    {
        var fileName = Path.GetFileName(posterPath.Trim());
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, posterPath.Trim(), StringComparison.Ordinal) ||
            !fileName.StartsWith(subjectId + ".", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("本地待看海报文件名无效。");

        lock (_gate)
        {
            if (!_state.Items.TryGetValue(subjectId, out var item))
                throw new KeyNotFoundException("本地待看条目不存在。");
            DeletePosterFiles(subjectId, fileName);
            item.PosterPath = fileName;
            item.UpdatedAt = DateTime.Now;
            SaveToDisk();
            return Clone(item);
        }
    }

    public LocalWatchlistItem AddOrUpdate(LocalWatchlistItem incoming)
    {
        var normalized = Normalize(incoming);
        lock (_gate)
        {
            var now = DateTime.Now;
            if (_state.Items.TryGetValue(normalized.SubjectId, out var existing))
            {
                normalized.AddedAt = existing.AddedAt == default ? now : existing.AddedAt;
                normalized.UpdatedAt = now;
                if (string.IsNullOrWhiteSpace(normalized.PosterPath)) normalized.PosterPath = existing.PosterPath;
                if (string.IsNullOrWhiteSpace(normalized.Note)) normalized.Note = existing.Note;
                if (string.IsNullOrWhiteSpace(normalized.Identity)) normalized.Identity = existing.Identity;
                if (string.IsNullOrWhiteSpace(normalized.Genre)) normalized.Genre = existing.Genre;
                if (string.IsNullOrWhiteSpace(normalized.Director)) normalized.Director = existing.Director;
                if (string.IsNullOrWhiteSpace(normalized.Cast)) normalized.Cast = existing.Cast;
                if (string.IsNullOrWhiteSpace(normalized.Score)) normalized.Score = existing.Score;
                if (string.IsNullOrWhiteSpace(normalized.Comment)) normalized.Comment = existing.Comment;
            }
            else
            {
                normalized.AddedAt = normalized.AddedAt == default ? now : normalized.AddedAt;
                normalized.UpdatedAt = now;
            }

            _state.Items[normalized.SubjectId] = normalized;
            SaveToDisk();
            return Clone(normalized);
        }
    }

    public bool Remove(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId)) return false;
        lock (_gate)
        {
            if (!_state.Items.Remove(subjectId)) return false;
            SaveToDisk();
            DeletePosterFiles(subjectId);
            return true;
        }
    }

    private LocalWatchlistState LoadFromDisk()
    {
        if (!File.Exists(_jsonPath)) return new LocalWatchlistState();

        try
        {
            var json = File.ReadAllText(_jsonPath);
            var state = JsonSerializer.Deserialize<LocalWatchlistState>(json, JsonOptions)
                ?? throw new InvalidDataException("本地待看文件为空。");
            state.Items ??= new Dictionary<string, LocalWatchlistItem>(StringComparer.OrdinalIgnoreCase);
            var normalized = new Dictionary<string, LocalWatchlistItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in state.Items)
            {
                var item = Normalize(pair.Value ?? throw new InvalidDataException("本地待看包含空条目。"));
                if (!string.Equals(pair.Key, item.SubjectId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("本地待看键与 SubjectId 不一致。");
                if (!normalized.TryAdd(item.SubjectId, item))
                    throw new InvalidDataException("本地待看包含重复 SubjectId。");
            }
            state.SchemaVersion = state.SchemaVersion <= 0 ? 1 : state.SchemaVersion;
            state.Items = normalized;
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Never replace a corrupt file during load. The caller can report
            // this error while the original file remains available for repair.
            throw new InvalidDataException($"本地待看数据损坏，原文件已保留：{_jsonPath}", ex);
        }
    }

    private void SaveToDisk()
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = _jsonPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(temporaryPath, json, new System.Text.UTF8Encoding(false));
            if (File.Exists(_jsonPath))
            {
                var backupPath = _jsonPath + ".bak";
                File.Replace(temporaryPath, _jsonPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _jsonPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void DeletePosterFiles(string subjectId, string? keepFileName = null)
    {
        foreach (var path in Directory.EnumerateFiles(_postersDirectory, subjectId + ".*"))
        {
            if (!string.IsNullOrWhiteSpace(keepFileName) && string.Equals(Path.GetFileName(path), keepFileName, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(path); } catch { }
        }
    }

    private string? ResolvePosterPath(string posterPath)
    {
        if (string.IsNullOrWhiteSpace(posterPath)) return null;
        var fileName = Path.GetFileName(posterPath.Trim());
        if (!string.Equals(fileName, posterPath.Trim(), StringComparison.Ordinal) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        var path = Path.Combine(_postersDirectory, fileName);
        return Path.GetFullPath(path).StartsWith(Path.GetFullPath(_postersDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    private static LocalWatchlistItem Normalize(LocalWatchlistItem item)
    {
        var subjectId = item.SubjectId.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(subjectId, "^\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException("本地待看 SubjectId 无效。");
        if (!Uri.TryCreate(item.SubjectUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("movie.douban.com", StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/subject/\\d+/?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException("本地待看详情 URL 无效。");
        var urlSubjectId = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, "^/subject/(\\d+)/?$").Groups[1].Value;
        if (!string.Equals(subjectId, urlSubjectId, StringComparison.Ordinal))
            throw new InvalidDataException("本地待看 SubjectId 与详情 URL 不一致。");

        item.SubjectId = subjectId;
        item.SubjectUrl = $"https://movie.douban.com/subject/{subjectId}/";
        item.Title = string.IsNullOrWhiteSpace(item.Title) ? $"豆瓣条目 {subjectId}" : item.Title.Trim();
        item.OriginalTitle = item.OriginalTitle.Trim();
        item.Year = item.Year.Trim();
        item.Identity = item.Identity.Trim();
        item.Genre = item.Genre.Trim();
        item.Director = item.Director.Trim();
        item.Cast = item.Cast.Trim();
        item.Score = item.Score.Trim();
        item.Comment = item.Comment.Trim();
        item.PosterPath = item.PosterPath.Trim();
        item.PosterSourceUrl = item.PosterSourceUrl.Trim();
        item.Note = item.Note.Trim();
        item.Source = item.Source.Trim();
        return item;
    }

    private static LocalWatchlistItem Clone(LocalWatchlistItem item) => new()
    {
        SubjectId = item.SubjectId,
        SubjectUrl = item.SubjectUrl,
        Title = item.Title,
        OriginalTitle = item.OriginalTitle,
        Year = item.Year,
        Identity = item.Identity,
        Genre = item.Genre,
        Director = item.Director,
        Cast = item.Cast,
        Score = item.Score,
        Comment = item.Comment,
        PosterPath = item.PosterPath,
        PosterSourceUrl = item.PosterSourceUrl,
        AddedAt = item.AddedAt,
        UpdatedAt = item.UpdatedAt,
        Note = item.Note,
        Source = item.Source
    };
}
