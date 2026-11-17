namespace DiskpartGUI.Models;

/// <param name="PartitionIndex">
/// WMI <c>Win32_DiskPartition.Index</c>: 0-based and skips partitions WMI does not report
/// (e.g. GPT MSR). Only valid for WMI lookups such as the <c>Win32_DiskPartition.DeviceID</c>
/// association. Never pass it to diskpart.
/// </param>
/// <param name="PartitionNumber">
/// The real partition number (<c>MSFT_Partition.PartitionNumber</c>), as used by diskpart's
/// <c>select partition N</c> and Disk Management. Null when it could not be resolved.
/// </param>
public sealed record PartitionInfo(
    int DiskIndex,
    int PartitionIndex,
    long StartingOffset,
    long SizeBytes,
    string Type,
    bool IsBootable,
    bool IsActive,
    string? DriveLetter,
    int? PartitionNumber = null
)
{
    /// <summary><see cref="PartitionIndex"/> of a partition WMI's Win32_DiskPartition doesn't list (e.g. GPT MSR).</summary>
    public const int NoWmiIndex = -1;
}
