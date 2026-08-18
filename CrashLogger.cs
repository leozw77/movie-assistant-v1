namespace QbPotDoubanAi;

public static class AppInfo
{
    public const string Version = "1.0.0";
}

public static class CrashLogger
{
    public static void Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoubanBrowserReminder", "logs");
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            File.WriteAllText(path, $"{DateTime.Now:O}\n{exception}");
        }
        catch { }
    }
}

public static class DiagnosticLogger
{
    private const long MaxLogBytes = 10L * 1024 * 1024;
    private const int MaxArchives = 3;
    private const int MaxMessageChars = 16_384;
    private static readonly object Gate = new();
    private static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoubanBrowserReminder", "logs");
    private static string LogPath => Path.Combine(LogDirectory, "diagnostic.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var normalized = NormalizeMessage(message ?? "");
                if (normalized.Length == 0) return;
                var bounded = BoundMessage(normalized);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {bounded}{Environment.NewLine}";
                var incomingBytes = System.Text.Encoding.UTF8.GetByteCount(line);
                RotateIfNeeded(incomingBytes);
                File.AppendAllText(LogPath, line);
            }
        }
        catch { }
    }

    public static void WriteStartup()
    {
        var path = Environment.ProcessPath ?? "";
        var buildTime = "unknown";
        try { if (File.Exists(path)) buildTime = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
        Write($"========== Application Startup ==========\nApplication Version: {AppInfo.Version}\nExecutable Path: {path}\nBuild Time: {buildTime}\nRelease metadata: VERSION.json in the executable directory\n==========================================");
    }

    private static string NormalizeMessage(string message)
    {
        if (message.Length == 0) return "";

        if (message.StartsWith("Unified Shell message posted;", StringComparison.Ordinal) &&
            message.Contains("Type=doubanShellPosterFallback", StringComparison.Ordinal))
            return "";

        if (message.StartsWith("Unified Shell poster fallback posted;", StringComparison.Ordinal))
            return "";

        const string payloadMarker = "; Payload=";
        if (message.StartsWith("Unified Shell data posted;", StringComparison.Ordinal))
        {
            var markerIndex = message.IndexOf(payloadMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var payload = message[(markerIndex + payloadMarker.Length)..];
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(payload);
                    var root = document.RootElement;
                    var requestId = ReadJsonString(root, "requestId");
                    var operation = ReadJsonString(root, "operation");
                    var error = CompactLogValue(ReadJsonString(root, "error"), 240);
                    var generation = root.TryGetProperty("generation", out var generationValue) && generationValue.TryGetInt32(out var generationNumber)
                        ? generationNumber
                        : 0;
                    var items = root.TryGetProperty("items", out var itemsValue) && itemsValue.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? itemsValue.GetArrayLength()
                        : 0;
                    var source = "DOM";
                    if (root.TryGetProperty("dom", out var domValue) && domValue.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        ReadJsonString(domValue, "source").Equals("frodo-api", StringComparison.OrdinalIgnoreCase))
                        source = "Frodo";
                    var status = error.Length == 0 ? "ok" : "error";
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(payload);
                    return $"Unified Shell data posted; RequestId={requestId}; Generation={generation}; Source={source}; Operation={operation}; Status={status}; Items={items}; Bytes={bytes}; Error={error}";
                }
                catch (Exception ex)
                {
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(payload);
                    return $"Unified Shell data posted; Status=unparsed; Bytes={bytes}; Error={CompactLogValue(ex.Message, 160)}";
                }
            }
        }

        const string readResultMarker = "; ReadResult=";
        if (message.StartsWith("Unified Shell Source read completed;", StringComparison.Ordinal))
        {
            var markerIndex = message.IndexOf(readResultMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var readResult = message[(markerIndex + readResultMarker.Length)..];
                var prefix = message[..markerIndex];
                return $"{prefix}; ReadResultBytes={System.Text.Encoding.UTF8.GetByteCount(readResult)}";
            }
        }

        return message;
    }

    private static string ReadJsonString(System.Text.Json.JsonElement owner, string name) =>
        owner.ValueKind == System.Text.Json.JsonValueKind.Object &&
        owner.TryGetProperty(name, out var value) &&
        value.ValueKind == System.Text.Json.JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    private static string CompactLogValue(string value, int maxLength)
    {
        var compact = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "…";
    }

    private static string BoundMessage(string message)
    {
        if (message.Length <= MaxMessageChars) return message;
        var omitted = message.Length - MaxMessageChars;
        return message[..MaxMessageChars] + $"... [truncated {omitted} chars]";
    }

    private static void RotateIfNeeded(int incomingBytes)
    {
        CleanupOversizedArchives();
        if (!File.Exists(LogPath)) return;

        long currentBytes;
        try { currentBytes = new FileInfo(LogPath).Length; }
        catch { return; }
        if (currentBytes + incomingBytes <= MaxLogBytes) return;

        if (currentBytes > MaxLogBytes)
        {
            File.Delete(LogPath);
            return;
        }

        var oldest = ArchivePath(MaxArchives);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = MaxArchives - 1; index >= 1; index--)
        {
            var source = ArchivePath(index);
            if (!File.Exists(source)) continue;
            File.Move(source, ArchivePath(index + 1), overwrite: true);
        }
        File.Move(LogPath, ArchivePath(1), overwrite: true);
    }

    private static void CleanupOversizedArchives()
    {
        for (var index = 1; index <= MaxArchives; index++)
        {
            var path = ArchivePath(index);
            if (!File.Exists(path)) continue;
            try
            {
                if (new FileInfo(path).Length > MaxLogBytes) File.Delete(path);
            }
            catch { }
        }
    }

    private static string ArchivePath(int index) => Path.Combine(LogDirectory, $"diagnostic.{index}.log");
}

public static class ReviewTransactionLogger
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DoubanBrowserReminder", "logs", "review-transactions.jsonl");

    public static void Write(object transaction)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var envelope = new
                {
                    Timestamp = DateTime.Now,
                    ProcessId = Environment.ProcessId,
                    ApplicationVersion = AppInfo.Version,
                    Transaction = transaction
                };
                File.AppendAllText(LogPath, System.Text.Json.JsonSerializer.Serialize(envelope) + Environment.NewLine);
            }
        }
        catch { }
    }
}
