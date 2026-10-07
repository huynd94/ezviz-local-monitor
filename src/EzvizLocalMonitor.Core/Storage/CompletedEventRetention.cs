namespace EzvizLocalMonitor.Services;

public sealed class CompletedEventRetention(AppPaths paths, EventStore store, ActiveEventFiles files, IAppLogger logger)
{
    /// <summary>
    /// Caller serializes retention with runtime transitions and other maintenance.
    /// Leases exclude cooperating in-process producers/readers. Path validation is
    /// deliberately conservative, but is not an atomic openat/unlinkat sandbox:
    /// the state tree must be owned by the service and not concurrently mutated
    /// by external writers. Symlink replacement/hard-link races remain outside
    /// this ownership boundary; filesystem deletion and SQLite are not atomic.
    /// </summary>
    public async Task<CleanupResult> RunAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var deleted = 0;
        var failed = 0;
        long freed = 0;
        long cursor = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = store.Before(cutoff, 500, cursor);
            if (batch.Count == 0) break;
            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                cursor = item.Id;
                if (DeliveryStatusPolicy.IsPending(item.DeliveryStatus)) continue;
                try
                {
                    if (string.IsNullOrWhiteSpace(item.ImagePath)) throw new IOException("Invalid event image path.");
                    var image = Path.GetFullPath(item.ImagePath);
                    var before = Path.Combine(Path.GetDirectoryName(image)!, Path.GetFileNameWithoutExtension(image) + "_before.jpg");
                    ValidatePath(image);
                    ValidatePath(before);

                    // Obtain the entire pair before touching either file. Roll back
                    // the first lease if admission for the second is denied.
                    using var imageLease = files.TryBeginDelete(image);
                    if (imageLease is null) continue;
                    using var beforeLease = image == before ? null : files.TryBeginDelete(before);
                    if (image != before && beforeLease is null) continue;
                    if (store.HasOtherImageReference(item.Id, image, before)) continue;

                    // Recheck under our leases. External writers are excluded by
                    // the service-owned filesystem boundary described above.
                    ValidatePath(image);
                    ValidatePath(before);
                    ct.ThrowIfCancellationRequested();
                    freed += DeleteImage(image);
                    if (image != before) freed += DeleteImage(before);
                    if (store.DeleteById(item.Id)) deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Microsoft.Data.Sqlite.SqliteException)
                {
                    failed++;
                    // Avoid path/exception text which may contain sensitive data.
                    logger.Error(LogChannel.App, $"Retention retained event {item.Id}: {ex.GetType().Name}.");
                }
            }
            // Yield between batches so shutdown can request cancellation promptly.
            await Task.Yield();
        }
        var result = new CleanupResult(deleted, 0, freed, failed);
        logger.Info(LogChannel.App, $"Retention: deletedEvents={deleted}, freedBytes={freed}, failedFiles={failed}.");
        return result;
    }

    private void ValidatePath(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.EventImages));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Event image is outside Events root.");

        // Include Events, state root and every ancestor, not just the leaf.
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget is not null) throw new IOException("Symbolic event path rejected.");
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse event path rejected.");
                if (current == path && (attributes & FileAttributes.Directory) != 0)
                    throw new IOException("Event image is a directory.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static long DeleteImage(string path)
    {
        // File.Exists suppresses access errors; only genuine ENOENT is safe.
        long bytes;
        try { bytes = new FileInfo(path).Length; }
        catch (FileNotFoundException) { return 0; }
        catch (DirectoryNotFoundException) { return 0; }
        File.Delete(path);
        return bytes;
    }
}
