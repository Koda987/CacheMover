using System.Diagnostics;
using System.Text.Json;

namespace CacheMover;

public record MoveResult(bool Success, string? Error, string? BakPath, long Bytes, int Files);

/// <summary>
/// 安全迁移流程：
/// 占用自检 → 复制到目标 → 校验 → 原文件夹改名留备份 → 创建目录联接 → 校验联接。
/// 任何一步失败都会回滚，原文件夹保持完好。
/// </summary>
public static class Mover
{
    public static (int files, long bytes) Stats(string dir, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0;
        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var f in Directory.EnumerateFiles(Util.LongPath(dir), "*", opt))
        {
            ct.ThrowIfCancellationRequested();
            try { bytes += new FileInfo(f).Length; files++; } catch { /* 个别文件读不到，跳过 */ }
        }
        return (files, bytes);
    }

    public static async Task<MoveResult> MoveAsync(
        string src, string dst, IProgress<string> log, IProgress<int> percent, CancellationToken ct)
    {
        src = Path.TrimEndingDirectorySeparator(Path.GetFullPath(src));
        dst = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dst));
        var sw = Stopwatch.StartNew();
        bool copied = false, renamed = false, linked = false;
        string? bak = null;

        async Task CleanupDstAsync()
        {
            try
            {
                log.Report("正在清理目标位置的临时复制…");
                await Task.Run(() => ForceDeleteDir(dst));
            }
            catch (Exception ex) { log.Report("清理目标位置失败（可稍后手动删除）：" + ex.Message); }
        }

        try
        {
            var dstParent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstParent)) Directory.CreateDirectory(Util.LongPath(dstParent));
            if (Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any())
                return new MoveResult(false, "目标文件夹已存在且不为空，请选择空文件夹或新路径", null, 0, 0);

            log.Report("① 自检文件占用（试改名）…");
            ProbeLock(src);

            log.Report("② 统计文件…");
            var (files, bytes) = Stats(src, ct);
            log.Report($"   共 {files:N0} 个文件，{Util.FmtBytes(bytes)}");

            log.Report("③ 复制到目标位置…（大文件夹可能耗时较长）");
            long done = 0;
            await Task.Run(() => CopyTree(src, dst, percent, bytes, ref done, ct), ct);
            copied = true;
            percent.Report(96);

            log.Report("④ 校验复制结果…");
            var st2 = await Task.Run(() => Stats(dst, ct), ct);
            if (st2.files != files || st2.bytes != bytes)
            {
                await CleanupDstAsync();
                return new MoveResult(false,
                    $"校验不一致（源 {files} 个文件/{Util.FmtBytes(bytes)}，目标 {st2.files} 个文件/{Util.FmtBytes(st2.bytes)}），已回滚，原文件夹未动",
                    null, 0, 0);
            }

            log.Report("⑤ 把原文件夹改名为备份…");
            bak = BackupName(src);
            await Task.Run(() => Directory.Move(Util.LongPath(src), Util.LongPath(bak)), ct);
            renamed = true;

            log.Report("⑥ 在原位置创建目录联接…");
            await Task.Run(() => CreateJunction(src, dst), ct);
            linked = true;

            var di = new DirectoryInfo(src);
            var linkTarget = di.LinkTarget;
            if (linkTarget == null ||
                !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkTarget)), dst, StringComparison.OrdinalIgnoreCase))
            {
                await RollbackAsync(src, bak, dst, CleanupDstAsync);
                return new MoveResult(false, "联接指向校验失败，已还原原文件夹", null, 0, 0);
            }

            percent.Report(100);
            log.Report($"✔ 迁移完成，耗时 {sw.Elapsed.TotalSeconds:F0} 秒");
            log.Report($"  备份保留在：{bak}");
            log.Report($"  确认软件运行正常后，点「删除备份」才会真正释放 C 盘的 {Util.FmtBytes(bytes)}");
            WriteHistory(src, dst, bak, bytes, files);
            return new MoveResult(true, null, bak, bytes, files);
        }
        catch (OperationCanceledException)
        {
            if (linked)
            {
                try { Directory.Delete(Util.LongPath(src), false); } catch { /* 保留现状 */ }
            }
            if (renamed && bak != null) await TryRestoreAsync(src, bak);
            if (copied) await CleanupDstAsync();
            return new MoveResult(false, "已取消，已尽量还原原状", null, 0, 0);
        }
        catch (Exception ex)
        {
            if (linked)
            {
                try { Directory.Delete(Util.LongPath(src), false); } catch { /* 保留现状 */ }
            }
            if (renamed && bak != null) await TryRestoreAsync(src, bak);
            if (copied) await CleanupDstAsync();
            return new MoveResult(false, ex.Message, null, 0, 0);
        }
    }

    public static void ForceDeleteDir(string p)
    {
        try { Directory.Delete(Util.LongPath(p), true); }
        catch
        {
            try
            {
                var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
                foreach (var f in Directory.EnumerateFiles(Util.LongPath(p), "*", opt))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* 忽略 */ }
                }
            }
            catch { /* 忽略 */ }
            Directory.Delete(Util.LongPath(p), true);
        }
    }

    /// <summary>用改名探测文件夹是否被进程占用（改名是 Windows 上最快的占用探测方式）。</summary>
    private static void ProbeLock(string src)
    {
        string probe = src + ".cm_probe";
        if (Directory.Exists(probe))
            throw new IOException($"存在残留的探测目录 {probe}，请手动删除后重试");
        Directory.Move(Util.LongPath(src), Util.LongPath(probe));
        try { Directory.Move(Util.LongPath(probe), Util.LongPath(src)); }
        catch
        {
            // 极少见：刚移过去就移不回来（杀软扫描），再试一次
            Thread.Sleep(500);
            Directory.Move(Util.LongPath(probe), Util.LongPath(src));
        }
    }

    private static string BackupName(string src)
    {
        var bak = src + ".bak";
        if (!Directory.Exists(bak) && !File.Exists(bak)) return bak;
        return src + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }

    private static void CopyTree(string src, string dst, IProgress<int> percent, long totalBytes, ref long done, CancellationToken ct)
    {
        Directory.CreateDirectory(Util.LongPath(dst));

        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            var target = Path.Combine(dst, name);
            var attrs = File.GetAttributes(dir);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                // 源里的子链接原样保留为链接，不复制其内容
                var linkTarget = new DirectoryInfo(dir).LinkTarget;
                if (linkTarget != null) CreateJunction(target, linkTarget);
            }
            else
            {
                CopyTree(dir, target, percent, totalBytes, ref done, ct);
            }
        }

        foreach (var file in Directory.EnumerateFiles(src))
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(dst, Path.GetFileName(file));
            try
            {
                CopyFileRetry(Util.LongPath(file), Util.LongPath(dest), ct);
            }
            catch (Exception ex)
            {
                throw new IOException($"复制文件失败：{file}（{ex.Message}）", ex);
            }
            try { done += new FileInfo(Util.LongPath(file)).Length; } catch { /* 忽略 */ }
            if (totalBytes > 0) percent.Report((int)Math.Min(done * 95 / totalBytes, 95));
        }
    }

    private static void CopyFileRetry(string s, string d, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Copy(s, d, false); return; }
            catch (IOException) when (attempt == 1) { Thread.Sleep(400); }
            catch (UnauthorizedAccessException) when (attempt == 1) { Thread.Sleep(400); }
        }
    }

    /// <summary>目录联接（junction）：不需要管理员权限。通过系统自带 mklink /J 创建。</summary>
    public static void CreateJunction(string link, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new IOException("无法启动 mklink");
        p.WaitForExit(15000);

        var di = new DirectoryInfo(link);
        if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) == 0)
            throw new IOException($"在 {link} 创建目录联接失败（目标：{target}）");
    }

    private static async Task TryRestoreAsync(string src, string bak)
    {
        try
        {
            if (!Directory.Exists(src) && Directory.Exists(bak))
                await Task.Run(() => Directory.Move(Util.LongPath(bak), Util.LongPath(src)));
        }
        catch { /* 还原失败只能提示用户手动处理 */ }
    }

    private static async Task RollbackAsync(string src, string bak, string link, Func<Task> cleanupDst)
    {
        try { if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(Util.LongPath(link), false); }
        catch { /* 忽略 */ }
        await TryRestoreAsync(src, bak);
        await cleanupDst();
    }

    private static void WriteHistory(string src, string dst, string bak, long bytes, int files)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CacheMover");
            Directory.CreateDirectory(dir);
            var line = JsonSerializer.Serialize(new
            {
                time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                src,
                dst,
                bak,
                bytes,
                files
            });
            File.AppendAllText(Path.Combine(dir, "history.jsonl"), line + Environment.NewLine);
        }
        catch { /* 历史记录失败不影响迁移 */ }
    }
}
