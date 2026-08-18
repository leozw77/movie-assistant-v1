using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace QbPotDoubanAi;

internal static class SingleInstanceControl
{
    private static string _pipeName = "QbPotDoubanAi.Control.v1";

    public static void UseUnifiedShellPreviewPipe(string? identity = null) =>
        _pipeName = $"QbPotDoubanAi.Control.UnifiedShellPreview.{IdentitySuffix(identity)}.v1";

    public static string GetUnifiedShellPreviewMutexName(string? identity = null) =>
        $"Local\\DoubanBrowserReminder.UnifiedShellPreview.{IdentitySuffix(identity)}";

    private static string IdentitySuffix(string? identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity ?? "default"));
        return Convert.ToHexString(bytes)[..12];
    }

    public static bool TrySendCommand(string command, int timeoutMilliseconds = 1200)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.None);
                client.Connect(timeoutMilliseconds);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(command);
                return true;
            }
            catch (TimeoutException) when (attempt < 2) { Thread.Sleep(100); }
            catch (IOException) when (attempt < 2) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 2) { Thread.Sleep(100); }
            catch (TimeoutException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        return false;
    }

    public static NamedPipeServerStream CreateServer()
        => new(
            _pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            0,
            0);
}
