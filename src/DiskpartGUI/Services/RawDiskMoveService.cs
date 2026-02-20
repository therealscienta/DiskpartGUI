using System.Diagnostics;
using DiskpartGUI.Interop;
using DiskpartGUI.Models;

namespace DiskpartGUI.Services;

public sealed class RawDiskMoveService : IPartitionMoveService
{
    private const int ChunkSize = 4 * 1024 * 1024; // 4 MB

    // ── Free space calculation ────────────────────────────────────────────────

    public IReadOnlyList<FreeSpaceRegion> GetFreeSpaceRegions(
        DiskInfo disk,
        IReadOnlyList<PartitionInfo> partitions,
        long partitionSizeBytes)
    {
        if (disk.SizeBytes <= 0) return [];

        // Sort partitions by starting offset, ignoring zero-size entries
        var sorted = partitions
            .Where(p => p.SizeBytes > 0)
            .OrderBy(p => p.StartingOffset)
            .ToList();

        var regions = new List<FreeSpaceRegion>();

        // Gap before the first partition (typically too small / reserved, but include if big enough)
        long cursor = 0L;
        foreach (var p in sorted)
        {
            long gapStart = cursor;
            long gapEnd   = p.StartingOffset;
            AddIfLargeEnough(regions, gapStart, gapEnd - gapStart, partitionSizeBytes);
            cursor = p.StartingOffset + p.SizeBytes;
        }

        // Gap after the last partition
        AddIfLargeEnough(regions, cursor, disk.SizeBytes - cursor, partitionSizeBytes);

        return regions;
    }

    private static void AddIfLargeEnough(
        List<FreeSpaceRegion> list, long start, long size, long minSize)
    {
        if (size >= minSize)
            list.Add(new FreeSpaceRegion(start, size));
    }

    // ── Move operation ────────────────────────────────────────────────────────

    public async Task MovePartitionAsync(
        int diskNumber,
        string? driveLetter,
        long sourceOffsetBytes,
        long destinationOffsetBytes,
        long sizeBytes,
        IProgress<MoveProgress> progress,
        CancellationToken ct)
    {
        await Task.Run(() => ExecuteMove(
            diskNumber, driveLetter,
            sourceOffsetBytes, destinationOffsetBytes, sizeBytes,
            progress, ct), ct);
    }

    private static void ExecuteMove(
        int diskNumber,
        string? driveLetter,
        long sourceOffsetBytes,
        long destinationOffsetBytes,
        long sizeBytes,
        IProgress<MoveProgress> progress,
        CancellationToken ct)
    {
        // 1. Lock + dismount the volume (if it has a drive letter)
        var volumeHandle = driveLetter is not null
            ? NativeDisk.OpenVolume(driveLetter)
            : null;

        try
        {
            if (volumeHandle is not null)
            {
                NativeDisk.IoctlSimple(volumeHandle, NativeDisk.FSCTL_LOCK_VOLUME,
                    "Cannot lock volume. Close any open files on this partition and retry.");
                NativeDisk.IoctlSimple(volumeHandle, NativeDisk.FSCTL_DISMOUNT_VOLUME,
                    "Cannot dismount volume.");
            }

            // 2. Open the physical disk
            using var diskHandle = NativeDisk.OpenDisk(diskNumber, readWrite: true);

            // 3. Copy all sectors (cancellable — source is still intact if cancelled)
            CopySectors(diskHandle, sourceOffsetBytes, destinationOffsetBytes, sizeBytes, progress, ct);

            // 4. Safe cancellation point — if ct was cancelled, CopySectors already threw.
            //    Reaching here means the copy completed successfully.

            // 5. Update partition table entry (cannot be safely cancelled — very fast)
            UpdatePartitionTableEntry(diskHandle, sourceOffsetBytes, destinationOffsetBytes);
        }
        finally
        {
            // 6. Unlock the volume regardless of outcome
            if (volumeHandle is not null)
            {
                try { NativeDisk.IoctlSimple(volumeHandle, NativeDisk.FSCTL_UNLOCK_VOLUME, "Unlock"); }
                catch { /* best-effort */ }
                volumeHandle.Dispose();
            }
        }
    }

    // ── Sector copy ───────────────────────────────────────────────────────────

    private static void CopySectors(
        Microsoft.Win32.SafeHandles.SafeFileHandle disk,
        long srcOffset, long dstOffset, long totalBytes,
        IProgress<MoveProgress> progress, CancellationToken ct)
    {
        var buf = new byte[ChunkSize];
        long bytesCopied = 0;
        var sw = Stopwatch.StartNew();

        if (dstOffset < srcOffset)
        {
            // Moving LEFT — copy forward (low to high addresses)
            for (long pos = 0; pos < totalBytes; pos += ChunkSize)
            {
                ct.ThrowIfCancellationRequested();
                var count = (int)Math.Min(ChunkSize, totalBytes - pos);
                ReadChunk(disk, srcOffset + pos, buf, count);
                WriteChunk(disk, dstOffset + pos, buf, count);
                bytesCopied += count;
                ReportProgress(progress, bytesCopied, totalBytes, sw);
            }
        }
        else
        {
            // Moving RIGHT — copy backward (high to low addresses) to avoid overwriting
            long remaining = totalBytes;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var count = (int)Math.Min(ChunkSize, remaining);
                remaining -= count;
                ReadChunk(disk, srcOffset + remaining, buf, count);
                WriteChunk(disk, dstOffset + remaining, buf, count);
                bytesCopied += count;
                ReportProgress(progress, bytesCopied, totalBytes, sw);
            }
        }
    }

    private static void ReadChunk(
        Microsoft.Win32.SafeHandles.SafeFileHandle disk, long offset, byte[] buf, int count)
    {
        NativeDisk.SeekTo(disk, offset);
        NativeDisk.ReadBytes(disk, buf, count);
    }

    private static void WriteChunk(
        Microsoft.Win32.SafeHandles.SafeFileHandle disk, long offset, byte[] buf, int count)
    {
        NativeDisk.SeekTo(disk, offset);
        NativeDisk.WriteBytes(disk, buf, count);
    }

    private static void ReportProgress(
        IProgress<MoveProgress> progress, long copied, long total, Stopwatch sw)
    {
        var elapsed = sw.Elapsed.TotalSeconds;
        var speed = elapsed > 0 ? copied / elapsed : 0;
        progress.Report(new MoveProgress(copied, total, speed));
    }

    // ── Partition table update ────────────────────────────────────────────────

    private static void UpdatePartitionTableEntry(
        Microsoft.Win32.SafeHandles.SafeFileHandle disk,
        long oldOffset, long newOffset)
    {
        var layout = NativeDisk.GetDriveLayout(disk);
        int count  = NativeDisk.GetPartitionCount(layout);

        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (NativeDisk.ReadEntryStartingOffset(layout, i) == oldOffset)
            {
                NativeDisk.WriteEntryStartingOffset(layout, i, newOffset);
                NativeDisk.SetEntryRewritePartition(layout, i);
                found = true;
                break;
            }
        }

        if (!found)
            throw new InvalidOperationException(
                $"Could not find partition with offset {oldOffset} in the drive layout.");

        NativeDisk.SetDriveLayout(disk, layout);
    }
}
