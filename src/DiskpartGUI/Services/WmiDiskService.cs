using System.Management;
using DiskpartGUI.Models;

namespace DiskpartGUI.Services;

public sealed class WmiDiskService : IDiskService
{
    private const string WmiScope = @"root\cimv2";
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    private const string GptMicrosoftReserved = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";

    public Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken ct = default)
        => Task.Run(() => QueryDisks(), ct);

    public Task<IReadOnlyList<PartitionInfo>> GetPartitionsAsync(int diskIndex, CancellationToken ct = default)
        => Task.Run(() => QueryPartitions(diskIndex), ct);

    public Task<LogicalDiskInfo?> GetLogicalDiskAsync(int diskIndex, int partitionIndex, CancellationToken ct = default)
        => Task.Run(() => QueryLogicalDisk(diskIndex, partitionIndex), ct);

    private sealed record StorageDisk(bool IsSystemDisk, long SizeBytes);

    private sealed record StoragePartition(int Number, long Offset, long Size, string? GptType, int? MbrType);

    private static IReadOnlyList<DiskInfo> QueryDisks()
    {
        var storageDisks = QueryStorageDisks();

        var results = new List<DiskInfo>();
        using var searcher = new ManagementObjectSearcher(WmiScope, "SELECT * FROM Win32_DiskDrive");
        foreach (ManagementObject disk in searcher.Get())
        {
            using (disk)
            {
                var diskNumber = Convert.ToInt32(disk["Index"]);
                storageDisks.TryGetValue(diskNumber, out var storage);

                // Win32_DiskDrive.Size is cylinders × heads × sectors, rounded down by up to
                // ~8 MB; partitions can end beyond it. Prefer the Storage API's exact size.
                var win32Size = disk["Size"] is not null ? Convert.ToInt64(disk["Size"]) : 0L;
                var size = storage is { SizeBytes: > 0 } ? storage.SizeBytes : win32Size;

                results.Add(new DiskInfo(
                    DiskNumber: diskNumber,
                    Model: disk["Model"]?.ToString() ?? "Unknown",
                    SizeBytes: size,
                    MediaType: disk["MediaType"]?.ToString() ?? "Unknown",
                    Status: disk["Status"]?.ToString() ?? "Unknown",
                    InterfaceType: disk["InterfaceType"]?.ToString() ?? "Unknown",
                    IsSystemDisk: storage?.IsSystemDisk ?? false
                ));
            }
        }
        return results.OrderBy(d => d.DiskNumber).ToList();
    }

    /// <summary>
    /// Exact size and boot/system flags per disk number from <c>MSFT_Disk</c>. Empty if the
    /// Storage namespace is unavailable; <c>DiskItemViewModel.IsSystemDisk</c> also checks for
    /// the system drive letter as a fallback.
    /// </summary>
    private static Dictionary<int, StorageDisk> QueryStorageDisks()
    {
        var disks = new Dictionary<int, StorageDisk>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                StorageScope,
                "SELECT Number, Size, IsBoot, IsSystem FROM MSFT_Disk");

            foreach (ManagementObject disk in searcher.Get())
            {
                using (disk)
                {
                    if (disk["Number"] is null) continue;
                    disks[Convert.ToInt32(disk["Number"])] = new StorageDisk(
                        IsSystemDisk: disk["IsBoot"] is true || disk["IsSystem"] is true,
                        SizeBytes: disk["Size"] is not null ? Convert.ToInt64(disk["Size"]) : 0L);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException)
        {
            // Storage namespace missing or query failed: fall back to Win32 values.
        }
        return disks;
    }

    private static IReadOnlyList<PartitionInfo> QueryPartitions(int diskIndex)
    {
        var storagePartitions = QueryStoragePartitions(diskIndex);
        var byOffset = new Dictionary<long, StoragePartition>();
        foreach (var sp in storagePartitions)
            byOffset.TryAdd(sp.Offset, sp);

        var results = new List<PartitionInfo>();
        using var searcher = new ManagementObjectSearcher(
            WmiScope,
            $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}");

        foreach (ManagementObject partition in searcher.Get())
        {
            using (partition)
            {
                var offset = partition["StartingOffset"] is not null ? Convert.ToInt64(partition["StartingOffset"]) : 0L;
                results.Add(new PartitionInfo(
                    DiskIndex: diskIndex,
                    PartitionIndex: Convert.ToInt32(partition["Index"]),
                    StartingOffset: offset,
                    SizeBytes: partition["Size"] is not null ? Convert.ToInt64(partition["Size"]) : 0L,
                    Type: partition["Type"]?.ToString() ?? "Unknown",
                    IsBootable: partition["Bootable"] is bool b && b,
                    IsActive: partition["BootPartition"] is bool bp && bp,
                    DriveLetter: null, // filled by GetLogicalDiskAsync
                    PartitionNumber: byOffset.TryGetValue(offset, out var match) ? match.Number : (int?)null
                ));
            }
        }

        // Win32_DiskPartition hides some partitions (notably the GPT Microsoft Reserved
        // partition). Without them their space looks unallocated and could be offered as a
        // Move destination, so add them from the Storage API.
        var listed = results.Select(p => p.StartingOffset).ToHashSet();
        foreach (var hidden in storagePartitions.Where(p => !listed.Contains(p.Offset) && !IsExtendedContainer(p)))
        {
            results.Add(new PartitionInfo(
                DiskIndex: diskIndex,
                PartitionIndex: PartitionInfo.NoWmiIndex,
                StartingOffset: hidden.Offset,
                SizeBytes: hidden.Size,
                Type: DescribeHiddenType(hidden),
                IsBootable: false,
                IsActive: false,
                DriveLetter: null,
                PartitionNumber: hidden.Number));
        }

        return results.OrderBy(p => p.StartingOffset).ToList();
    }

    // MBR extended-partition containers (0x05, 0x0F) overlap the logical partitions they hold.
    private static bool IsExtendedContainer(StoragePartition p) => p.MbrType is 0x05 or 0x0F;

    private static string DescribeHiddenType(StoragePartition p)
    {
        if (p.GptType is { Length: > 0 } gpt)
            return string.Equals(gpt, GptMicrosoftReserved, StringComparison.OrdinalIgnoreCase)
                ? "GPT: Microsoft Reserved"
                : "GPT: Hidden";
        return "Hidden";
    }

    /// <summary>
    /// All partitions from <c>MSFT_Partition</c>, which (unlike <c>Win32_DiskPartition</c>)
    /// includes hidden ones and reports the real partition number diskpart's
    /// <c>select partition</c> expects. Empty if the Storage namespace is unavailable; callers
    /// must then refuse diskpart operations.
    /// </summary>
    private static List<StoragePartition> QueryStoragePartitions(int diskIndex)
    {
        var list = new List<StoragePartition>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                StorageScope,
                $"SELECT PartitionNumber, Offset, Size, GptType, MbrType FROM MSFT_Partition WHERE DiskNumber = {diskIndex}");

            foreach (ManagementObject partition in searcher.Get())
            {
                using (partition)
                {
                    if (partition["Offset"] is null || partition["PartitionNumber"] is null)
                        continue;
                    list.Add(new StoragePartition(
                        Number: Convert.ToInt32(partition["PartitionNumber"]),
                        Offset: Convert.ToInt64(partition["Offset"]),
                        Size: partition["Size"] is not null ? Convert.ToInt64(partition["Size"]) : 0L,
                        GptType: partition["GptType"]?.ToString(),
                        MbrType: partition["MbrType"] is not null ? Convert.ToInt32(partition["MbrType"]) : null));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException)
        {
            // Storage namespace missing or query failed: leave numbers unresolved.
        }
        return list;
    }

    private static LogicalDiskInfo? QueryLogicalDisk(int diskIndex, int partitionIndex)
    {
        if (partitionIndex < 0) return null; // hidden partition: not in Win32_DiskPartition

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
