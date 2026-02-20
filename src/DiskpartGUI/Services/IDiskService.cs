using DiskpartGUI.Models;

namespace DiskpartGUI.Services;

public interface IDiskService
{
    Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PartitionInfo>> GetPartitionsAsync(int diskIndex, CancellationToken ct = default);
    Task<LogicalDiskInfo?> GetLogicalDiskAsync(int diskIndex, int partitionIndex, CancellationToken ct = default);
}
