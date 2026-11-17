using DiskpartGUI.Converters;
using DiskpartGUI.Models;
using DiskpartGUI.ViewModels;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class DiskItemViewModelTests
{
    private const long MiB = DiskAlignment.MiB;
    private const long GiB = 1024 * MiB;

    private static DiskItemViewModel Disk(long size, params (long Offset, long Size, string Type)[] parts)
    {
        var vm = new DiskItemViewModel(new DiskInfo(0, "Test", size, "Fixed", "OK", "SATA"));
        int n = 1;
        foreach (var (offset, length, type) in parts)
            vm.Partitions.Add(new PartitionItemViewModel(
                new PartitionInfo(0, n - 1, offset, length, type, false, false, null, n++), null));
        vm.BuildDisplayItems();
        return vm;
    }

    private static List<FreeSpaceItemViewModel> FreeItems(DiskItemViewModel vm)
        => vm.DisplayItems.OfType<FreeSpaceItemViewModel>().ToList();

    [Fact]
    public void BuildDisplayItems_PartitionAtFirstMiB_ShowsNoLeadingUnallocatedRegion()
    {
        var vm = Disk(100 * GiB, (MiB, 100 * GiB - 2 * MiB, "GPT: Basic Data"));
        Assert.DoesNotContain(FreeItems(vm), f => f.StartingOffset < MiB);
        Assert.IsType<PartitionItemViewModel>(vm.DisplayItems[0]);
    }

    [Fact]
    public void BuildDisplayItems_HiddenMsrListed_LeavesNoFakeGap()
    {
        // EFI 100 MB, MSR 16 MB, data: before the fix the MSR showed as 16 MB "Unallocated".
        var vm = Disk(100 * GiB,
            (MiB, 100 * MiB, "GPT: System"),
            (101 * MiB, 16 * MiB, "GPT: Microsoft Reserved"),
            (117 * MiB, 99 * GiB, "GPT: Basic Data"));

        Assert.DoesNotContain(FreeItems(vm), f => f.StartingOffset < 99 * GiB);
    }

    [Fact]
    public void BuildDisplayItems_EmptyDisk_OneRegionExcludingReservedAreas()
    {
        var size = 4_026_531_840L;
        var vm = Disk(size);

        var region = Assert.Single(FreeItems(vm));
        Assert.Equal(MiB, region.StartingOffset);
        Assert.Equal(DiskAlignment.UsableEnd(size) - MiB, region.SizeBytes);
        Assert.Equal(region.SizeBytes, vm.LargestFreeRegion!.SizeBytes);
    }

    [Fact]
    public void LargestFreeRegion_PicksBiggestGap_NotTotalFree()
    {
        var vm = Disk(100 * GiB,
            (MiB, 10 * GiB, "IFS"),             // gap of ~5 GiB after
            (15 * GiB, 70 * GiB, "IFS"));       // gap of ~15 GiB after

        Assert.Equal(85 * GiB, vm.LargestFreeRegion!.StartOffsetBytes);
        Assert.Equal(2, FreeItems(vm).Count);
    }

    [Fact]
    public void LargestFreeRegion_FullDisk_IsNull()
    {
        var vm = Disk(100 * GiB, (MiB, DiskAlignment.UsableEnd(100 * GiB) - MiB, "IFS"));
        Assert.Null(vm.LargestFreeRegion);
    }
}

public sealed class PartitionTypeToColorConverterTests
{
    private static object Color(string type)
        => new PartitionTypeToColorConverter().Convert(type, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void MbrDataPartition_IsColoredAsData_NotSystem()
    {
        // WMI's type name for MBR NTFS/exFAT partitions contains the word "System".
        Assert.Same(Color("GPT: Basic Data"), Color("Installable File System"));
        Assert.NotSame(Color("GPT: System"), Color("Installable File System"));
    }

    [Theory]
    [InlineData("GPT: System")]
    [InlineData("GPT: Microsoft Reserved")]
    public void SystemAndReservedPartitions_AreColoredAsSystem(string type)
        => Assert.Same(Color("GPT: System"), Color(type));
}
