using System.Buffers.Binary;
using DiskpartGUI.Models;
using DiskpartGUI.Services;
using Xunit;

namespace DiskpartGUI.Tests.Services;

public sealed class RawDiskMoveServiceTests
{
    private const long MiB = DiskAlignment.MiB;
    private const long GiB = 1024 * MiB;

    private static DiskInfo MakeDisk(long sizeBytes)
        => new(0, "Test Disk", sizeBytes, "HDD", "OK", "SATA");

    private static PartitionInfo MakePart(long offset, long size)
        => new(0, 0, offset, size, "NTFS", false, false, null);

    private readonly RawDiskMoveService _svc = new();

    // ── GetFreeSpaceRegions ──────────────────────────────────────────────────

    [Fact]
    public void GetFreeSpaceRegions_NoPartitions_ReturnsDiskMinusReservedAreas()
    {
        var disk = MakeDisk(100 * GiB);
        var regions = _svc.GetFreeSpaceRegions(disk, [], MiB);

        Assert.Single(regions);
        Assert.Equal(DiskAlignment.LeadingReserveBytes, regions[0].StartOffsetBytes);
        Assert.Equal(DiskAlignment.UsableEnd(disk.SizeBytes), regions[0].StartOffsetBytes + regions[0].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_NeverIncludesFirstMiB_EvenWhenFirstPartitionWasDeleted()
    {
        // First partition deleted: the gap used to start at offset 0, so a left move would
        // have overwritten the MBR / GPT header.
        var disk = MakeDisk(100 * GiB);
        var regions = _svc.GetFreeSpaceRegions(disk, [MakePart(20 * GiB, 10 * GiB)], 5 * GiB);

        Assert.All(regions, r => Assert.True(r.StartOffsetBytes >= DiskAlignment.LeadingReserveBytes));
    }

    [Fact]
    public void GetFreeSpaceRegions_NeverIncludesBackupGptArea()
    {
        var disk = MakeDisk(100 * GiB);
        var regions = _svc.GetFreeSpaceRegions(disk, [MakePart(MiB, 50 * GiB)], MiB);

        var last = regions[^1];
        Assert.True(last.StartOffsetBytes + last.SizeBytes <= disk.SizeBytes - DiskAlignment.TrailingReserveBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_PartitionFillsDisk_ReturnsEmpty()
    {
        var diskSize = 100 * GiB;
        var disk = MakeDisk(diskSize);
        var parts = new[] { MakePart(MiB, diskSize - MiB) };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, 1024);

        Assert.Empty(regions);
    }

    [Fact]
    public void GetFreeSpaceRegions_FreeSpaceAfterLastPartition_Included()
    {
        var disk = MakeDisk(100 * GiB);
        var parts = new[] { MakePart(MiB, 50 * GiB) };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, GiB);

        Assert.Single(regions);
        Assert.Equal(50 * GiB + MiB, regions[0].StartOffsetBytes);
        Assert.Equal(DiskAlignment.UsableEnd(disk.SizeBytes) - (50 * GiB + MiB), regions[0].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_FreeSpaceBetweenPartitions_Included()
    {
        var disk = MakeDisk(100 * GiB);
        // 20 GB used, 10 GB gap, then 30 GB used
        var parts = new[]
        {
            MakePart(MiB,     20 * GiB - MiB),
            MakePart(30 * GiB, 30 * GiB),
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, GiB);

        // Gap between partitions (10 GB) + gap after last partition (~40 GB)
        Assert.Equal(2, regions.Count);
        Assert.Equal(20 * GiB, regions[0].StartOffsetBytes);
        Assert.Equal(10 * GiB, regions[0].SizeBytes);
        Assert.Equal(60 * GiB, regions[1].StartOffsetBytes);
        Assert.Equal(DiskAlignment.UsableEnd(disk.SizeBytes) - 60 * GiB, regions[1].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_RegionSmallerThanPartition_Excluded()
    {
        var disk = MakeDisk(100 * GiB);
        // 1 GB gap — too small for a 5 GB partition
        var parts = new[]
        {
            MakePart(MiB,     20 * GiB - MiB),
            MakePart(21 * GiB, 79 * GiB),
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, 5 * GiB);

        Assert.Empty(regions);
    }

    [Fact]
    public void GetFreeSpaceRegions_RegionExactlyPartitionSize_Included()
    {
        var disk = MakeDisk(100 * GiB);
        // 10 GB gap, partition is exactly 10 GB
        var parts = new[]
        {
            MakePart(MiB,      20 * GiB - MiB),
            MakePart(30 * GiB, 70 * GiB - MiB),
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, 10 * GiB);

        Assert.Single(regions);
        Assert.Equal(20 * GiB, regions[0].StartOffsetBytes);
        Assert.Equal(10 * GiB, regions[0].SizeBytes);
    }

    [Fact]
    public void GetFreeSpaceRegions_MultipleGaps_SortedByOffset()
    {
        var disk = MakeDisk(120 * GiB);
        var parts = new[]
        {
            MakePart(10 * GiB, 10 * GiB),  // gap before: ~10 GB at 1 MiB
            MakePart(30 * GiB, 10 * GiB),  // gap before: 10 GB at 20
            MakePart(50 * GiB, 10 * GiB),  // gap before: 10 GB at 40, gap after: ~60 GB
        };

        var regions = _svc.GetFreeSpaceRegions(disk, parts, GiB);

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

    // ── ValidateMove (live partition table check) ────────────────────────────

    private const uint Mbr = 0, Gpt = 1;
    private const long DiskLength = 100 * GiB;

    /// <summary>Builds a DRIVE_LAYOUT_INFORMATION_EX buffer as IOCTL_DISK_GET_DRIVE_LAYOUT_EX returns it.</summary>
    private static byte[] Layout(uint style, params (long Start, long Length)[] entries)
    {
        var buf = new byte[48 + 144 * Math.Max(entries.Length, 1)];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), style);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), (uint)entries.Length);
        if (style == Gpt)
        {
            // Usable area: after the primary GPT (17 KiB) up to before the backup GPT.
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(24), 17 * 1024);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(32), DiskLength - 17 * 1024 - 16_896);
        }
        for (int i = 0; i < entries.Length; i++)
        {
            int pos = 48 + 144 * i;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), style);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(pos + 8), entries[i].Start);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(pos + 16), entries[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos + 24), (uint)(i + 1));
        }
        return buf;
    }

    // Typical Windows GPT disk: EFI 100 MB, MSR 16 MB (hidden from WMI), data 10 GB at 1 GB.
    private static readonly (long, long) Efi  = (MiB, 100 * MiB);
    private static readonly (long, long) Msr  = (101 * MiB, 16 * MiB);
    private static readonly (long, long) Data = (GiB, 10 * GiB);

    [Fact]
    public void ValidateMove_IntoUnallocatedSpace_IsAllowed()
    {
        var layout = Layout(Gpt, Efi, Msr, Data);
        Assert.Null(RawDiskMoveService.ValidateMove(layout, DiskLength, GiB, 20 * GiB, 10 * GiB));
    }

    [Fact]
    public void ValidateMove_ToStartOfDisk_IsRefused()
    {
        var layout = Layout(Gpt, (20 * GiB, 10 * GiB));
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, 20 * GiB, 0, 10 * GiB);
        Assert.Contains("outside", error);
    }

    [Fact]
    public void ValidateMove_OverHiddenMsrPartition_IsRefused()
    {
        // WMI doesn't list the MSR, so the UI once showed its space as free.
        var layout = Layout(Gpt, Efi, Msr, (200 * MiB, 50 * MiB));
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, 200 * MiB, 101 * MiB, 50 * MiB);
        Assert.Contains("overlaps another partition", error);
    }

    [Fact]
    public void ValidateMove_IntoBackupGptArea_IsRefused()
    {
        var layout = Layout(Gpt, Data);
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, GiB, DiskLength - 10 * GiB, 10 * GiB);
        Assert.Contains("outside", error);
    }

    [Fact]
    public void ValidateMove_SourceNotInTable_AsksForRefresh()
    {
        var layout = Layout(Gpt, Data);
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, 2 * GiB, 20 * GiB, 10 * GiB);
        Assert.Contains("Refresh", error);
    }

    [Fact]
    public void ValidateMove_SourceSizeChanged_AsksForRefresh()
    {
        var layout = Layout(Gpt, Data);
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, GiB, 20 * GiB, 8 * GiB);
        Assert.Contains("Refresh", error);
    }

    [Fact]
    public void ValidateMove_DestinationOverlappingSource_IsRefused()
    {
        // Overlap would make a cancelled or failed copy corrupt the source.
        var layout = Layout(Gpt, Data);
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, GiB, 5 * GiB, 10 * GiB);
        Assert.Contains("overlaps the partition itself", error);
    }

    [Fact]
    public void ValidateMove_UnalignedDestination_IsRefused()
    {
        var layout = Layout(Gpt, Data);
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, GiB, 20 * GiB + 512, 10 * GiB);
        Assert.Contains("aligned", error);
    }

    [Fact]
    public void ValidateMove_MbrBelowFirstMiB_IsRefused()
    {
        var layout = Layout(Mbr, (10 * GiB, 5 * GiB), (0, 0), (0, 0), (0, 0));
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, 10 * GiB, 0, 5 * GiB);
        Assert.Contains("outside", error);
    }

    [Fact]
    public void ValidateMove_MbrPastEndOfDisk_IsRefused()
    {
        var layout = Layout(Mbr, (MiB, 5 * GiB), (0, 0), (0, 0), (0, 0));
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, MiB, DiskLength - 4 * GiB, 5 * GiB);
        Assert.Contains("outside", error);
    }

    [Fact]
    public void ValidateMove_MbrPrimaryIntoFreeSpace_IsAllowed()
    {
        var layout = Layout(Mbr, (MiB, 5 * GiB), (0, 0), (0, 0), (0, 0));
        Assert.Null(RawDiskMoveService.ValidateMove(layout, DiskLength, MiB, 50 * GiB, 5 * GiB));
    }

    [Fact]
    public void ValidateMove_MbrLogicalPartition_IsRefused()
    {
        // Extended container 20–60 GiB holding a logical partition at 21 GiB.
        var layout = Layout(Mbr, (MiB, 5 * GiB), (20 * GiB, 40 * GiB), (0, 0), (0, 0), (21 * GiB, 5 * GiB));
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, 21 * GiB, 70 * GiB, 5 * GiB);
        Assert.Contains("extended partition", error);
    }

    [Fact]
    public void ValidateMove_RawDisk_IsRefused()
    {
        var layout = Layout(2);
        var error = RawDiskMoveService.ValidateMove(layout, DiskLength, GiB, 20 * GiB, GiB);
        Assert.Contains("no partition table", error);
    }
}
