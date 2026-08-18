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
                var bounded = BoundMessage(message ?? "");
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

        // Old builds could grow diagnostic.log to hundreds of MB. Do not preserve
        // an oversized legacy file as an archive; deleting it is what restores the
        // new hard storage bound on the first write after upgrade.
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
