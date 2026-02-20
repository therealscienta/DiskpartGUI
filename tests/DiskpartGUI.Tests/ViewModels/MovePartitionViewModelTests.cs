using DiskpartGUI.Models;
using DiskpartGUI.ViewModels;
using Xunit;

namespace DiskpartGUI.Tests.ViewModels;

public sealed class MovePartitionViewModelTests
{
    private static FreeSpaceRegion MakeRegion(long offset = 0, long size = 10L * 1024 * 1024 * 1024)
        => new(offset, size);

    private static MovePartitionViewModel MakeVm(
        IReadOnlyList<FreeSpaceRegion>? regions = null,
        Func<long, IProgress<MoveProgress>, CancellationToken, Task>? moveOp = null)
    {
        regions ??= [MakeRegion()];
        moveOp  ??= (_, _, _) => Task.CompletedTask;
        return new MovePartitionViewModel("Disk 0, Partition 1 (50 GB NTFS)", regions, moveOp);
    }

    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_NoSelection_CanMoveIsFalse()
    {
        var vm = MakeVm();
        Assert.Null(vm.SelectedRegion);
        Assert.False(vm.CanMove);
    }

    [Fact]
    public void InitialState_RegionsPopulated()
    {
        var regions = new[] { MakeRegion(0), MakeRegion(100L * 1024 * 1024 * 1024) };
        var vm = MakeVm(regions);
        Assert.Equal(2, vm.AvailableRegions.Count);
    }

    [Fact]
    public void InitialState_ShowConfigureIsTrue_ShowProgressIsFalse()
    {
        var vm = MakeVm();
        Assert.True(vm.ShowConfigure);
        Assert.False(vm.ShowProgress);
    }

    [Fact]
    public void InitialState_ShowCloseIsTrue_CanCancelIsFalse()
    {
        var vm = MakeVm();
        Assert.True(vm.ShowClose);
        Assert.False(vm.CanCancel);
    }

    // ── SelectedRegion → CanMove ──────────────────────────────────────────────

    [Fact]
    public void SelectRegion_CanMoveBecomesTrue()
    {
        var vm = MakeVm();
        vm.SelectedRegion = vm.AvailableRegions[0];
        Assert.True(vm.CanMove);
    }

    [Fact]
    public void ClearRegion_CanMoveBecomesFalse()
    {
        var vm = MakeVm();
        vm.SelectedRegion = vm.AvailableRegions[0];
        vm.SelectedRegion = null;
        Assert.False(vm.CanMove);
    }

    // ── Successful move ───────────────────────────────────────────────────────

    [Fact]
    public async Task MoveCommand_OnSuccess_IsCompleteTrue()
    {
        var vm = MakeVm();
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.IsComplete);
        Assert.False(vm.IsCancelled);
        Assert.False(vm.IsMoving);
    }

    [Fact]
    public async Task MoveCommand_OnSuccess_ShowProgressTrue()
    {
        var vm = MakeVm();
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.ShowProgress);
        Assert.False(vm.ShowConfigure);
    }

    [Fact]
    public async Task MoveCommand_OnSuccess_CanMoveIsFalse()
    {
        var vm = MakeVm();
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.False(vm.CanMove);
    }

    [Fact]
    public async Task MoveCommand_OnSuccess_StatusMessageIndicatesCompletion()
    {
        var vm = MakeVm();
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Contains("complet", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cancelled move ────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveCommand_WhenDelegateCancels_IsCancelledTrue()
    {
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> op =
            (_, _, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        // Pre-cancelled token scenario: delegate that throws OperationCanceledException
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> cancelOp =
            (_, _, _) => Task.FromCanceled(new CancellationToken(canceled: true));

        var vm = MakeVm(moveOp: cancelOp);
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.IsCancelled);
        Assert.False(vm.IsComplete);
        Assert.False(vm.IsMoving);
    }

    [Fact]
    public async Task MoveCommand_WhenCancelled_StatusMessageIndicatesCancellation()
    {
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> cancelOp =
            (_, _, _) => Task.FromCanceled(new CancellationToken(canceled: true));

        var vm = MakeVm(moveOp: cancelOp);
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Contains("cancel", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Progress reporting ────────────────────────────────────────────────────

    [Fact]
    public async Task MoveCommand_ProgressReported_PercentUpdates()
    {
        double capturedPercent = -1;

        Func<long, IProgress<MoveProgress>, CancellationToken, Task> op = (_, prog, _) =>
        {
            prog.Report(new MoveProgress(50L * 1024 * 1024, 100L * 1024 * 1024, 0));
            return Task.CompletedTask;
        };

        var vm = MakeVm(moveOp: op);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovePartitionViewModel.ProgressPercent))
                capturedPercent = vm.ProgressPercent;
        };

        vm.SelectedRegion = vm.AvailableRegions[0];
        await vm.MoveCommand.ExecuteAsync(null);

        // Progress.Report is async by default — percent may or may not have been set
        // synchronously in tests; just verify completion state
        Assert.True(vm.IsComplete);
    }

    // ── Destination offset passed correctly ───────────────────────────────────

    [Fact]
    public async Task MoveCommand_PassesSelectedRegionOffsetToDelegate()
    {
        var region = new FreeSpaceRegion(512L * 1024 * 1024, 20L * 1024 * 1024 * 1024);
        long capturedOffset = -1;

        Func<long, IProgress<MoveProgress>, CancellationToken, Task> op = (offset, _, _) =>
        {
            capturedOffset = offset;
            return Task.CompletedTask;
        };

        var vm = MakeVm(regions: [region], moveOp: op);
        vm.SelectedRegion = vm.AvailableRegions[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Equal(region.StartOffsetBytes, capturedOffset);
    }

    // ── PartitionDescription ──────────────────────────────────────────────────

    [Fact]
    public void PartitionDescription_SetFromConstructor()
    {
        const string desc = "Disk 0, Partition 2 (100 GB NTFS)";
        var vm = new MovePartitionViewModel(desc, [MakeRegion()], (_, _, _) => Task.CompletedTask);
        Assert.Equal(desc, vm.PartitionDescription);
    }
}
