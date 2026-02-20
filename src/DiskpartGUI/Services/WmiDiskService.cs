using System.Management;
using DiskpartGUI.Models;

namespace DiskpartGUI.Services;

public sealed class WmiDiskService : IDiskService
{
    private const string WmiScope = @"root\cimv2";

    public Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken ct = default)
        => Task.Run(() => QueryDisks(), ct);

    public Task<IReadOnlyList<PartitionInfo>> GetPartitionsAsync(int diskIndex, CancellationToken ct = default)
        => Task.Run(() => QueryPartitions(diskIndex), ct);

    public Task<LogicalDiskInfo?> GetLogicalDiskAsync(int diskIndex, int partitionIndex, CancellationToken ct = default)
        => Task.Run(() => QueryLogicalDisk(diskIndex, partitionIndex), ct);

    private static IReadOnlyList<DiskInfo> QueryDisks()
    {
        var results = new List<DiskInfo>();
        using var searcher = new ManagementObjectSearcher(WmiScope, "SELECT * FROM Win32_DiskDrive");
        foreach (ManagementObject disk in searcher.Get())
        {
            using (disk)
            {
                results.Add(new DiskInfo(
                    DiskNumber: Convert.ToInt32(disk["Index"]),
                    Model: disk["Model"]?.ToString() ?? "Unknown",
                    SizeBytes: disk["Size"] is not null ? Convert.ToInt64(disk["Size"]) : 0L,
                    MediaType: disk["MediaType"]?.ToString() ?? "Unknown",
                    Status: disk["Status"]?.ToString() ?? "Unknown",
                    InterfaceType: disk["InterfaceType"]?.ToString() ?? "Unknown"
                ));
            }
        }
        return results.OrderBy(d => d.DiskNumber).ToList();
    }

    private static IReadOnlyList<PartitionInfo> QueryPartitions(int diskIndex)
    {
        var results = new List<PartitionInfo>();
        using var searcher = new ManagementObjectSearcher(
            WmiScope,
            $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}");

        foreach (ManagementObject partition in searcher.Get())
        {
            using (partition)
            {
                results.Add(new PartitionInfo(
                    DiskIndex: diskIndex,
                    PartitionIndex: Convert.ToInt32(partition["Index"]),
                    StartingOffset: partition["StartingOffset"] is not null ? Convert.ToInt64(partition["StartingOffset"]) : 0L,
                    SizeBytes: partition["Size"] is not null ? Convert.ToInt64(partition["Size"]) : 0L,
                    Type: partition["Type"]?.ToString() ?? "Unknown",
                    IsBootable: partition["Bootable"] is bool b && b,
                    IsActive: partition["BootPartition"] is bool bp && bp,
                    DriveLetter: null // filled by GetLogicalDiskAsync
                ));
            }
        }
        return results.OrderBy(p => p.PartitionIndex).ToList();
    }

    private static LogicalDiskInfo? QueryLogicalDisk(int diskIndex, int partitionIndex)
    {
        // WMI DeviceID for a partition is formatted as "Disk #N, Partition #M"
        var partitionDeviceId = $"Disk #{diskIndex}, Partition #{partitionIndex}";

        using var searcher = new ManagementObjectSearcher(
            WmiScope,
            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} " +
            "WHERE AssocClass=Win32_LogicalDiskToPartition");

        foreach (ManagementObject logical in searcher.Get())
        {
            using (logical)
            {
                return new LogicalDiskInfo(
                    DeviceId: logical["DeviceID"]?.ToString() ?? string.Empty,
                    VolumeName: logical["VolumeName"]?.ToString(),
                    FileSystem: logical["FileSystem"]?.ToString(),
                    SizeBytes: logical["Size"] is not null ? Convert.ToInt64(logical["Size"]) : 0L,
                    FreeSpaceBytes: logical["FreeSpace"] is not null ? Convert.ToInt64(logical["FreeSpace"]) : 0L
                );
            }
        }
        return null;
    }
}
