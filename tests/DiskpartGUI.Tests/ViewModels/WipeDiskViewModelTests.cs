using DiskpartGUI.ViewModels;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class WipeDiskViewModelTests
{
    private static WipeDiskViewModel Create(params string[] partitions)
        => new("Disk 1 — USB Stick (57.7 GB)", partitions);

    [Fact]
    public void CanConfirm_FalseUntilConfirmed()
    {
        var vm = Create("Partition 3 — E: (300 KB, GPT: Basic Data)");
        Assert.False(vm.CanConfirm);

        vm.Confirmed = true;
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void Confirmed_RaisesCanConfirmChanged()
    {
        var vm = Create();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Confirmed = true;

        Assert.Contains(nameof(WipeDiskViewModel.CanConfirm), raised);
    }

    [Fact]
    public void ConfirmationText_NamesTheDisk()
    {
        var vm = Create();
        Assert.Contains("Disk 1 — USB Stick (57.7 GB)", vm.ConfirmationText);
    }

    [Fact]
    public void HasPartitions_ReflectsSummaries()
    {
        Assert.False(Create().HasPartitions);
        Assert.True(Create("Partition 2 (5 MB, GPT: System)").HasPartitions);
    }
}
