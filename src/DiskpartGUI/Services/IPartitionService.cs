namespace DiskpartGUI.Services;

public record DiskpartResult(bool Success, string Output, string? ErrorOutput);

public interface IPartitionService
{
    /// <param name="sizeMb">Size in MB; null fills the free region that starts at <paramref name="offsetKb"/>.</param>
    /// <param name="offsetKb">Start offset in KB; null lets diskpart choose.</param>
    Task<DiskpartResult> AddPartitionAsync(
        int diskNumber,
        long? sizeMb,
        long? offsetKb,
        string? label,
        string filesystemType,
        CancellationToken ct = default);

    Task<DiskpartResult> DeletePartitionAsync(
        int diskNumber,
        int partitionNumber,
        bool forceOverride = false,
        CancellationToken ct = default);

    Task<long> QueryShrinkMaxAsync(
        int diskNumber,
        int partitionNumber,
        CancellationToken ct = default);

    Task<DiskpartResult> ShrinkPartitionAsync(
        int diskNumber,
        int partitionNumber,
        long shrinkMb,
        CancellationToken ct = default);

    /// <summary>
    /// Runs diskpart <c>clean</c>: removes all partitions and the partition table, leaving the
    /// disk uninitialized. Data is not overwritten.
    /// </summary>
    Task<DiskpartResult> CleanDiskAsync(
        int diskNumber,
        CancellationToken ct = default);

    Task<DiskpartResult> ExtendPartitionAsync(
        int diskNumber,
        int partitionNumber,
        long? extendMb = null,
        CancellationToken ct = default);
}
