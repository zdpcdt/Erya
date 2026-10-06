using System.IO.Compression;
using Erya.Core.Backup;

namespace Erya.Core.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly string _root;
    private readonly string _userDir;

    public BackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        _userDir = Path.Combine(_root, "Rime");
        Directory.CreateDirectory(_userDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_userDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Packs the user dir into a zip that lives outside of it (in _root).</summary>
    private string ZipUserDir(string zipName)
    {
        var zipPath = Path.Combine(_root, zipName);
        RimeBackup.Create(_userDir, zipPath);
        return zipPath;
    }

    private static HashSet<string> EntryNames(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(e => e.FullName).ToHashSet();
    }

    private static string ExtractText(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.Single(e => e.FullName == entryName);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    // ---------- backup ----------

    [Fact]
    public void Create_packs_user_files_but_not_the_build_dir()
    {
        Write("default.custom.yaml", "patch:\n  schema_list:\n    - schema: rime_ice\n");
        Write("wubi86.userdb/0001", "dict");
        Write("build/luna_pinyin.schema.yaml", "derived");

        var report = RimeBackup.Create(_userDir, Path.Combine(_root, "backup.zip"));

        Assert.Equal(2, report.FileCount);
        Assert.Empty(report.SkippedFiles);
        var names = EntryNames(Path.Combine(_root, "backup.zip"));
        Assert.Contains("default.custom.yaml", names);
        Assert.Contains("wubi86.userdb/0001", names);
        Assert.DoesNotContain(names, n => n.StartsWith("build/"));
    }

    [Fact]
    public void Create_skips_locked_files_and_reports_them()
    {
        Write("default.custom.yaml", "patch: {}");
        var locked = Write("rime.log", "in use");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = RimeBackup.Create(_userDir, Path.Combine(_root, "backup.zip"));

            Assert.Equal(1, report.FileCount);
            Assert.True(report.HasSkippedFiles);
            Assert.Contains(report.SkippedFiles, s => s.StartsWith("rime.log"));
        }
    }

    [Fact]
    public void Create_overwrites_an_existing_target_zip()
    {
        Write("default.custom.yaml", "patch: {}");
        var zipPath = Path.Combine(_root, "backup.zip");
        RimeBackup.Create(_userDir, zipPath);
        Write("extra.yaml", "added later");

        RimeBackup.Create(_userDir, zipPath);

        Assert.Contains("extra.yaml", EntryNames(zipPath));
    }

    [Fact]
    public void Create_throws_when_user_dir_is_missing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => RimeBackup.Create(Path.Combine(_root, "nope"), Path.Combine(_root, "b.zip")));
    }

    // ---------- restore ----------

    [Fact]
    public void Restore_recreates_files_in_a_fresh_directory()
    {
        Write("default.custom.yaml", "patch:\n  menu/page_size: 7\n");
        Write("wubi86.userdb/0001", "dict");
        var zipPath = ZipUserDir("backup.zip");

        var target = Path.Combine(_root, "fresh");
        var report = RimeBackup.Restore(zipPath, target);

        Assert.Equal(2, report.FileCount);
        Assert.Null(report.SafetyBackupPath); // nothing to snapshot in an empty dir
        Assert.Equal("patch:\n  menu/page_size: 7\n", File.ReadAllText(Path.Combine(target, "default.custom.yaml")));
        Assert.Equal("dict", File.ReadAllText(Path.Combine(target, "wubi86.userdb", "0001")));
    }

    [Fact]
    public void Restore_overwrites_and_snapshots_previous_state_next_to_the_backup()
    {
        Write("default.custom.yaml", "old content");
        var zipPath = ZipUserDir("backup.zip");

        var target = Path.Combine(_root, "restore-here");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "default.custom.yaml"), "current state");

        var report = RimeBackup.Restore(zipPath, target);

        Assert.Equal("old content", File.ReadAllText(Path.Combine(target, "default.custom.yaml")));
        Assert.NotNull(report.SafetyBackupPath);
        var safetyZip = Assert.Single(Directory.GetFiles(_root, "pre-restore-*.zip"));
        Assert.Equal("current state", ExtractText(safetyZip, "default.custom.yaml"));
    }

    [Fact]
    public void Restore_skips_zip_slip_entries_and_writes_nothing_outside_user_dir()
    {
        var zipPath = Path.Combine(_root, "evil.zip");
        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Write("ok.yaml", "fine"), "ok.yaml");
            var evil = archive.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(evil.Open());
            writer.Write("should not land here");
        }

        var report = RimeBackup.Restore(zipPath, _userDir);

        Assert.Equal(1, report.FileCount);
        Assert.Contains(report.SkippedFiles, s => s.Contains("escape.txt"));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
        Assert.Equal("fine", File.ReadAllText(Path.Combine(_userDir, "ok.yaml")));
    }

    [Fact]
    public void Restore_throws_on_a_corrupt_zip()
    {
        var path = Path.Combine(_root, "garbage.zip");
        File.WriteAllText(path, "not a zip");

        Assert.ThrowsAny<InvalidDataException>(() => RimeBackup.Restore(path, _userDir));
    }

    [Fact]
    public void Restore_throws_when_backup_file_is_missing()
        => Assert.Throws<FileNotFoundException>(
            () => RimeBackup.Restore(Path.Combine(_root, "missing.zip"), _userDir));

    [Fact]
    public void Restore_throws_on_an_archive_without_entries()
    {
        var zipPath = Path.Combine(_root, "empty.zip");
        using (var stream = File.Create(zipPath))
        using (new ZipArchive(stream, ZipArchiveMode.Create)) { }

        Assert.Throws<InvalidDataException>(() => RimeBackup.Restore(zipPath, _userDir));
    }
}
