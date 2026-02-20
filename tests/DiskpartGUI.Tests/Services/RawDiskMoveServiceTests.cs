using DiskpartGUI.Models;
using DiskpartGUI.Services;
using Xunit;

namespace DiskpartGUI.Tests.Services;

public sealed class RawDiskMoveServiceTests
{
    private static DiskInfo MakeDisk(long sizeBytes)
        => new(0, "Test Disk", sizeBytes, "HDD", "OK", "SATA");

    private static PartitionInfo MakePart(long offset, long size)
        => new(0, 0, offset, size, "NTFS", false, false, null);

    private readonly RawDiskMoveService _svc = new();

    [Fact]
    public void GetFreeSpaceRegions_NoPartitions_ReturnsEntireDisk()
    {
        var disk = MakeDisk(100L * 1024 * 1024 * 1024); // 100 GB
        var regions = _svc.GetFreeSpaceRegions(disk, [], 1024 * 1024);

        Assert.Single(regions);
        Assert.Equal(0L, regions[0].StartOffsetBytes);
        Assert.Equal(disk.SizeBytes, regions[0].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_PartitionFillsDisk_ReturnsEmpty()
    {
        var diskSize = 100L * 1024 * 1024 * 1024;
        var disk = MakeDisk(diskSize);
        var parts = new[] { MakePart(0, diskSize) };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, 1024);

        Assert.Empty(regions);
    }

    [Fact]
    public void GetFreeSpaceRegions_FreeSpaceAfterLastPartition_Included()
    {
        var gb = 1024L * 1024 * 1024;
        var disk = MakeDisk(100 * gb);
        var parts = new[] { MakePart(0, 50 * gb) };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, gb);

        Assert.Single(regions);
        Assert.Equal(50 * gb, regions[0].StartOffsetBytes);
        Assert.Equal(50 * gb, regions[0].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_FreeSpaceBetweenPartitions_Included()
    {
        var gb = 1024L * 1024 * 1024;
        var disk = MakeDisk(100 * gb);
        // 20 GB used, 10 GB gap, then 30 GB used
        var parts = new[]
        {
            MakePart(0,       20 * gb),
            MakePart(30 * gb, 30 * gb),
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, gb);

        // Gap between partitions (10 GB) + gap after last partition (40 GB)
        Assert.Equal(2, regions.Count);
        Assert.Equal(20 * gb, regions[0].StartOffsetBytes);
        Assert.Equal(10 * gb, regions[0].SizeBytes);
        Assert.Equal(60 * gb, regions[1].StartOffsetBytes);
        Assert.Equal(40 * gb, regions[1].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_RegionSmallerThanPartition_Excluded()
    {
        var gb = 1024L * 1024 * 1024;
        var disk = MakeDisk(100 * gb);
        // 1 GB gap — too small for a 5 GB partition
        var parts = new[]
        {
            MakePart(0,      20 * gb),
            MakePart(21 * gb, 79 * gb),
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, 5 * gb);

        Assert.Empty(regions);
    }

    [Fact]
    public void GetFreeSpaceRegions_RegionExactlyPartitionSize_Included()
    {
        var gb = 1024L * 1024 * 1024;
        var disk = MakeDisk(100 * gb);
        // 10 GB gap, partition is exactly 10 GB
        var parts = new[]
        {
            MakePart(0,       20 * gb),
            MakePart(30 * gb, 70 * gb),
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, 10 * gb);

        Assert.Single(regions);
        Assert.Equal(20 * gb, regions[0].StartOffsetBytes);
        Assert.Equal(10 * gb, regions[0].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_MultipleGaps_SortedByOffset()
    {
        var gb = 1024L * 1024 * 1024;
        var disk = MakeDisk(120 * gb);
        var parts = new[]
        {
            MakePart(10 * gb, 10 * gb),  // gap before: 10 GB at 0
            MakePart(30 * gb, 10 * gb),  // gap before: 10 GB at 20
            MakePart(50 * gb, 10 * gb),  // gap before: 10 GB at 40, gap after: 60 GB
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, gb);

        Assert.Equal(4, regions.Count);
        Assert.True(regions[0].StartOffsetBytes < regions[1].StartOffsetBytes);
        Assert.True(regions[1].StartOffsetBytes < regions[2].StartOffsetBytes);
        Assert.True(regions[2].StartOffsetBytes < regions[3].StartOffsetBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_ZeroDisk_ReturnsEmpty()
    {
        var disk = MakeDisk(0);
        var regions = _svc.GetFreeSpaceRegions(disk, [], 1024);
        Assert.Empty(regions);
    }
}
