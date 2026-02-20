using DiskpartGUI.ViewModels;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class DeletePartitionViewModelTests
{
    [Fact]
    public void CanConfirm_NonSystemPartition_AlwaysTrue()
    {
        var vm = new DeletePartitionViewModel("Partition 1 (100 GB)", isSystemOrBoot: false);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void CanConfirm_SystemPartition_WithoutForceOverride_ReturnsFalse()
    {
        var vm = new DeletePartitionViewModel("Partition 0 (boot)", isSystemOrBoot: true);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void CanConfirm_SystemPartition_WithForceOverride_ReturnsTrue()
    {
        var vm = new DeletePartitionViewModel("Partition 0 (boot)", isSystemOrBoot: true);
        vm.ForceOverride = true;
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void WarningMessage_SystemPartition_ContainsBootWarning()
    {
        var vm = new DeletePartitionViewModel("Partition 0", isSystemOrBoot: true);
        Assert.Contains("bootable", vm.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningMessage_NonSystemPartition_ContainsPermanentWarning()
    {
        var vm = new DeletePartitionViewModel("Partition 2", isSystemOrBoot: false);
        Assert.Contains("permanent", vm.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForceOverride_Change_RaisesCanConfirmPropertyChanged()
    {
        var vm = new DeletePartitionViewModel("Partition 0", isSystemOrBoot: true);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ForceOverride = true;

        Assert.Contains("CanConfirm", raised);
    }
}
