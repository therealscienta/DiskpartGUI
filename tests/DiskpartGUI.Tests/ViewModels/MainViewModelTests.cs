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
        CreateSut(IReadOnlyList<DiskInfo>? disks = null, IReadOnlyList<PartitionInfo>? partitions = null)
    {
        var diskSvc    = new Mock<IDiskService>();
        var partSvc    = new Mock<IPartitionService>();
        var moveSvc    = new Mock<IPartitionMoveService>();
        var dialogSvc  = new Mock<IDialogService>();

        diskSvc.Setup(s => s.GetDisksAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(disks ?? [SampleDisk]);
        diskSvc.Setup(s => s.GetPartitionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(partitions ?? [SamplePartition]);
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

    // Mirrors a GPT USB stick where WMI reports Index 0/1 but the real partition numbers are 2/3.
    private static readonly PartitionInfo GptPartition = SamplePartition with
    {
        PartitionIndex = 1,
        PartitionNumber = 3,
    };

    private static async Task RefreshAndSelectFirstPartition(MainViewModel vm)
    {
        await vm.RefreshAsync();
        vm.Disks[0].SelectedItem = vm.Disks[0].Partitions[0];
    }

    [Fact]
    public async Task DeletePartition_PassesRealPartitionNumber_NotWmiIndex()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut(partitions: [GptPartition]);
        dialogSvc.Setup(d => d.ShowDeletePartitionDialog(It.IsAny<DeletePartitionViewModel>())).Returns(true);
        partSvc.Setup(s => s.DeletePartitionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(true, "DiskPart successfully deleted the selected partition.", null));
        await RefreshAndSelectFirstPartition(vm);

        await vm.DeletePartitionCommand.ExecuteAsync(null);

        partSvc.Verify(s => s.DeletePartitionAsync(0, 3, false, It.IsAny<CancellationToken>()), Times.Once);
        partSvc.Verify(s => s.DeletePartitionAsync(It.IsAny<int>(), 1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletePartition_DialogShowsRealPartitionNumber()
    {
        var (vm, _, _, dialogSvc) = CreateSut(partitions: [GptPartition]);
        DeletePartitionViewModel? shown = null;
        dialogSvc.Setup(d => d.ShowDeletePartitionDialog(It.IsAny<DeletePartitionViewModel>()))
                 .Callback<DeletePartitionViewModel>(d => shown = d)
                 .Returns(false);
        await RefreshAndSelectFirstPartition(vm);

        await vm.DeletePartitionCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Contains("Partition 3", shown!.PartitionDescription);
    }

    [Fact]
    public async Task DeletePartition_UnresolvedPartitionNumber_DoesNotRunDiskpart()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut(partitions: [SamplePartition with { PartitionNumber = null }]);
        await RefreshAndSelectFirstPartition(vm);

        await vm.DeletePartitionCommand.ExecuteAsync(null);

        dialogSvc.Verify(d => d.ShowDeletePartitionDialog(It.IsAny<DeletePartitionViewModel>()), Times.Never);
        dialogSvc.Verify(d => d.ShowError("Delete Partition Failed", It.IsAny<string>()), Times.Once);
        partSvc.Verify(s => s.DeletePartitionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletePartition_InvalidSelectionOutput_ShowsDiskpartMessage()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut(partitions: [GptPartition]);
        dialogSvc.Setup(d => d.ShowDeletePartitionDialog(It.IsAny<DeletePartitionViewModel>())).Returns(true);
        var output =
            "Microsoft DiskPart version 10.0.26100.1150\r\n\r\n" +
            "Disk 0 is now the selected disk.\r\n\r\n" +
            "The specified partition is not valid.\r\nPlease select a valid partition.\r\n\r\n" +
            "There is no partition selected.\r\nPlease select a partition and try again.\r\n";
        partSvc.Setup(s => s.DeletePartitionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(false, output, null));
        await RefreshAndSelectFirstPartition(vm);

        await vm.DeletePartitionCommand.ExecuteAsync(null);

        dialogSvc.Verify(d => d.ShowError("Delete Partition Failed", "The specified partition is not valid."), Times.Once);
        Assert.Equal("Failed to delete partition.", vm.StatusMessage);
    }

    // ── Add Partition ────────────────────────────────────────────────────────

    private const long MiB = DiskAlignment.MiB;

    [Fact]
    public async Task AddPartition_AtMaximumSize_OmitsSizeAndTargetsLargestRegion()
    {
        // SamplePartition: 100 MiB at 1 MiB → largest region starts at 101 MiB.
        var (vm, _, partSvc, dialogSvc) = CreateSut();
        dialogSvc.Setup(d => d.ShowAddPartitionDialog(It.IsAny<AddPartitionViewModel>()))
                 .Callback<AddPartitionViewModel>(a => a.SizeMb = a.AvailableSpaceMb)
                 .Returns(true);
        partSvc.Setup(s => s.AddPartitionAsync(It.IsAny<int>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(true, "DiskPart successfully assigned the drive letter or mount point.", null));
        await vm.RefreshAsync();

        await vm.AddPartitionCommand.ExecuteAsync(null);

        partSvc.Verify(s => s.AddPartitionAsync(0, null, 101 * 1024, "New Volume", "NTFS", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddPartition_BelowMaximum_PassesSizeAndOffset()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut();
        dialogSvc.Setup(d => d.ShowAddPartitionDialog(It.IsAny<AddPartitionViewModel>()))
                 .Callback<AddPartitionViewModel>(a => a.SizeMb = 2048)
                 .Returns(true);
        partSvc.Setup(s => s.AddPartitionAsync(It.IsAny<int>(), It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(true, string.Empty, null));
        await vm.RefreshAsync();

        await vm.AddPartitionCommand.ExecuteAsync(null);

        partSvc.Verify(s => s.AddPartitionAsync(0, 2048, 101 * 1024, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Resize: Extend gating ────────────────────────────────────────────────

    [Theory]
    [InlineData("exFAT", "E:", "NTFS")]
    [InlineData("NTFS", "", "drive letter")]
    public async Task Resize_ExtendUnavailableForNonNtfsOrNoLetter(string fileSystem, string letter, string expectedReason)
    {
        var (vm, diskSvc, _, dialogSvc) = CreateSut(partitions: [GptPartition]);
        diskSvc.Setup(s => s.GetLogicalDiskAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new LogicalDiskInfo(letter, "USB", fileSystem, 104_857_600, 0));
        ResizePartitionViewModel? shown = null;
        dialogSvc.Setup(d => d.ShowResizePartitionDialog(It.IsAny<ResizePartitionViewModel>()))
                 .Callback<ResizePartitionViewModel>(r => shown = r)
                 .Returns(false);
        await RefreshAndSelectFirstPartition(vm);

        await vm.ResizePartitionCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal(shown!.CurrentSizeMb, shown.MaxSizeMb); // no room offered to extend into
        Assert.Contains(expectedReason, shown.ExtendNote);
    }

    // ── Move Partition ───────────────────────────────────────────────────────

    private static (MainViewModel vm, Mock<IDiskService> diskSvc, Mock<IPartitionMoveService> moveSvc, Mock<IDialogService> dialogSvc)
        CreateSutForMove(PartitionInfo partition)
    {
        var diskSvc   = new Mock<IDiskService>();
        var moveSvc   = new Mock<IPartitionMoveService>();
        var dialogSvc = new Mock<IDialogService>();
        diskSvc.Setup(s => s.GetDisksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([SampleDisk]);
        diskSvc.Setup(s => s.GetPartitionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([partition]);
        diskSvc.Setup(s => s.GetLogicalDiskAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((LogicalDiskInfo?)null);
        moveSvc.Setup(s => s.GetFreeSpaceRegions(It.IsAny<DiskInfo>(), It.IsAny<IReadOnlyList<PartitionInfo>>(), It.IsAny<long>()))
               .Returns([new FreeSpaceRegion(1024 * MiB, 10 * 1024 * MiB)]);
        var vm = new MainViewModel(diskSvc.Object, new Mock<IPartitionService>().Object, moveSvc.Object, dialogSvc.Object);
        return (vm, diskSvc, moveSvc, dialogSvc);
    }

    [Fact]
    public async Task MovePartition_BootablePartition_IsRefused()
    {
        var (vm, _, _, dialogSvc) = CreateSutForMove(SamplePartition with { IsBootable = true, PartitionNumber = 1 });
        await RefreshAndSelectFirstPartition(vm);

        await vm.MovePartitionCommand.ExecuteAsync(null);

        dialogSvc.Verify(d => d.ShowMovePartitionDialog(It.IsAny<MovePartitionViewModel>()), Times.Never);
        dialogSvc.Verify(d => d.ShowError("Move Partition", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MovePartition_CompletedThenClosedWithX_StillRefreshes()
    {
        var (vm, diskSvc, _, dialogSvc) = CreateSutForMove(SamplePartition with { PartitionNumber = 1 });
        dialogSvc.Setup(d => d.ShowMovePartitionDialog(It.IsAny<MovePartitionViewModel>()))
                 .Callback<MovePartitionViewModel>(m =>
                 {
                     m.SelectedDestination = m.AvailableDestinations[0];
                     m.MoveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                 })
                 .Returns((bool?)null); // closed with the window's X, not the Close button
        await RefreshAndSelectFirstPartition(vm);

        await vm.MovePartitionCommand.ExecuteAsync(null);

        diskSvc.Verify(s => s.GetDisksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal("Partition moved successfully.", vm.StatusMessage);
    }

    [Fact]
    public async Task MovePartition_Failed_RefreshesAndReportsFailure()
    {
        var (vm, diskSvc, moveSvc, dialogSvc) = CreateSutForMove(SamplePartition with { PartitionNumber = 1 });
        moveSvc.Setup(s => s.MovePartitionAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>(),
                    It.IsAny<long>(), It.IsAny<IProgress<MoveProgress>>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new PartitionOperationException("Copying failed. Nothing changed."));
        dialogSvc.Setup(d => d.ShowMovePartitionDialog(It.IsAny<MovePartitionViewModel>()))
                 .Callback<MovePartitionViewModel>(m =>
                 {
                     m.SelectedDestination = m.AvailableDestinations[0];
                     m.MoveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                 })
                 .Returns(false);
        await RefreshAndSelectFirstPartition(vm);

        await vm.MovePartitionCommand.ExecuteAsync(null);

        diskSvc.Verify(s => s.GetDisksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.StartsWith("Move failed", vm.StatusMessage);
    }

    // ── Busy state ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePartition_WhileDiskpartRuns_IsBusyAndAllCommandsDisabled()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut(partitions: [GptPartition]);
        dialogSvc.Setup(d => d.ShowDeletePartitionDialog(It.IsAny<DeletePartitionViewModel>())).Returns(true);
        var diskpart = new TaskCompletionSource<DiskpartResult>();
        partSvc.Setup(s => s.DeletePartitionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Returns(diskpart.Task);
        await RefreshAndSelectFirstPartition(vm);
        Assert.False(vm.IsBusy);

        var running = vm.DeletePartitionCommand.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.Contains("Deleting partition 3", vm.StatusMessage);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.AddPartitionCommand.CanExecute(null));
        Assert.False(vm.ResizePartitionCommand.CanExecute(null));
        Assert.False(vm.MovePartitionCommand.CanExecute(null));
        Assert.False(vm.WipeDiskCommand.CanExecute(null));

        diskpart.SetResult(new DiskpartResult(true, "DiskPart successfully deleted the selected partition.", null));
        await running;

        Assert.False(vm.IsBusy);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.Equal("Partition deleted successfully.", vm.StatusMessage);
    }

    [Fact]
    public async Task DeletePartition_Failure_ClearsBusyBeforeShowingError()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut(partitions: [GptPartition]);
        dialogSvc.Setup(d => d.ShowDeletePartitionDialog(It.IsAny<DeletePartitionViewModel>())).Returns(true);
        bool? busyWhenErrorShown = null;
        dialogSvc.Setup(d => d.ShowError(It.IsAny<string>(), It.IsAny<string>()))
                 .Callback(() => busyWhenErrorShown = vm.IsBusy);
        partSvc.Setup(s => s.DeletePartitionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(false, "Access is denied.", null));
        await RefreshAndSelectFirstPartition(vm);

        await vm.DeletePartitionCommand.ExecuteAsync(null);

        Assert.False(busyWhenErrorShown);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_Failure_ClearsBusy()
    {
        var (vm, diskSvc, _, _) = CreateSut();
        diskSvc.Setup(s => s.GetDisksAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("WMI failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RefreshAsync());

        Assert.False(vm.IsBusy);
        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResizePartition_UsesPartitionSelectedWhenStarted_EvenIfSelectionChanges()
    {
        var other = GptPartition with { PartitionIndex = 2, PartitionNumber = 4, StartingOffset = 900_000_000 };
        var (vm, _, partSvc, dialogSvc) = CreateSut(partitions: [GptPartition, other]);
        var querymax = new TaskCompletionSource<long>();
        partSvc.Setup(s => s.QueryShrinkMaxAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .Returns(querymax.Task);
        dialogSvc.Setup(d => d.ShowResizePartitionDialog(It.IsAny<ResizePartitionViewModel>()))
                 .Callback<ResizePartitionViewModel>(r => r.NewSizeMb = r.CurrentSizeMb - 10)
                 .Returns(true);
        partSvc.Setup(s => s.ShrinkPartitionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(true, "DiskPart successfully shrunk the volume by:   10 MB", null));
        await RefreshAndSelectFirstPartition(vm);

        var running = vm.ResizePartitionCommand.ExecuteAsync(null);
        vm.Disks[0].SelectedItem = vm.Disks[0].Partitions[1]; // user clicks another partition meanwhile
        querymax.SetResult(50);
        await running;

        partSvc.Verify(s => s.ShrinkPartitionAsync(0, 3, 10, It.IsAny<CancellationToken>()), Times.Once);
        partSvc.Verify(s => s.ShrinkPartitionAsync(It.IsAny<int>(), 4, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Wipe Disk ────────────────────────────────────────────────────────────

    private static readonly DiskInfo UsbDisk = SampleDisk with
    {
        DiskNumber = 1,
        Model = "Kingston DataTraveler 3.0",
        MediaType = "Removable Media",
    };

    [Fact]
    public void WipeDiskCommand_CanExecute_ReturnsFalse_WhenNoDiskSelected()
    {
        var (vm, _, _, _) = CreateSut();
        Assert.False(vm.WipeDiskCommand.CanExecute(null));
    }

    [Fact]
    public async Task WipeDisk_Confirmed_CleansSelectedDiskAndRefreshes()
    {
        var (vm, diskSvc, partSvc, dialogSvc) = CreateSut([UsbDisk], [GptPartition]);
        WipeDiskViewModel? shown = null;
        dialogSvc.Setup(d => d.ShowWipeDiskDialog(It.IsAny<WipeDiskViewModel>()))
                 .Callback<WipeDiskViewModel>(w => { shown = w; w.Confirmed = true; })
                 .Returns(true);
        partSvc.Setup(s => s.CleanDiskAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(true, "DiskPart succeeded in cleaning the disk.", null));
        await vm.RefreshAsync();

        await vm.WipeDiskCommand.ExecuteAsync(null);

        partSvc.Verify(s => s.CleanDiskAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        diskSvc.Verify(s => s.GetDisksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.StartsWith("Disk 1 wiped.", vm.StatusMessage);
        Assert.NotNull(shown);
        Assert.Contains("Kingston DataTraveler 3.0", shown!.DiskDescription);
        Assert.Single(shown.PartitionSummaries);
        Assert.StartsWith("Partition 3", shown.PartitionSummaries[0]);
    }

    [Fact]
    public async Task WipeDisk_Cancelled_DoesNotRunDiskpart()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut([UsbDisk]);
        dialogSvc.Setup(d => d.ShowWipeDiskDialog(It.IsAny<WipeDiskViewModel>())).Returns(false);
        await vm.RefreshAsync();

        await vm.WipeDiskCommand.ExecuteAsync(null);

        partSvc.Verify(s => s.CleanDiskAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WipeDisk_DialogOkWithoutConfirmation_DoesNotRunDiskpart()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut([UsbDisk]);
        dialogSvc.Setup(d => d.ShowWipeDiskDialog(It.IsAny<WipeDiskViewModel>())).Returns(true);
        await vm.RefreshAsync();

        await vm.WipeDiskCommand.ExecuteAsync(null);

        partSvc.Verify(s => s.CleanDiskAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WipeDisk_SystemDisk_IsRefusedWithoutDialog()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut([SampleDisk with { IsSystemDisk = true }]);
        await vm.RefreshAsync();

        await vm.WipeDiskCommand.ExecuteAsync(null);

        dialogSvc.Verify(d => d.ShowWipeDiskDialog(It.IsAny<WipeDiskViewModel>()), Times.Never);
        dialogSvc.Verify(d => d.ShowError("Wipe Disk", It.IsAny<string>()), Times.Once);
        partSvc.Verify(s => s.CleanDiskAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WipeDisk_DiskHoldingSystemDrive_IsRefused()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var (vm, diskSvc, partSvc, dialogSvc) = CreateSut([UsbDisk]);
        diskSvc.Setup(s => s.GetLogicalDiskAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new LogicalDiskInfo(systemDrive, "Windows", "NTFS", 104_857_600, 0));
        await vm.RefreshAsync();

        await vm.WipeDiskCommand.ExecuteAsync(null);

        dialogSvc.Verify(d => d.ShowWipeDiskDialog(It.IsAny<WipeDiskViewModel>()), Times.Never);
        partSvc.Verify(s => s.CleanDiskAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WipeDisk_DiskpartFails_ShowsDiskpartMessage()
    {
        var (vm, _, partSvc, dialogSvc) = CreateSut([UsbDisk]);
        dialogSvc.Setup(d => d.ShowWipeDiskDialog(It.IsAny<WipeDiskViewModel>()))
                 .Callback<WipeDiskViewModel>(w => w.Confirmed = true)
                 .Returns(true);
        partSvc.Setup(s => s.CleanDiskAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DiskpartResult(false,
                   "Disk 1 is now the selected disk.\r\n\r\nDiskPart has encountered an error: Access is denied.\r\nSee the System Event Log for more information.\r\n",
                   null));
        await vm.RefreshAsync();

        await vm.WipeDiskCommand.ExecuteAsync(null);

        dialogSvc.Verify(d => d.ShowError("Wipe Disk Failed", "DiskPart has encountered an error: Access is denied."), Times.Once);
        Assert.Equal("Failed to wipe disk.", vm.StatusMessage);
    }
}
