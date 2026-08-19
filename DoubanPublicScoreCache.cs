using System.Text.Json;

namespace QbPotDoubanAi;

internal sealed record DoubanPublicScoreEntry(double Score, DateTimeOffset UpdatedAtUtc);

internal sealed record DoubanPublicScoreCacheFile(
    int SchemaVersion,
    DateTimeOffset UpdatedAtUtc,
    Dictionary<string, DoubanPublicScoreEntry> Items);

internal sealed class DoubanPublicScoreCache
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, DoubanPublicScoreEntry> _items = new(StringComparer.Ordinal);

    internal DoubanPublicScoreCache(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "douban-public-score-v1.json");
        Load();
    }

    internal bool TryGet(string subjectId, out double score)
    {
        lock (_gate)
        {
            score = 0;
            return _items.TryGetValue(subjectId, out var entry) &&
                   entry.Score is > 0 and <= 10 &&
                   (score = entry.Score) > 0;
        }
    }

    internal async Task StoreAsync(string subjectId, double score, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId) ||
            !subjectId.All(char.IsDigit) ||
            score is <= 0 or > 10)
            return;

        lock (_gate)
            _items[subjectId] = new DoubanPublicScoreEntry(score, DateTimeOffset.UtcNow);

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, DoubanPublicScoreEntry> copy;
            lock (_gate)
                copy = new Dictionary<string, DoubanPublicScoreEntry>(_items, StringComparer.Ordinal);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            var payload = new DoubanPublicScoreCacheFile(
                SchemaVersion,
                DateTimeOffset.UtcNow,
                copy);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(payload, Json), cancellationToken).ConfigureAwait(false);
            File.Move(temp, _path, true);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var payload = JsonSerializer.Deserialize<DoubanPublicScoreCacheFile>(
                File.ReadAllText(_path), Json);
            if (payload is null || payload.SchemaVersion != SchemaVersion || payload.Items is null)
                return;

            lock (_gate)
            {
                _items.Clear();
                foreach (var pair in payload.Items)
                {
                    if (pair.Key.All(char.IsDigit) &&
                        pair.Value.Score is > 0 and <= 10)
                        _items[pair.Key] = pair.Value;
                }
            }

            DiagnosticLogger.Write($"Douban public score cache loaded; Items={_items.Count}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLogger.Write($"Douban public score cache load failed; Error={ex.Message}");
        }
    }
}