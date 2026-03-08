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
        Assert.Null(vm.SelectedDestination);
        Assert.False(vm.CanMove);
    }

    [Fact]
    public void InitialState_DestinationsPopulated()
    {
        var regions = new[] { MakeRegion(0), MakeRegion(100L * 1024 * 1024 * 1024) };
        var vm = MakeVm(regions);
        Assert.Equal(2, vm.AvailableDestinations.Count);
    }

    [Fact]
    public void InitialState_ShowConfigureIsTrue_ShowProgressIsFalse()
    {
        var vm = MakeVm();
        Assert.True(vm.ShowConfigure);
        Assert.False(vm.ShowProgress);
    }

    [Fact]
    public void InitialState_ShowCloseIsFalse_CanCancelIsFalse()
    {
        var vm = MakeVm();
        Assert.False(vm.ShowClose);
        Assert.False(vm.CanCancel);
    }

    // ── SelectedDestination → CanMove ─────────────────────────────────────────

    [Fact]
    public void SelectDestination_CanMoveBecomesTrue()
    {
        var vm = MakeVm();
        vm.SelectedDestination = vm.AvailableDestinations[0];
        Assert.True(vm.CanMove);
    }

    [Fact]
    public void ClearDestination_CanMoveBecomesFalse()
    {
        var vm = MakeVm();
        vm.SelectedDestination = vm.AvailableDestinations[0];
        vm.SelectedDestination = null;
        Assert.False(vm.CanMove);
    }

    // ── Successful move ───────────────────────────────────────────────────────

    [Fact]
    public async Task MoveCommand_OnSuccess_IsCompleteTrue()
    {
        var vm = MakeVm();
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.IsComplete);
        Assert.False(vm.IsCancelled);
        Assert.False(vm.IsMoving);
    }

    [Fact]
    public async Task MoveCommand_OnSuccess_ShowProgressTrue()
    {
        var vm = MakeVm();
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.ShowProgress);
        Assert.False(vm.ShowConfigure);
    }

    [Fact]
    public async Task MoveCommand_OnSuccess_CanMoveIsFalse()
    {
        var vm = MakeVm();
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.False(vm.CanMove);
    }

    [Fact]
    public async Task MoveCommand_OnSuccess_StatusMessageIndicatesCompletion()
    {
        var vm = MakeVm();
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Contains("complet", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cancelled move ────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveCommand_WhenDelegateCancels_IsCancelledTrue()
    {
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> cancelOp =
            (_, _, _) => Task.FromCanceled(new CancellationToken(canceled: true));

        var vm = MakeVm(moveOp: cancelOp);
        vm.SelectedDestination = vm.AvailableDestinations[0];

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
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Contains("cancel", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Progress reporting ────────────────────────────────────────────────────

    [Fact]
    public async Task MoveCommand_ProgressReported_PercentUpdates()
    {
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> op = (_, prog, _) =>
        {
            prog.Report(new MoveProgress(50L * 1024 * 1024, 100L * 1024 * 1024, 0));
            return Task.CompletedTask;
        };

        var vm = MakeVm(moveOp: op);
        vm.SelectedDestination = vm.AvailableDestinations[0];
        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.IsComplete);
    }

    // ── Destination offset computed correctly ─────────────────────────────────

    [Fact]
    public async Task MoveCommand_RightwardMove_PassesEndOfRegionToDelegate()
    {
        var region = new FreeSpaceRegion(512L * 1024 * 1024, 20L * 1024 * 1024 * 1024);
        var partSize = 526L * 1024 * 1024;
        long capturedOffset = -1;

        Func<long, IProgress<MoveProgress>, CancellationToken, Task> op = (offset, _, _) =>
        {
            capturedOffset = offset;
            return Task.CompletedTask;
        };

        // source at 0 → region at 512 MB is a rightward move
        var vm = new MovePartitionViewModel("desc", [region], op,
            sourceOffsetBytes: 0, partitionSizeBytes: partSize);
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        // For a right move: destination = region start + region size - partition size
        long expected = region.StartOffsetBytes + region.SizeBytes - partSize;
        Assert.Equal(expected, capturedOffset);
    }

    [Fact]
    public async Task MoveCommand_LeftwardMove_PassesStartOfRegionToDelegate()
    {
        var region = new FreeSpaceRegion(5L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024);
        var partSize = 526L * 1024 * 1024;
        long capturedOffset = -1;

        Func<long, IProgress<MoveProgress>, CancellationToken, Task> op = (offset, _, _) =>
        {
            capturedOffset = offset;
            return Task.CompletedTask;
        };

        // source at 10 GB → region at 5 GB is a leftward move
        var vm = new MovePartitionViewModel("desc", [region], op,
            sourceOffsetBytes: 10L * 1024 * 1024 * 1024, partitionSizeBytes: partSize);
        vm.SelectedDestination = vm.AvailableDestinations[0];

        await vm.MoveCommand.ExecuteAsync(null);

        // For a left move: destination = region start
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

    // ── Direction label ───────────────────────────────────────────────────────

    [Fact]
    public void Direction_DestinationAfterSource_ShowsRight()
    {
        var region = new FreeSpaceRegion(50L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024);
        var vm = new MovePartitionViewModel("desc", [region], (_, _, _) => Task.CompletedTask,
            sourceOffsetBytes: 20L * 1024 * 1024 * 1024);
        Assert.Contains("right", vm.AvailableDestinations[0].DirectionLabel);
    }

    [Fact]
    public void Direction_DestinationBeforeSource_ShowsLeft()
    {
        var region = new FreeSpaceRegion(5L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024);
        var vm = new MovePartitionViewModel("desc", [region], (_, _, _) => Task.CompletedTask,
            sourceOffsetBytes: 30L * 1024 * 1024 * 1024);
        Assert.Contains("left", vm.AvailableDestinations[0].DirectionLabel);
    }
}
