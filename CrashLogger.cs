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
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoubanBrowserReminder", "logs", "diagnostic.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
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
