using DiskpartGUI.Services;
using Xunit;

namespace DiskpartGUI.Tests.Services;

[Trait("Category", "Integration")]
public sealed class WmiDiskServiceTests
{
    [Fact]
    public async Task GetPartitionsAsync_ResolvesUniqueDiskpartPartitionNumbers()
    {
        var svc = new WmiDiskService();
        var disks = await svc.GetDisksAsync();

        foreach (var disk in disks)
        {
            var partitions = await svc.GetPartitionsAsync(disk.DiskNumber);
            foreach (var p in partitions)
            {
                Assert.True(p.PartitionNumber is >= 1,
                    $"Disk {disk.DiskNumber}, WMI index {p.PartitionIndex} at offset {p.StartingOffset}: " +
                    $"partition number not resolved ({p.PartitionNumber?.ToString() ?? "null"}).");
            }
            Assert.Equal(partitions.Count, partitions.Select(p => p.PartitionNumber).Distinct().Count());
        }
    }

    [Fact]
    public async Task GetPartitionsAsync_ListsEveryPartitionWithoutOverlap()
    {
        var svc = new WmiDiskService();
        foreach (var disk in await svc.GetDisksAsync())
        {
            var partitions = (await svc.GetPartitionsAsync(disk.DiskNumber)).OrderBy(p => p.StartingOffset).ToList();
            for (int i = 1; i < partitions.Count; i++)
                Assert.True(partitions[i].StartingOffset >= partitions[i - 1].StartingOffset + partitions[i - 1].SizeBytes,
                    $"Disk {disk.DiskNumber}: partitions {partitions[i - 1].PartitionNumber} and {partitions[i].PartitionNumber} overlap.");
            Assert.All(partitions, p => Assert.True(p.StartingOffset + p.SizeBytes <= disk.SizeBytes,
                $"Disk {disk.DiskNumber}: partition {p.PartitionNumber} ends past the disk size."));
        }
    }

    [Fact]
    public async Task FindVolumeDevicePath_FindsTheWindowsVolumeByItsPartition()
    {
        var svc = new WmiDiskService();
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        foreach (var disk in await svc.GetDisksAsync())
            foreach (var p in await svc.GetPartitionsAsync(disk.DiskNumber))
            {
                if (p.PartitionIndex < 0) continue;
                var logical = await svc.GetLogicalDiskAsync(disk.DiskNumber, p.PartitionIndex);
                if (!string.Equals(logical?.DeviceId, systemDrive, StringComparison.OrdinalIgnoreCase)) continue;

                var path = DiskpartGUI.Interop.NativeDisk.FindVolumeDevicePath(disk.DiskNumber, p.StartingOffset);
                Assert.NotNull(path);
                Assert.StartsWith(@"\\?\Volume{", path);
                Assert.False(path!.EndsWith('\\'));
                return;
            }
        Assert.Fail("The system drive's partition was not found.");
    }

    [Fact]
    public async Task GetDisksAsync_FlagsTheDiskWindowsRunsFrom()
    {
        var svc = new WmiDiskService();
        var disks = await svc.GetDisksAsync();

        Assert.Contains(disks, d => d.IsSystemDisk);
        Assert.Contains(disks, d => !d.IsSystemDisk || disks.Count == 1);
    }
}
