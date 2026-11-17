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
    /// The move is first checked against the live partition table; the destination must be
    /// MiB-aligned unallocated space that doesn't overlap the source, so cancelling or failing
    /// during the copy leaves the source partition intact.
    /// </summary>
    /// <exception cref="PartitionOperationException">The move was refused or failed; the message says what state the disk is in.</exception>
    /// <exception cref="OperationCanceledException">Cancelled during the copy; nothing changed.</exception>
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
    /// Refuses (before changing anything) volumes that have no drive letter or aren't NTFS.
    /// </summary>
    /// <exception cref="PartitionOperationException">The message says whether the partition was grown.</exception>
    Task ExtendPartitionRawAsync(
        int diskNumber,
        long partitionStartOffset,
        long growByBytes,
        string? driveLetter,
        CancellationToken ct = default);
}
