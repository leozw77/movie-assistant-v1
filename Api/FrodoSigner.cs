using System.Security.Cryptography;
using System.Text;

namespace QbPotDoubanAi;

internal static class FrodoSigner
{
    public static string SignGet(string path, long unixSeconds, string secret)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Frodo path 必须是以 / 开头的绝对 API path。", nameof(path));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Frodo secret 不能为空。", nameof(secret));

        // Frodo Android signs GET&<path with '/' encoded as %2F>&<unix seconds>.
        var encodedPath = path.Replace("/", "%2F", StringComparison.Ordinal);
        var message = $"GET&{encodedPath}&{unixSeconds}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }
}
