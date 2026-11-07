namespace CacheMover;

public static class Util
{
    /// <summary>加 \\?\ 前缀以支持超过 260 字符的长路径。</summary>
    public static string LongPath(string p)
    {
        if (p.StartsWith(@"\\?\")) return p;
        var full = Path.GetFullPath(p);
        if (full.StartsWith(@"\\")) return @"\\?\UNC\" + full[2..];
        return @"\\?\" + full;
    }

    public static string FmtBytes(long n)
    {
        const long KB = 1024, MB = KB * 1024, GB = MB * 1024;
        return n switch
        {
            >= GB => $"{(double)n / GB:F2} GB",
            >= MB => $"{(double)n / MB:F1} MB",
            >= KB => $"{(double)n / KB:F1} KB",
            _ => $"{n} B"
        };
    }
}
