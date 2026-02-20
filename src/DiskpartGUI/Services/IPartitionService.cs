namespace DiskpartGUI.Services;

public record DiskpartResult(bool Success, string Output, string? ErrorOutput);

public interface IPartitionService
{
    Task<DiskpartResult> AddPartitionAsync(
        int diskNumber,
        long sizeMb,
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

    Task<DiskpartResult> ExtendPartitionAsync(
        int diskNumber,
        int partitionNumber,
        long extendMb,
        CancellationToken ct = default);
}
