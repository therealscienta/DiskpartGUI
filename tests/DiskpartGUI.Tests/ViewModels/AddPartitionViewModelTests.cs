using DiskpartGUI.ViewModels;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class AddPartitionViewModelTests
{
    [Fact]
    public void IsValid_SizeMbZero_ReturnsFalse()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        vm.SizeMb = 0;
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_SizeMbNegative_ReturnsFalse()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        vm.SizeMb = -100;
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_SizeMbExceedsAvailable_ReturnsFalse()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 1024);
        vm.SizeMb = 2048;
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_ValidSize_ReturnsTrue()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        vm.SizeMb = 5120;
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void IsValid_SizeMbEqualsAvailable_ReturnsTrue()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 1024);
        vm.SizeMb = 1024;
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Constructor_DefaultsToNtfs()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        Assert.Equal("NTFS", vm.FileSystem);
    }

    [Fact]
    public void Constructor_DefaultLabel_IsNotEmpty()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        Assert.False(string.IsNullOrWhiteSpace(vm.Label));
    }

    [Fact]
    public void Constructor_DefaultSize_ClampedToAvailable()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 512);
        Assert.True(vm.SizeMb <= 512);
    }

    [Fact]
    public void FileSystems_ContainsNtfsFat32ExFat()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        Assert.Contains("NTFS", vm.FileSystems);
        Assert.Contains("FAT32", vm.FileSystems);
        Assert.Contains("exFAT", vm.FileSystems);
    }

    [Fact]
    public void SizeMb_Change_RaisesIsValidPropertyChanged()
    {
        var vm = new AddPartitionViewModel(availableSpaceMb: 10240);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SizeMb = 2048;

        Assert.Contains("IsValid", raised);
    }
}
