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
}
