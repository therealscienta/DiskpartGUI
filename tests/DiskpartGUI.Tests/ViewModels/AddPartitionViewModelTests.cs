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
    public void IsValid_Fat32Over32Gb_ReturnsFalseWithMessage()
    {
        var vm = new AddPartitionViewModel(59_051) { SizeMb = 59_051, FileSystem = "FAT32" };
        Assert.False(vm.IsValid);
        Assert.Contains("FAT32", vm.ValidationMessage);
    }

    [Fact]
    public void IsValid_Fat32AtLimit_ReturnsTrue()
    {
        var vm = new AddPartitionViewModel(59_051) { SizeMb = AddPartitionViewModel.MaxFat32SizeMb, FileSystem = "FAT32" };
        Assert.True(vm.IsValid);
        Assert.Null(vm.ValidationMessage);
    }

    [Theory]
    [InlineData("NTFS")]
    [InlineData("exFAT")]
    public void IsValid_LargeNonFat32_ReturnsTrue(string fileSystem)
    {
        var vm = new AddPartitionViewModel(59_051) { SizeMb = 59_051, FileSystem = fileSystem };
        Assert.True(vm.IsValid);
        Assert.Null(vm.ValidationMessage);
    }

    [Theory]
    [InlineData("NTFS", "A label of exactly 32 chars......", false)]  // 33 chars
    [InlineData("NTFS", "Thirty-two characters exactly!!!", true)]   // 32 chars
    [InlineData("FAT32", "USB STICK 1", true)]                      // 11 chars
    [InlineData("FAT32", "USB STICK 12", false)]                    // 12 chars
    [InlineData("exFAT", "USB STICK 12", false)]
    [InlineData("FAT32", "BACKUP.2026", false)]                     // '.' not allowed on FAT
    [InlineData("exFAT", "A/B", false)]
    [InlineData("NTFS", "Backup 2026.11", true)]                    // fine on NTFS
    [InlineData("NTFS", "Say \"hi\"", false)]                       // breaks the diskpart script
    [InlineData("NTFS", "Line\nBreak", false)]
    public void IsValid_ChecksLabelForFileSystem(string fileSystem, string label, bool expectedValid)
    {
        var vm = new AddPartitionViewModel(10_000) { SizeMb = 1024, FileSystem = fileSystem, Label = label };
        Assert.Equal(expectedValid, vm.IsValid);
        Assert.Equal(expectedValid, vm.ValidationMessage is null);
    }

    [Fact]
    public void Label_Change_RaisesValidationPropertyChanged()
    {
        var vm = new AddPartitionViewModel(10_000);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Label = "x";

        Assert.Contains(nameof(AddPartitionViewModel.IsValid), raised);
        Assert.Contains(nameof(AddPartitionViewModel.ValidationMessage), raised);
    }

    [Fact]
    public void FileSystem_Change_RaisesIsValidPropertyChanged()
    {
        var vm = new AddPartitionViewModel(59_051) { SizeMb = 59_051 };
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FileSystem = "FAT32";

        Assert.Contains(nameof(AddPartitionViewModel.IsValid), raised);
        Assert.Contains(nameof(AddPartitionViewModel.ValidationMessage), raised);
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
