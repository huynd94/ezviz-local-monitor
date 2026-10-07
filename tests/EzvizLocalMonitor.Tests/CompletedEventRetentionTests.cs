using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class CompletedEventRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "retention-" + Guid.NewGuid());
    private readonly AppPaths _paths;
    private readonly EventStore _store;
    private readonly ActiveEventFiles _files = new();
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
    public CompletedEventRetentionTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "state"), Path.Combine(_root, "model"));
        _store = new EventStore(_paths);
        _store.Initialize();
    }

    [Fact]
    public async Task DeletesOldCompletedPairButKeepsBoundaryRecentOrphansAndLogs()
    {
        var old = Add("old.jpg");
        File.WriteAllBytes(Before(old.ImagePath), [4, 5]);
        var boundary = Add("boundary.jpg", at: Cutoff);
        var recent = Add("recent.jpg", at: Cutoff.AddMinutes(1));
        var orphan = Path.Combine(_paths.EventImages, "orphan.jpg");
        File.WriteAllText(orphan, "orphan");
        File.WriteAllText(_paths.AppLogFile, "log");
        var result = await Run();
        Assert.Equal(new CleanupResult(1, 0, 5, 0), result);
        Assert.False(File.Exists(old.ImagePath));
        Assert.False(File.Exists(Before(old.ImagePath)));
        Assert.Equal(new[] { boundary.Id, recent.Id }.Order(), _store.Recent().Select(x => x.Id).Order());
        Assert.True(File.Exists(orphan));
        Assert.Equal("log", File.ReadAllText(_paths.AppLogFile));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Chưa gửi")]
    [InlineData("Chưa gửi (đã dừng)")]
    [InlineData("Chưa gửi (hàng đợi đã dừng)")]
    [InlineData("Chưa gửi (worker lỗi: fixture)")]
    [InlineData("Cảnh báo: hàng đợi đã dừng an toàn")]
    [InlineData("Cảnh báo: hàng đợi đầy, sự kiện chưa được gửi")]
    [InlineData("Cảnh báo: worker lỗi đã được cô lập (fixture)")]
    [InlineData("Cảnh báo: thao tác đã hủy hoặc quá thời gian")]
    [InlineData("Telegram: đã gửi | Zalo: chưa gửi")]
    [InlineData("Telegram: đã gửi but unfinished")]
    [InlineData("Unknown future delivery state")]
    [InlineData("Cảnh báo: lần gửi 1 lỗi (fixture)")]
    public async Task UnfinishedDeliveryIsRetained(string? status)
    {
        Assert.True(DeliveryStatusPolicy.IsPending(status));
        var item = Add("pending.jpg", status ?? "");
        Assert.Equal(0, (await Run()).DeletedEvents);
        Assert.True(File.Exists(item.ImagePath));
        Assert.Single(_store.Recent());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EitherImageLeaseProtectsBothImagesAndRow(bool before)
    {
        var item = Add("leased.jpg");
        File.WriteAllText(Before(item.ImagePath), "before");
        using (_files.Acquire(before ? Before(item.ImagePath) : item.ImagePath))
        {
            Assert.Equal(0, (await Run()).DeletedEvents);
            Assert.True(File.Exists(item.ImagePath));
            Assert.True(File.Exists(Before(item.ImagePath)));
            Assert.Single(_store.Recent());
            Assert.False(_files.IsInUse(before ? item.ImagePath : Before(item.ImagePath)));
        }
        Assert.Equal(1, (await Run()).DeletedEvents);
    }

    [Fact]
    public async Task KeysetProgressesPastFiveHundredPendingAndLeasedRows()
    {
        var leased = Add("leased.jpg");
        using var lease = _files.Acquire(leased.ImagePath);
        for (var i = 0; i < 501; i++) Add("pending.jpg", "Chưa gửi");
        var completed = Add("completed.jpg");
        Assert.Equal(1, (await Run()).DeletedEvents);
        Assert.False(File.Exists(completed.ImagePath));
        Assert.Equal(502, _store.Recent(1000).Count);
    }

    [Fact]
    public async Task CutoffComparesInstantsAcrossOffsetsAndDeleteTargetsOnlyId()
    {
        var old = Add("offset-old.jpg", at: DateTimeOffset.Parse("2026-09-01T01:00:00+02:00"));
        var newer = Add("offset-new.jpg", at: DateTimeOffset.Parse("2026-08-31T23:30:00-02:00"));
        Assert.Equal(old.Id, Assert.Single(_store.Before(Cutoff)).Id);
        Assert.Equal(1, (await Run()).DeletedEvents);
        Assert.Equal(newer.Id, Assert.Single(_store.Recent()).Id);
        Assert.False(_store.DeleteById(old.Id));
        Assert.True(_store.DeleteById(newer.Id));
    }

    [Fact]
    public async Task MissingImagesAllowRowRemovalButDirectoryFailureRetainsRow()
    {
        var missing = Add("missing.jpg");
        File.Delete(missing.ImagePath);
        var failure = Add("failure.jpg");
        File.Delete(failure.ImagePath);
        Directory.CreateDirectory(failure.ImagePath);
        var result = await Run();
        Assert.Equal(1, result.DeletedEvents);
        Assert.Equal(1, result.FailedFiles);
        Assert.Equal(failure.Id, Assert.Single(_store.Recent()).Id);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("before")]
    [InlineData("other-before")]
    [InlineData("normalized")]
    public async Task SharedImageIncludingBeforeAliasIsNeverDeleted(string alias)
    {
        var old = Add(alias == "other-before" ? "shared_before.jpg" : "shared.jpg");
        File.WriteAllText(Before(old.ImagePath), "shared before");
        Add(alias switch
        {
            "before" => "shared_before.jpg",
            "other-before" => "shared.jpg",
            "normalized" => "./shared.jpg",
            _ => "shared.jpg"
        }, "Chưa gửi");
        Assert.Equal(0, (await Run()).DeletedEvents);
        Assert.True(File.Exists(old.ImagePath));
        Assert.True(File.Exists(Before(old.ImagePath)));
    }

    [Theory]
    [InlineData("Không có kênh nào được bật")]
    [InlineData("Không gửi: AI không thấy chuyển động/người")]
    [InlineData("Không có cấu hình cảnh báo")]
    [InlineData("Telegram: đã gửi (idempotent)")]
    [InlineData("Telegram: đã gửi | Zalo: đã gửi + Zalo ảnh: chưa gửi; API yêu cầu URL HTTPS công khai cho photo")]
    [InlineData("Cảnh báo: lần gửi 3 lỗi (fixture)")]
    [InlineData("Telegram: lỗi HTTP 500")]
    public async Task TerminalDeliveryIncludingExhaustedRetriesAllowsCleanup(string status)
    {
        Add("terminal.jpg", status);
        Assert.False(DeliveryStatusPolicy.IsPending(status));
        Assert.Equal(1, (await Run()).DeletedEvents);
        Assert.Empty(_store.Recent());
    }

    [Fact]
    public async Task InvalidBeforeImagePreventsDeletingOtherwiseValidMainImage()
    {
        var item = Add("pair.jpg");
        Directory.CreateDirectory(Before(item.ImagePath));
        var result = await Run();
        Assert.Equal(0, result.DeletedEvents);
        Assert.Equal(1, result.FailedFiles);
        Assert.True(File.Exists(item.ImagePath));
        Assert.False(_files.IsInUse(item.ImagePath));
        Assert.False(_files.IsInUse(Before(item.ImagePath)));
        Assert.Single(_store.Recent());
    }

    [LinuxFact]
    public async Task ActualFilesystemDeleteDenialRetainsPairAndAllowsRetry()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("This fixture requires Linux.");
        var directory = Path.Combine(_paths.EventImages, "permission-fixture");
        Directory.CreateDirectory(directory);
        var item = Add("permission-fixture/denied.jpg");
        File.WriteAllText(Before(item.ImagePath), "before");
        var originalMode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var result = await Run();
            Assert.Equal(new CleanupResult(0, 0, 0, 1), result);
            Assert.True(File.Exists(item.ImagePath));
            Assert.True(File.Exists(Before(item.ImagePath)));
            Assert.Single(_store.Recent());
            Assert.False(_files.IsInUse(item.ImagePath));
            Assert.False(_files.IsInUse(Before(item.ImagePath)));
        }
        finally { File.SetUnixFileMode(directory, originalMode); }
        Assert.Equal(1, (await Run()).DeletedEvents);
    }

    [LinuxTheory]
    [InlineData("outside")]
    [InlineData("leaf")]
    [InlineData("before")]
    [InlineData("parent")]
    [InlineData("events")]
    [InlineData("root")]
    [InlineData("ancestor")]
    public async Task RejectsOutsidePathsAndSymlinksAtEveryBoundary(string kind)
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var target = Path.Combine(outside, "target.jpg");
        File.WriteAllText(target, "protected");
        var item = Add("unsafe.jpg");
        if (kind == "outside") { item.ImagePath = target; _store.DeleteById(item.Id); item.Id = _store.Add(item); }
        if (kind == "leaf") { File.Delete(item.ImagePath); File.CreateSymbolicLink(item.ImagePath, target); }
        if (kind == "before") File.CreateSymbolicLink(Before(item.ImagePath), target);
        if (kind == "parent")
        {
            Directory.CreateSymbolicLink(Path.Combine(_paths.EventImages, "linked"), outside);
            item.ImagePath = Path.Combine(_paths.EventImages, "linked", "target.jpg");
            _store.DeleteById(item.Id); item.Id = _store.Add(item);
        }
        if (kind == "events")
        {
            Directory.Move(_paths.EventImages, Path.Combine(_root, "saved-events"));
            Directory.CreateSymbolicLink(_paths.EventImages, outside);
        }
        if (kind == "root" || kind == "ancestor")
        {
            var original = kind == "root" ? _paths.Root : _root;
            var moved = original + "-moved";
            Directory.Move(original, moved);
            Directory.CreateSymbolicLink(original, moved);
        }
        Assert.Equal(0, (await Run()).DeletedEvents);
        Assert.Single(_store.Recent());
        Assert.Equal("protected", File.ReadAllText(target));
    }

    private sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Linux permission/symlink fixture; exercised on Ubuntu 24.04."; }
    }
    private sealed class LinuxTheoryAttribute : TheoryAttribute
    {
        public LinuxTheoryAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Linux permission/symlink fixture; exercised on Ubuntu 24.04."; }
    }

    [Fact]
    public async Task CancellationBeforeRunLeavesImagesAndRows()
    {
        var item = Add("cancel.jpg");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(cancellation.Token));
        Assert.True(File.Exists(item.ImagePath));
        Assert.Single(_store.Recent());
    }

    private DetectionEvent Add(string name, string status = "Telegram: đã gửi", DateTimeOffset? at = null)
    {
        var item = new DetectionEvent { CameraId = Guid.NewGuid(), CameraName = "fixture", DetectedAt = at ?? Cutoff.AddDays(-1), ImagePath = Path.Combine(_paths.EventImages, name), DeliveryStatus = status };
        File.WriteAllBytes(item.ImagePath, [1, 2, 3]);
        item.Id = _store.Add(item);
        return item;
    }
    private static string Before(string path) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_before.jpg");
    private Task<CleanupResult> Run(CancellationToken ct = default) => new CompletedEventRetention(_paths, _store, _files, new Logger()).RunAsync(Cutoff, ct);
    private sealed class Logger : IAppLogger
    {
        public void Info(LogChannel channel, string message) { }
        public void Error(LogChannel channel, string message, Exception? exception = null) { }
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        // Remove only this fixture, unlinking any fixture-owned symlinks first.
        RemoveTree(_root);
        RemoveTree(_root + "-moved");
    }
    private static void RemoveTree(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null) { info.Delete(); return; }
        if (!info.Exists) return;
        foreach (var entry in info.EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null) entry.Delete();
            else if (entry is DirectoryInfo) RemoveTree(entry.FullName);
            else entry.Delete();
        }
        info.Delete();
    }
}
