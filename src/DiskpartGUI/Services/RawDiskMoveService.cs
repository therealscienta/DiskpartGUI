using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using DiskpartGUI.Interop;
using DiskpartGUI.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskpartGUI.Services;

/// <summary>
/// A move or extend that failed. <see cref="Exception.Message"/> is written for the user and says
/// what state the disk was left in.
/// </summary>
public sealed class PartitionOperationException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class RawDiskMoveService : IPartitionMoveService
{
    // Large chunks cut the number of seeks between source and destination on spinning disks.
    private const int ChunkSize = 32 * 1024 * 1024; // 32 MB

    // Progress<T> posts every report to the UI thread; cap it so fast disks don't flood it.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

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

        // Never offer the first MiB (MBR / GPT header and entries) or the backup GPT at the end.
        long cursor = DiskAlignment.LeadingReserveBytes;
        foreach (var p in sorted)
        {
            AddIfLargeEnough(regions, cursor, p.StartingOffset - cursor, partitionSizeBytes);
            cursor = Math.Max(cursor, p.StartingOffset + p.SizeBytes);
        }

        // Gap after the last partition
        long usableEnd = DiskAlignment.UsableEnd(disk.SizeBytes);
        AddIfLargeEnough(regions, cursor, usableEnd - cursor, partitionSizeBytes);

        return regions;
    }

    private static void AddIfLargeEnough(
        List<FreeSpaceRegion> list, long start, long size, long minSize)
    {
        if (size > 0 && size >= minSize)
            list.Add(new FreeSpaceRegion(start, size));
    }

    // ── Extend operation ──────────────────────────────────────────────────────

    public Task ExtendPartitionRawAsync(
        int diskNumber, long partitionStartOffset, long growByBytes,
        string? driveLetter, CancellationToken ct = default)
        => Task.Run(() => ExecuteExtend(diskNumber, partitionStartOffset, growByBytes, driveLetter), ct);

    private static void ExecuteExtend(
        int diskNumber, long partitionStartOffset, long growByBytes, string? driveLetter)
    {
        // Check the filesystem *before* touching the partition table: growing a partition whose
        // filesystem can't follow just strands the new space.
        if (driveLetter is null)
            throw new PartitionOperationException(
                "Extend needs the volume to have a drive letter. Nothing was changed.");
        try
        {
            using var volume = NativeDisk.OpenVolume(driveLetter);
            NativeDisk.GetNtfsVolumeData(volume);
        }
        catch (Win32Exception ex)
        {
            throw new PartitionOperationException(
                $"Only NTFS volumes can be extended ({ex.Message}). Nothing was changed.", ex);
        }

        // Grow the partition table entry directly via partmgr (synchronous, bypasses VDS).
        using (var diskHandle = NativeDisk.OpenDisk(diskNumber, readWrite: true))
        {
            var layout = NativeDisk.GetDriveLayout(diskHandle);
            uint partitionNumber = FindEntry(layout, partitionStartOffset) is int i
                ? NativeDisk.ReadEntryPartitionNumber(layout, i)
                : 0;

            if (partitionNumber == 0)
                throw new PartitionOperationException(
                    "The partition was not found in the disk's partition table. Refresh and try again. Nothing was changed.");

            NativeDisk.GrowPartition(diskHandle, partitionNumber, growByBytes);
        }

        // Extend the NTFS filesystem to fill the newly grown partition.
        try
        {
            ExtendNtfsWithRetry(driveLetter);
        }
        catch (Exception ex)
        {
            throw new PartitionOperationException(
                "The partition was grown, but its NTFS file system could not be extended to fill it " +
                $"({ex.Message}). To finish, run diskpart, select this volume, and run \"extend filesystem\".", ex);
        }
    }

    private static void ExtendNtfsWithRetry(string driveLetter)
    {
        // GrowPartition triggers an async PnP re-enumeration, so the volume may be briefly
        // inaccessible or still report its old size. Try at once, then back off (~3.5 s total).
        int[] delaysMs = [0, 300, 600, 800, 800, 1000];
        Exception? last = null;
        foreach (var delay in delaysMs)
        {
            if (delay > 0) Thread.Sleep(delay);
            try
            {
                using var volumeHandle = NativeDisk.OpenVolume(driveLetter);
                NativeDisk.ExtendNtfsToFillVolume(volumeHandle);
                return;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException) { last = ex; }
        }
        throw last!;
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
        const string Unchanged = "Nothing was changed.";

        // 1. Check the move against the disk's *live* partition table before writing anything.
        //    The UI's free-space list comes from WMI, which can be stale or incomplete.
        SafeFileHandle? diskHandle = null;
        string? error;
        try
        {
            diskHandle = NativeDisk.OpenDisk(diskNumber, readWrite: true);
            error = ValidateMove(
                NativeDisk.GetDriveLayout(diskHandle), NativeDisk.GetLength(diskHandle),
                sourceOffsetBytes, destinationOffsetBytes, sizeBytes);
        }
        catch (Win32Exception ex)
        {
            diskHandle?.Dispose();
            throw new PartitionOperationException($"Could not read the disk's partition table ({ex.Message}). {Unchanged}", ex);
        }
        using var _ = diskHandle;
        if (error is not null)
            throw new PartitionOperationException($"{error} {Unchanged}");

        // 2. Lock + dismount the volume in this partition, found by its disk extents so that
        //    volumes without a drive letter (or mounted in a folder) are locked too.
        SafeFileHandle? volumeHandle = null;
        try
        {
            var volumePath = NativeDisk.FindVolumeDevicePath(diskNumber, sourceOffsetBytes);
            try
            {
                volumeHandle = volumePath is not null ? NativeDisk.OpenVolumeDevice(volumePath)
                             : driveLetter is not null ? NativeDisk.OpenVolume(driveLetter)
                             : null;
                if (volumeHandle is not null)
                {
                    NativeDisk.IoctlSimple(volumeHandle, NativeDisk.FSCTL_LOCK_VOLUME,
                        "Cannot lock the volume. Close any programs or windows using this partition and retry.");
                    NativeDisk.IoctlSimple(volumeHandle, NativeDisk.FSCTL_DISMOUNT_VOLUME,
                        "Cannot dismount the volume.");
                }
            }
            catch (Win32Exception ex)
            {
                throw new PartitionOperationException($"{ex.Message} {Unchanged}", ex);
            }

            // 3. Copy all sectors. The destination never overlaps the source (checked above), so
            //    the source stays intact if this fails or is cancelled.
            try
            {
                CopySectors(diskHandle, sourceOffsetBytes, destinationOffsetBytes, sizeBytes, progress, ct);

                // Make sure the copy is on the disk, not in a write cache, before the partition
                // table is switched over to it.
                NativeDisk.Flush(diskHandle);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new PartitionOperationException(
                    $"Copying failed ({ex.Message}). The partition was not moved and its data is unchanged; " +
                    "only unallocated space was written to.", ex);
            }

            // 4. Point the partition table entry at the copy.
            try
            {
                UpdatePartitionTableEntry(diskHandle, sourceOffsetBytes, destinationOffsetBytes);
            }
            catch (Exception ex)
            {
                throw new PartitionOperationException(
                    $"All data was copied, but the partition table could not be updated ({ex.Message}). " +
                    "The partition still points to its original location, whose data is unchanged.", ex);
            }
        }
        finally
        {
            // 5. Unlock the volume regardless of outcome
            if (volumeHandle is not null)
            {
                try { NativeDisk.IoctlSimple(volumeHandle, NativeDisk.FSCTL_UNLOCK_VOLUME, "Unlock"); }
                catch { /* best-effort */ }
                volumeHandle.Dispose();
            }
        }
    }

    /// <summary>
    /// Checks a move against the disk's live partition table. Returns null if it is safe, or a
    /// message saying why not. Guarantees, when null: the source partition is exactly where the
    /// UI thinks it is, the destination is MiB-aligned, lies inside the area partitions may
    /// use, and overlaps neither the source nor any other partition (so a failed or cancelled
    /// copy only ever writes to unallocated space).
    /// </summary>
    internal static string? ValidateMove(
        byte[] layout, long diskLengthBytes, long sourceOffset, long destinationOffset, long sizeBytes)
    {
        const string Refresh = "The partition list is out of date; click Refresh and try again.";

        if (sizeBytes <= 0)
            return "The partition size is invalid.";
        if (destinationOffset == sourceOffset)
            return "The destination is the partition's current position.";
        if (destinationOffset % DiskAlignment.MiB != 0)
            return "The destination is not aligned to 1 MiB.";

        var style = NativeDisk.GetPartitionStyle(layout);
        long usableStart, usableEnd;
        if (style == NativeDisk.PARTITION_STYLE_GPT)
        {
            var (start, length) = NativeDisk.GetGptUsableRange(layout);
            (usableStart, usableEnd) = (start, start + length);
        }
        else if (style == NativeDisk.PARTITION_STYLE_MBR)
        {
            (usableStart, usableEnd) = (DiskAlignment.LeadingReserveBytes, diskLengthBytes);
        }
        else
        {
            return "The disk has no partition table.";
        }

        long destinationEnd = destinationOffset + sizeBytes;
        if (destinationOffset < usableStart || destinationEnd > usableEnd)
            return "The destination is outside the area of the disk partitions may use.";

        if (FindEntry(layout, sourceOffset) is not int sourceIndex ||
            NativeDisk.ReadEntryLength(layout, sourceIndex) != sizeBytes)
            return Refresh;

        if (Overlaps(destinationOffset, destinationEnd, sourceOffset, sourceOffset + sizeBytes))
            return "The destination overlaps the partition itself.";

        int count = NativeDisk.GetPartitionCount(layout);
        for (int i = 0; i < count; i++)
        {
            if (i == sourceIndex) continue;
            long start = NativeDisk.ReadEntryStartingOffset(layout, i);
            long length = NativeDisk.ReadEntryLength(layout, i);
            if (length <= 0) continue; // unused MBR slot

            // Includes MBR extended containers, so logical partitions can't be moved out of
            // (or into) one and corrupt the EBR chain.
            if (Overlaps(destinationOffset, destinationEnd, start, start + length))
                return $"The destination overlaps another partition (at {start / DiskAlignment.MiB:N0} MiB). {Refresh}";
            if (Overlaps(sourceOffset, sourceOffset + sizeBytes, start, start + length))
                return "Partitions inside an MBR extended partition can't be moved.";
        }

        return null;
    }

    private static bool Overlaps(long aStart, long aEnd, long bStart, long bEnd)
        => aStart < bEnd && bStart < aEnd;

    private static int? FindEntry(byte[] layout, long startingOffset)
    {
        int count = NativeDisk.GetPartitionCount(layout);
        for (int i = 0; i < count; i++)
        {
            if (NativeDisk.ReadEntryLength(layout, i) > 0 &&
                NativeDisk.ReadEntryStartingOffset(layout, i) == startingOffset)
                return i;
        }
        return null;
    }

    // ── Sector copy ───────────────────────────────────────────────────────────

    private static void CopySectors(
        SafeFileHandle disk,
        long srcOffset, long dstOffset, long totalBytes,
        IProgress<MoveProgress> progress, CancellationToken ct)
    {
        var buf = new byte[(int)Math.Min(ChunkSize, totalBytes)];
        long bytesCopied = 0;
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        void Report()
        {
            if (bytesCopied < totalBytes && sw.Elapsed - lastReport < ProgressInterval) return;
            lastReport = sw.Elapsed;
            ReportProgress(progress, bytesCopied, totalBytes, sw);
        }

        if (dstOffset < srcOffset)
        {
            // Moving LEFT — copy forward (low to high addresses)
            for (long pos = 0; pos < totalBytes; pos += buf.Length)
            {
                ct.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buf.Length, totalBytes - pos);
                ReadChunk(disk, srcOffset + pos, buf, count);
                WriteChunk(disk, dstOffset + pos, buf, count);
                bytesCopied += count;
                Report();
            }
        }
        else
        {
            // Moving RIGHT — copy backward (high to low addresses) to avoid overwriting
            long remaining = totalBytes;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buf.Length, remaining);
                remaining -= count;
                ReadChunk(disk, srcOffset + remaining, buf, count);
                WriteChunk(disk, dstOffset + remaining, buf, count);
                bytesCopied += count;
                Report();
            }
        }
    }

    private static void ReadChunk(SafeFileHandle disk, long offset, byte[] buf, int count)
    {
        NativeDisk.SeekTo(disk, offset);
        NativeDisk.ReadBytes(disk, buf, count);
    }

    private static void WriteChunk(SafeFileHandle disk, long offset, byte[] buf, int count)
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

    private static void UpdatePartitionTableEntry(SafeFileHandle disk, long oldOffset, long newOffset)
    {
        var layout = NativeDisk.GetDriveLayout(disk);

        if (FindEntry(layout, oldOffset) is not int i)
            throw new InvalidOperationException(
                $"Could not find partition with offset {oldOffset} in the drive layout.");

        NativeDisk.WriteEntryStartingOffset(layout, i, newOffset);
        NativeDisk.SetEntryRewritePartition(layout, i);
        NativeDisk.SetDriveLayout(disk, layout);

        // Notify partmgr/VDS about the changed layout so that a subsequent
        // diskpart "extend" sees the new free space immediately.
        try { NativeDisk.IoctlSimple(disk, NativeDisk.IOCTL_DISK_UPDATE_PROPERTIES,
                  "IOCTL_DISK_UPDATE_PROPERTIES"); }
        catch { /* best-effort — rescan will handle it if this fails */ }
    }
}
