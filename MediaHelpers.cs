using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace QbPotDoubanAi;

public sealed record PotSnapshot(IntPtr Window, string Title, long CurrentMs, long TotalMs, int Status)
{
    public bool HasMedia => Window != IntPtr.Zero && TotalMs > 0 && CurrentMs >= 0;
    public double Ratio => HasMedia ? Math.Clamp((double)CurrentMs / TotalMs, 0, 1) : 0;
}

public static class PotPlayer
{
    private const uint WmUser = 0x400;
    public static PotSnapshot Read()
    {
        var w = FindWindow("PotPlayer64", null); if (w == IntPtr.Zero) w = FindWindow("PotPlayer", null);
        if (w == IntPtr.Zero) return new(w, "", 0, 0, -1);
        var text = new StringBuilder(1024); GetWindowText(w, text, text.Capacity);
        var title = Regex.Replace(text.ToString(), @"\s+-\s+PotPlayer(?: 64 bit)?$", "", RegexOptions.IgnoreCase).Trim();
        return new(w, title, SendMessage(w, WmUser, 0x5004, 0).ToInt64(), SendMessage(w, WmUser, 0x5002, 0).ToInt64(), (int)SendMessage(w, WmUser, 0x5006, 0));
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr w, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr w, uint m, int wp, int lp);
}

public static class MovieTitle
{
    private static readonly Regex ReleaseTag = new(@"(?i)^(?:2160p|1080p|720p|4k|web[- .]?dl|webrip|bluray|bdrip|x26[45]|hevc|avc|中字|简|繁|双语|国粤双语)$", RegexOptions.Compiled);

    public static int? YearFromPath(string path)
    {
        var match = Regex.Match(Path.GetFileName(path), @"(?<!\d)((?:19|20)\d{2})(?!\d)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
    }

    public static MovieTitleParts ParsePath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var aliases = new List<string>();
        foreach (Match match in Regex.Matches(fileName, @"[\[【（](?<value>.*?)[\]】）]"))
        {
            var value = CleanSegment(match.Groups["value"].Value);
            if (IsTitleAlias(value)) aliases.Add(value);
        }

        var primary = CleanTitle(fileName);
        if (!string.IsNullOrWhiteSpace(primary)) aliases.Add(primary);
        aliases = aliases.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (aliases.Count == 0) aliases.Add(fileName.Trim());
        return BuildParts(fileName, primary, aliases, YearFromPath(path));
    }

    public static MovieTitleParts FromTitle(string title, int? year = null)
    {
        var primary = CleanSegment(title);
        var aliases = string.IsNullOrWhiteSpace(primary) ? [] : new List<string> { primary };
        return BuildParts(title, primary, aliases, year);
    }

    public static string FromPath(string path) => ParsePath(path).PrimaryTitle;

    public static IReadOnlyList<int> YearsFromText(string text) =>
        Regex.Matches(text ?? "", @"(?<!\d)((?:19|20)\d{2})(?!\d)")
            .Select(x => int.Parse(x.Groups[1].Value)).Distinct().ToArray();

    public static string Normalize(string s) => Regex.Replace(s ?? "", @"[^\p{L}\p{N}]+", "").ToLowerInvariant();

    private static MovieTitleParts BuildParts(string source, string primary, IReadOnlyList<string> aliases, int? year)
    {
        var chinese = aliases.FirstOrDefault(ContainsChinese);
        var english = aliases.FirstOrDefault(x => !ContainsChinese(x) && Regex.IsMatch(x, @"[A-Za-z]"));
        return new MovieTitleParts(
            source,
            primary,
            chinese ?? "",
            english ?? "",
            year,
            aliases);
    }

    private static string CleanTitle(string value)
    {
        var s = value;
        s = Regex.Replace(s, @"(?i)\bS\d{1,2}E\d{1,3}\b.*$", " ");
        s = Regex.Replace(s, @"(?i)(?:\b(?:19|20)\d{2}\b|\b(?:2160p|1080p|720p|4k|web[- .]?dl|webrip|bluray|bdrip|x26[45]|hevc|avc)\b).*$", " ");
        s = Regex.Replace(s, @"[\[【（].*?[\]】）]", " ");
        return CleanSegment(s);
    }

    private static string CleanSegment(string value)
    {
        var s = value.Trim();
        s = Regex.Replace(s, @"[._]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_');
        return s;
    }

    private static bool IsTitleAlias(string value) =>
        value.Length >= 2 && !ReleaseTag.IsMatch(value) && (ContainsChinese(value) || Regex.IsMatch(value, @"[A-Za-z]{2}"));

    private static bool ContainsChinese(string value) => value.Any(ch => ch is >= '\u3400' and <= '\u9fff');
}

public sealed record MovieTitleParts(
    string SourceFileName,
    string PrimaryTitle,
    string ChineseTitle,
    string EnglishTitle,
    int? Year,
    IReadOnlyList<string> Aliases);

