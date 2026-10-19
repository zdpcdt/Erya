using System.IO.Compression;
using System.Formats.Tar;
using System.Net.Http;
using Erya.Core.Backup;

namespace Erya.Core.Packages;

/// <summary>Progress update: phase label plus optional 0–100 percent.</summary>
public sealed record SchemaInstallProgress(string Phase, double? Percent);

/// <summary>Outcome of one package install.</summary>
/// <param name="InstalledFiles">Files copied into the user directory.</param>
/// <param name="Bytes">Uncompressed bytes written.</param>
/// <param name="BackupPath">Snapshot of the pre-install user directory, when it had content.</param>
/// <param name="Skipped">Archive entries left out, each with the reason.</param>
public sealed record SchemaInstallReport(
    int InstalledFiles,
    long Bytes,
    string? BackupPath,
    IReadOnlyList<string> Skipped)
{
    public bool HasSkippedFiles => Skipped.Count > 0;
}

/// <summary>Outcome of one package uninstall.</summary>
/// <param name="DeletedFiles">Files removed from the user directory.</param>
/// <param name="BackupPath">Snapshot of the pre-uninstall user directory, when it had content.</param>
public sealed record SchemaUninstallReport(int DeletedFiles, string? BackupPath);

/// <summary>
/// Installs a community package: fetch the repository tarball over plain
/// HTTPS (no git dependency), unpack the RIME-relevant files to a staging
/// area, snapshot the current user directory, then copy into place. The
/// safety net is the pre-install backup — an install overwrites same-name
/// files by design (that is the upgrade path), so a rollback is always one
/// restore away.
/// </summary>
public sealed class SchemaInstaller
{
    /// <summary>Sanity cap — the largest real-world package is ~100 MB today.</summary>
    private const long MaxArchiveBytes = 512L * 1024 * 1024;

    private readonly HttpClient _client;

    public SchemaInstaller(HttpMessageHandler? handler = null)
        => _client = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromMinutes(15) }
            : new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };

    public async Task<SchemaInstallReport> InstallAsync(
        SchemaPackage package,
        string userDir,
        IProgress<SchemaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDir);

        var tempRoot = Path.Combine(Path.GetTempPath(), "erya-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var archivePath = await DownloadAsync(
                package, Path.Combine(tempRoot, "package.tgz"), progress, cancellationToken);
            var staging = Path.Combine(tempRoot, "staging");
            var skipped = await ExtractAsync(package, archivePath, staging, cancellationToken);

            // A package without a single schema installs nothing useful — fail
            // before touching the user directory.
            if (!HasSchemaFile(staging))
                throw new InvalidOperationException($"包内没有 *.schema.yaml，{package.Name} 不是有效的 RIME 方案配置。");

            Directory.CreateDirectory(userDir);
            string? backupPath = null;
            if (HasFiles(userDir))
            {
                progress?.Report(new("正在备份当前配置…", null));
                backupPath = PreInstallBackupPath(package.Id);
                try
                {
                    RimeBackup.Create(userDir, backupPath, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // No backup, no install — the backup is the rollback path.
                    throw new InvalidOperationException($"无法生成安装前备份（{backupPath}），已中止安装。", ex);
                }
            }

            progress?.Report(new("正在写入用户目录…", null));
            var (files, bytes) = CopyAll(staging, userDir);
            return new SchemaInstallReport(files, bytes, backupPath, skipped);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>
    /// Removes a package from the user directory. The repo's current file
    /// list is the delete manifest — install and uninstall read the same
    /// source, so files the package never shipped (the user's own) stay put.
    /// Whole-directory backup first; a rollback is one restore away.
    /// </summary>
    public async Task<SchemaUninstallReport> UninstallAsync(
        SchemaPackage package,
        string userDir,
        IProgress<SchemaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDir);

        var tempRoot = Path.Combine(Path.GetTempPath(), "erya-uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            progress?.Report(new("正在获取文件清单…", null));
            var archivePath = await DownloadAsync(
                package, Path.Combine(tempRoot, "package.tgz"), progress, cancellationToken);
            var staging = Path.Combine(tempRoot, "staging");
            await ExtractAsync(package, archivePath, staging, cancellationToken);

            if (!HasSchemaFile(staging))
                throw new InvalidOperationException($"包内没有 *.schema.yaml，{package.Name} 不是有效的 RIME 方案配置。");

            var userRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDir));
            var targets = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(file => SafeCombine(userRoot, Path.GetRelativePath(staging, file)))
                .OfType<string>()
                .ToList();

            string? backupPath = null;
            if (HasFiles(userDir))
            {
                progress?.Report(new("正在备份当前配置…", null));
                backupPath = PreUninstallBackupPath(package.Id);
                try
                {
                    RimeBackup.Create(userDir, backupPath, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // No backup, no delete — the backup is the rollback path.
                    throw new InvalidOperationException($"无法生成删除前备份（{backupPath}），已中止删除。", ex);
                }
            }

            progress?.Report(new("正在删除文件…", null));
            var deleted = 0;
            foreach (var target in targets)
            {
                if (!File.Exists(target)) continue;
                File.Delete(target);
                deleted++;
            }
            PruneEmptyDirectories(userRoot, targets);

            return new SchemaUninstallReport(deleted, backupPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }

    private async Task<string> DownloadAsync(
        SchemaPackage package, string targetPath, IProgress<SchemaInstallProgress>? progress, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(package.ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"网络错误，无法访问 {package.ArchiveUrl}：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"下载超时：{package.ArchiveUrl}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"下载失败：HTTP {(int)response.StatusCode}（{package.ArchiveUrl}）");

            var total = response.Content.Headers.ContentLength;
            await using var network = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(targetPath);
            var buffer = new byte[64 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await network.ReadAsync(buffer, ct)) > 0)
            {
                downloaded += read;
                if (downloaded > MaxArchiveBytes)
                    throw new InvalidOperationException("包体积超出安全上限（512 MB），已中止。");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                if (total is > 0)
                    progress?.Report(new($"正在下载 {package.Name}…", Math.Min(100, downloaded * 100.0 / total.Value)));
            }
        }
        return targetPath;
    }

    /// <summary>
    /// Unpacks the archive's RIME-relevant files into <paramref name="staging"/>,
    /// stripping GitHub's <c>{repo}-{branch}/</c> root folder. Nothing lands in
    /// the user directory yet — staging is validated first.
    /// </summary>
    private static async Task<List<string>> ExtractAsync(
        SchemaPackage package, string archivePath, string staging, CancellationToken ct)
    {
        var skipped = new List<string>();
        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        string? rootPrefix = null;
        while (await tar.GetNextEntryAsync(copyData: false, ct) is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile) continue;

            var name = entry.Name.Replace('\\', '/');
            var firstSlash = name.IndexOf('/');
            if (firstSlash <= 0)
            {
                skipped.Add($"{name}（缺少仓库根目录，已跳过）");
                continue;
            }
            // GitHub tarballs nest everything under one root; entries outside
            // it would mean a hand-crafted archive.
            rootPrefix ??= name[..firstSlash];
            if (!name.StartsWith(rootPrefix + "/", StringComparison.Ordinal))
            {
                skipped.Add($"{name}（路径不在仓库根目录下，已跳过）");
                continue;
            }

            var relative = name[(firstSlash + 1)..];
            if (relative.Length == 0) continue;
            if (package.IsExcluded(relative)) continue;

            var targetPath = SafeCombine(staging, relative);
            if (targetPath is null)
            {
                skipped.Add($"{relative}（路径越出安装范围，已跳过）");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using (var output = File.Create(targetPath))
                await entry.DataStream!.CopyToAsync(output, ct);
        }
        return skipped;
    }

    /// <summary>Resolves an entry name under the root, rejecting escapes (tar slip).</summary>
    private static string? SafeCombine(string root, string relative)
    {
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relative));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison) ? candidate : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasSchemaFile(string staging)
        => Directory.EnumerateFiles(staging, "*.schema.yaml", SearchOption.AllDirectories).Any();

    private static bool HasFiles(string dir)
        => Directory.EnumerateFiles(dir, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        }).Any();

    private static string PreInstallBackupPath(string packageId)
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erya", "backups", $"pre-install-{packageId}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    private static string PreUninstallBackupPath(string packageId)
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erya", "backups", $"pre-uninstall-{packageId}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    /// <summary>
    /// Removes directories the deletions left empty — deepest first, the user
    /// directory itself always stays. Ancestors already holding other files
    /// are skipped by the emptiness check.
    /// </summary>
    private static void PruneEmptyDirectories(string userRoot, IEnumerable<string> deletedPaths)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in deletedPaths)
        {
            var current = Path.GetDirectoryName(path);
            while (current is not null && current.Length > userRoot.Length)
            {
                candidates.Add(current);
                current = Path.GetDirectoryName(current);
            }
        }

        foreach (var dir in candidates.OrderByDescending(path => path.Length))
        {
            if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) continue;
            try { Directory.Delete(dir); }
            catch (IOException) { /* raced to non-empty — fine */ }
        }
    }

    private static (int Files, long Bytes) CopyAll(string staging, string userDir)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging));
        int files = 0;
        long bytes = 0;
        foreach (var source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, source);
            var target = Path.Combine(userDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = File.OpenRead(source);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
            input.CopyTo(output);
            files++;
            bytes += input.Length;
        }
        return (files, bytes);
    }
}
