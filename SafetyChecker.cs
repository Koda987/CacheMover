namespace CacheMover;

public enum CheckLevel { Ok, Warn, Fail }

public record CheckResult(CheckLevel Level, string Message);

public static class SafetyChecker
{
    public static List<CheckResult> Check(string? src, string? dst, long srcSize, CancellationToken ct)
    {
        var results = new List<CheckResult>();
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localLow = Path.Combine(profile, "AppData", "LocalLow");
        string msRoot = Path.Combine(localAppData, "Microsoft");
        string msWindows = Path.Combine(localAppData, "Microsoft", "Windows");
        string packages = Path.Combine(localAppData, "Packages");
        string oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        string temp = Path.GetTempPath();

        if (string.IsNullOrWhiteSpace(src))
        {
            results.Add(new CheckResult(CheckLevel.Fail, "请先选择要转移的文件夹"));
            return results;
        }
        if (string.IsNullOrWhiteSpace(dst))
        {
            results.Add(new CheckResult(CheckLevel.Fail, "请先选择目标位置（其他盘的文件夹）"));
            return results;
        }

        string s, d;
        try { s = Path.TrimEndingDirectorySeparator(Path.GetFullPath(src)); }
        catch { results.Add(new CheckResult(CheckLevel.Fail, "源路径格式不正确")); return results; }
        try { d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dst)); }
        catch { results.Add(new CheckResult(CheckLevel.Fail, "目标路径格式不正确")); return results; }

        // ---- 源路径检查 ----
        if (!Directory.Exists(s))
        {
            results.Add(new CheckResult(CheckLevel.Fail, File.Exists(s)
                ? "所选源路径是一个文件，请选择文件夹"
                : "源文件夹不存在：" + s));
        }
        else
        {
            if (Same(s, Path.GetPathRoot(s) ?? "")) results.Add(new CheckResult(CheckLevel.Fail, "不能迁移整个盘符根目录"));
            if (Same(s, windir) || Under(s, windir)) results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：Windows 系统目录"));
            if (Same(s, pf) || Under(s, pf) || Same(s, pf86) || Under(s, pf86))
                results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：程序安装目录（Program Files）"));
            if (Same(s, profile)) results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：用户主目录本身"));
            if (Same(s, programData)) results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：ProgramData 总目录"));
            else if (Under(s, programData))
                results.Add(new CheckResult(CheckLevel.Warn, "ProgramData 下的程序数据，部分软件不支持迁移，请确认软件官方允许"));
            if (Same(s, localAppData) || Same(s, roaming) || Same(s, localLow))
                results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：AppData 总目录不能整体搬走，请选择其中的具体软件缓存文件夹"));
            if (Same(s, msWindows) || Under(s, msWindows))
                results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：该目录包含系统缓存与注册表数据（如 UsrClass.dat）"));
            if (Same(s, packages) || Under(s, packages))
                results.Add(new CheckResult(CheckLevel.Fail, "禁止迁移：UWP 应用（微软商店）数据目录"));
            if (Under(s, msRoot) && !Under(s, msWindows))
                results.Add(new CheckResult(CheckLevel.Warn, "微软相关目录，个别系统组件可能不兼容，请确认是纯缓存数据"));
            if (oneDrive.Length > 0 && (Same(s, oneDrive) || Under(s, oneDrive)))
                results.Add(new CheckResult(CheckLevel.Warn, "OneDrive 云同步目录不建议用联接方式迁移"));
            if (Same(s, temp) || Under(s, temp))
                results.Add(new CheckResult(CheckLevel.Warn, "临时目录迁移后如有异常，请改回默认位置"));
            if (Directory.Exists(s) && (File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0)
                results.Add(new CheckResult(CheckLevel.Fail, "该文件夹已经是链接（或正处于迁移状态），无需再次迁移"));
        }

        // ---- 目标路径检查 ----
        if (Same(s, d)) results.Add(new CheckResult(CheckLevel.Fail, "源和目标不能是同一个文件夹"));
        if (Under(d, s)) results.Add(new CheckResult(CheckLevel.Fail, "目标文件夹不能在源文件夹内部"));
        if (Under(s, d)) results.Add(new CheckResult(CheckLevel.Fail, "目标文件夹不能是源文件夹的上层目录"));

        string dstRoot = Path.GetPathRoot(d) ?? "";
        if (dstRoot.Length == 0)
        {
            results.Add(new CheckResult(CheckLevel.Fail, "无法识别目标所在分区"));
        }
        else
        {
            var drive = new DriveInfo(dstRoot);
            if (!drive.IsReady) results.Add(new CheckResult(CheckLevel.Fail, "目标分区不可用"));
            else
            {
                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    results.Add(new CheckResult(CheckLevel.Warn, $"目标分区是 {drive.DriveFormat} 而非 NTFS，联接兼容性可能有问题"));
                if (string.Equals(Path.GetPathRoot(s), dstRoot, StringComparison.OrdinalIgnoreCase))
                    results.Add(new CheckResult(CheckLevel.Warn, "源和目标在同一分区，转移不会释放空间"));
                if (srcSize > 0)
                {
                    if (drive.AvailableFreeSpace < srcSize)
                        results.Add(new CheckResult(CheckLevel.Fail,
                            $"目标盘空间不足：需要约 {Util.FmtBytes(srcSize)}，{drive.Name} 只剩 {Util.FmtBytes(drive.AvailableFreeSpace)}"));
                    else if (drive.AvailableFreeSpace < srcSize * 11 / 10)
                        results.Add(new CheckResult(CheckLevel.Warn, $"目标盘剩余空间较小，迁移后该盘只剩 {Util.FmtBytes(drive.AvailableFreeSpace - srcSize)}"));
                }
            }
        }

        if (Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any())
            results.Add(new CheckResult(CheckLevel.Fail, "目标文件夹必须为空（或不存在，迁移时会自动创建）"));

        // ---- 文件占用抽查 ----
        if (Directory.Exists(s) && (File.GetAttributes(s) & FileAttributes.ReparsePoint) == 0 && !ct.IsCancellationRequested)
        {
            var locked = FindLockedFile(s, 3000);
            if (locked != null)
                results.Add(new CheckResult(CheckLevel.Warn,
                    "检测到可能被占用的文件，请先关闭正在使用该文件夹的软件（如微信、浏览器、游戏、Docker 等）。示例：" + locked));
            else
                results.Add(new CheckResult(CheckLevel.Ok, "抽查未发现被占用的文件"));
        }

        if (results.All(r => r.Level != CheckLevel.Fail))
            results.Add(new CheckResult(CheckLevel.Ok, "检查通过，可以开始迁移"));
        return results;
    }

    private static string? FindLockedFile(string dir, int maxFiles)
    {
        int n = 0;
        try
        {
            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var f in Directory.EnumerateFiles(Util.LongPath(dir), "*", opt))
            {
                if (n++ >= maxFiles) break;
                try { using var _ = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None); }
                catch (IOException) { return f; }
                catch (UnauthorizedAccessException) { /* 无权限 ≠ 被占用 */ }
            }
        }
        catch { /* 抽查失败不阻断 */ }
        return null;
    }

    private static string Norm(string p)
    {
        try { p = Path.GetFullPath(p); } catch { /* 保留原样 */ }
        return p.TrimEnd('\\', '/').ToLowerInvariant();
    }

    private static bool Same(string a, string b) => Norm(a) == Norm(b) && Norm(a).Length > 0;

    private static bool Under(string child, string parent)
    {
        var c = Norm(child);
        var p = Norm(parent);
        return p.Length > 0 && c.StartsWith(p + "\\", StringComparison.Ordinal);
    }
}
