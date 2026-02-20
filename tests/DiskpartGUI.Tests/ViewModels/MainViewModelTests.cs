using DiskpartGUI.Models;
using DiskpartGUI.Services;
using DiskpartGUI.ViewModels;
using Moq;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class MainViewModelTests
{
    private static readonly DiskInfo SampleDisk = new(
        DiskNumber: 0,
        Model: "Test SSD 500GB",
        SizeBytes: 500_107_862_016L,
        MediaType: "Fixed hard disk media",
        Status: "OK",
        InterfaceType: "SCSI");

    private static readonly PartitionInfo SamplePartition = new(
        DiskIndex: 0,
        PartitionIndex: 1,
        StartingOffset: 1_048_576,
        SizeBytes: 104_857_600,
        Type: "IFS",
        IsBootable: false,
        IsActive: false,
        DriveLetter: null);

    private static (MainViewModel vm, Mock<IDiskService> diskSvc, Mock<IPartitionService> partSvc, Mock<IDialogService> dialogSvc)
        CreateSut(IReadOnlyList<DiskInfo>? disks = null)
    {
        var diskSvc    = new Mock<IDiskService>();
        var partSvc    = new Mock<IPartitionService>();
        var moveSvc    = new Mock<IPartitionMoveService>();
        var dialogSvc  = new Mock<IDialogService>();

        diskSvc.Setup(s => s.GetDisksAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(disks ?? [SampleDisk]);
        diskSvc.Setup(s => s.GetPartitionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([SamplePartition]);
        diskSvc.Setup(s => s.GetLogicalDiskAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((LogicalDiskInfo?)null);

        var vm = new MainViewModel(diskSvc.Object, partSvc.Object, moveSvc.Object, dialogSvc.Object);
        return (vm, diskSvc, partSvc, dialogSvc);
    }

    [Fact]
    public void Constructor_NullDiskService_Throws()
    {
        var partSvc   = new Mock<IPartitionService>();
        var moveSvc   = new Mock<IPartitionMoveService>();
        var dialogSvc = new Mock<IDialogService>();
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(null!, partSvc.Object, moveSvc.Object, dialogSvc.Object));
    }

    [Fact]
    public void Constructor_NullPartitionService_Throws()
    {
        var diskSvc   = new Mock<IDiskService>();
        var moveSvc   = new Mock<IPartitionMoveService>();
        var dialogSvc = new Mock<IDialogService>();
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(diskSvc.Object, null!, moveSvc.Object, dialogSvc.Object));
    }

    [Fact]
    public void Constructor_NullDialogService_Throws()
    {
        var diskSvc = new Mock<IDiskService>();
        var partSvc = new Mock<IPartitionService>();
        var moveSvc = new Mock<IPartitionMoveService>();
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(diskSvc.Object, partSvc.Object, moveSvc.Object, null!));
    }

    [Fact]
    public async Task RefreshAsync_PopulatesDisksCollection()
    {
        var (vm, _, _, _) = CreateSut();
        await vm.RefreshAsync();
        Assert.Single(vm.Disks);
        Assert.Equal(0, vm.Disks[0].DiskNumber);
    }

    [Fact]
    public async Task RefreshAsync_SetsSelectedDisk()
    {
        var (vm, _, _, _) = CreateSut();
        await vm.RefreshAsync();
        Assert.NotNull(vm.SelectedDisk);
    }

    [Fact]
    public async Task RefreshAsync_PopulatesPartitions()
    {
        var (vm, _, _, _) = CreateSut();
        await vm.RefreshAsync();
        Assert.Single(vm.Disks[0].Partitions);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesStatusMessage()
    {
        var (vm, _, _, _) = CreateSut();
        await vm.RefreshAsync();
        Assert.Contains("1", vm.StatusMessage); // "Found 1 disk(s)."
    }

    [Fact]
    public async Task RefreshAsync_OnError_UpdatesStatusMessage()
    {
        var (vm, diskSvc, _, _) = CreateSut();
        diskSvc.Setup(s => s.GetDisksAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("WMI failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RefreshAsync());
        Assert.Contains("Error", vm.StatusMessage);
    }

    [Fact]
    public void AddPartitionCommand_CanExecute_ReturnsFalse_WhenNoDiskSelected()
    {
        var (vm, _, _, _) = CreateSut();
        Assert.Null(vm.SelectedDisk);
        Assert.False(vm.AddPartitionCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddPartitionCommand_CanExecute_ReturnsTrue_WhenDiskSelected()
    {
        var (vm, _, _, _) = CreateSut();
        await vm.RefreshAsync();
        Assert.True(vm.AddPartitionCommand.CanExecute(null));
    }

    [Fact]
    public void DeletePartitionCommand_CanExecute_ReturnsFalse_WhenNoPartitionSelected()
    {
        var (vm, _, _, _) = CreateSut();
        Assert.False(vm.DeletePartitionCommand.CanExecute(null));
    }

    [Fact]
    public void ResizePartitionCommand_CanExecute_ReturnsFalse_WhenNoPartitionSelected()
    {
        var (vm, _, _, _) = CreateSut();
        Assert.False(vm.ResizePartitionCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshAsync_MultipleDisks_AllAppearInCollection()
    {
        var disks = new List<DiskInfo>
        {
            SampleDisk,
            SampleDisk with { DiskNumber = 1, Model = "External USB Drive" }
        };
        var (vm, _, _, _) = CreateSut(disks);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Disks.Count);
    }

    [Fact]
    public async Task RefreshAsync_NoDisks_EmptyCollection()
    {
        var (vm, _, _, _) = CreateSut([]);
        await vm.RefreshAsync();
        Assert.Empty(vm.Disks);
        Assert.Null(vm.SelectedDisk);
    }
}
