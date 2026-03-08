using DiskpartGUI.Models;

namespace DiskpartGUI.Services;

public interface IPartitionMoveService
{
    /// <summary>
    /// Returns the contiguous free-space regions on <paramref name="disk"/> that are
    /// large enough to hold a partition of <paramref name="partitionSizeBytes"/> bytes.
    /// Pure calculation — no I/O.
    /// </summary>
    IReadOnlyList<FreeSpaceRegion> GetFreeSpaceRegions(
        DiskInfo disk,
        IReadOnlyList<PartitionInfo> partitions,
        long partitionSizeBytes);

    /// <summary>
    /// Physically moves all sector data from <paramref name="sourceOffsetBytes"/> to
    /// <paramref name="destinationOffsetBytes"/>, then updates the partition table entry.
    /// Safe to cancel during the copy phase — cancellation leaves the source partition intact.
    /// </summary>
    Task MovePartitionAsync(
        int diskNumber,
        string? driveLetter,
        long sourceOffsetBytes,
        long destinationOffsetBytes,
        long sizeBytes,
        IProgress<MoveProgress> progress,
        CancellationToken ct);

    /// <summary>
    /// Grows a partition by <paramref name="growByBytes"/> using raw Win32 IOCTLs, bypassing
    /// VDS entirely (VDS caches stale layouts after raw partition-table moves).
    /// Uses IOCTL_DISK_GROW_PARTITION to extend the partition entry (synchronous, goes directly
    /// to partmgr), then FSCTL_EXTEND_VOLUME to extend the NTFS filesystem.
    /// If the partition has no drive letter the filesystem step is skipped.
    /// </summary>
    Task ExtendPartitionRawAsync(
        int diskNumber,
        long partitionStartOffset,
        long growByBytes,
        string? driveLetter,
        CancellationToken ct = default);
}
