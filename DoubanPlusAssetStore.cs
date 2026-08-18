using System.Reflection;

namespace QbPotDoubanAi;

internal static class DoubanPlusAssetStore
{
    private static readonly Assembly Assembly = typeof(DoubanPlusAssetStore).Assembly;

    internal static bool Exists(string path)
    {
        if (File.Exists(path)) return true;
        return FindResource(path) is not null;
    }

    internal static string ReadText(string path, string errorMessage)
    {
        if (File.Exists(path))
        {
            var content = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(content)) return content;
            throw new InvalidDataException(errorMessage + "资源为空。");
        }

        var resourceName = FindResource(path);
        if (resourceName is null) throw new FileNotFoundException(errorMessage, path);
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(errorMessage, path);
        using var reader = new StreamReader(stream);
        var embedded = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(embedded)) throw new InvalidDataException(errorMessage + "资源为空。");
        return embedded;
    }

    private static string? FindResource(string path)
    {
        var fileName = Path.GetFileName(path);
        var suffix = ".WebAssets.DoubanPlus." + fileName;
        return Assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
