using System.Collections.ObjectModel;
using System.Windows;
using DiskpartGUI.Models;
using DiskpartGUI.Services;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IDiskService _diskService;
    private readonly IPartitionService _partitionService;
    private readonly IPartitionMoveService _moveService;
    private readonly IDialogService _dialogService;

    private DiskItemViewModel? _selectedDisk;
    private int _busyCount;
    private string _statusMessage = "Ready";

    public ObservableCollection<DiskItemViewModel> Disks { get; } = [];

    public DiskItemViewModel? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
                RaiseCommandStates();
        }
    }

    public PartitionItemViewModel? SelectedPartition => SelectedDisk?.SelectedPartition;

    /// <summary>
    /// True while a disk read or diskpart operation is running. The window shows a progress
    /// overlay and every command is disabled, so a second operation can't start against stale
    /// state and the selection can't change mid-operation.
    /// </summary>
    public bool IsBusy => _busyCount > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddPartitionCommand { get; }
    public AsyncRelayCommand DeletePartitionCommand { get; }
    public AsyncRelayCommand ResizePartitionCommand { get; }
    public AsyncRelayCommand MovePartitionCommand { get; }
    public AsyncRelayCommand WipeDiskCommand { get; }
    public RelayCommand AboutCommand { get; }

    public MainViewModel(
        IDiskService diskService,
        IPartitionService partitionService,
        IPartitionMoveService moveService,
        IDialogService dialogService)
    {
        _diskService      = diskService      ?? throw new ArgumentNullException(nameof(diskService));
        _partitionService = partitionService ?? throw new ArgumentNullException(nameof(partitionService));
        _moveService      = moveService      ?? throw new ArgumentNullException(nameof(moveService));
        _dialogService    = dialogService    ?? throw new ArgumentNullException(nameof(dialogService));

        RefreshCommand        = new AsyncRelayCommand(RefreshAsync, () => !IsBusy, onError: HandleError);
        AddPartitionCommand   = new AsyncRelayCommand(AddPartitionAsync, () => !IsBusy && SelectedDisk is not null, onError: HandleError);
        DeletePartitionCommand = new AsyncRelayCommand(DeletePartitionAsync, () => !IsBusy && SelectedPartition is not null, onError: HandleError);
        ResizePartitionCommand = new AsyncRelayCommand(ResizePartitionAsync, () => !IsBusy && SelectedPartition is not null, onError: HandleError);
        MovePartitionCommand  = new AsyncRelayCommand(MovePartitionAsync, () => !IsBusy && SelectedPartition is not null, onError: HandleError);
        WipeDiskCommand       = new AsyncRelayCommand(WipeDiskAsync, () => !IsBusy && SelectedDisk is not null, onError: HandleError);
        AboutCommand          = new RelayCommand(() => _dialogService.ShowAboutDialog());
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        AddPartitionCommand.RaiseCanExecuteChanged();
        DeletePartitionCommand.RaiseCanExecuteChanged();
        ResizePartitionCommand.RaiseCanExecuteChanged();
        MovePartitionCommand.RaiseCanExecuteChanged();
        WipeDiskCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Marks the view model busy until the returned scope is disposed. Scopes nest.</summary>
    private IDisposable BeginBusy()
    {
        if (++_busyCount == 1)
        {
            OnPropertyChanged(nameof(IsBusy));
            RaiseCommandStates();
        }
        return new BusyScope(this);
    }

    private void EndBusy()
    {
        if (--_busyCount == 0)
        {
            OnPropertyChanged(nameof(IsBusy));
            RaiseCommandStates();
        }
    }

    private sealed class BusyScope(MainViewModel owner) : IDisposable
    {
        private MainViewModel? _owner = owner;

        public void Dispose()
        {
            _owner?.EndBusy();
            _owner = null;
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var busy = BeginBusy();
        StatusMessage = "Loading disks...";
        try
        {
            var disks = await _diskService.GetDisksAsync(ct);
            var diskVms = new List<DiskItemViewModel>();

            foreach (var disk in disks)
            {
                var diskVm = new DiskItemViewModel(disk);
                diskVm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DiskItemViewModel.SelectedPartition) ||
                        e.PropertyName == nameof(DiskItemViewModel.SelectedItem))
                    {
                        OnPropertyChanged(nameof(SelectedPartition));
                        RaiseCommandStates();
                    }
                };

                var partitions = await _diskService.GetPartitionsAsync(disk.DiskNumber, ct);
                foreach (var partition in partitions)
                {
                    var logicalDisk = partition.PartitionIndex == PartitionInfo.NoWmiIndex
                        ? null
                        : await _diskService.GetLogicalDiskAsync(disk.DiskNumber, partition.PartitionIndex, ct);
                    diskVm.Partitions.Add(new PartitionItemViewModel(partition, logicalDisk));
                }
                diskVm.BuildDisplayItems();
                diskVms.Add(diskVm);
            }

            void ApplyUpdate()
            {
                var prevSelection = SelectedDisk?.DiskNumber;
                Disks.Clear();
                foreach (var vm in diskVms)
                    Disks.Add(vm);

                SelectedDisk = Disks.FirstOrDefault(d => d.DiskNumber == prevSelection) ?? Disks.FirstOrDefault();
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(ApplyUpdate);
            else
                ApplyUpdate();

            StatusMessage = $"Found {disks.Count} disk(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading disks: {ex.Message}";
            throw;
        }
    }

    private async Task AddPartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null) return;

        // A partition must fit in one contiguous region, so offer the largest one (not the sum
        // of all free space) and tell diskpart exactly where it starts.
        var region = SelectedDisk.LargestFreeRegion;
        var availableMb = region is null ? 0 : region.SizeBytes / DiskAlignment.MiB;
        if (region is null || availableMb <= 0)
        {
            _dialogService.ShowError("No Space Available", "There is no unallocated space on this disk.");
            return;
        }

        var diskNumber = SelectedDisk.DiskNumber;
        var vm = new AddPartitionViewModel(availableMb);
        var result = _dialogService.ShowAddPartitionDialog(vm);
        if (result != true) return;

        // At the maximum, omit size= so diskpart fills the region exactly instead of failing
        // on a few KB of rounding.
        long? sizeMb = vm.SizeMb >= availableMb ? null : vm.SizeMb;
        long offsetKb = region.StartOffsetBytes / 1024;

        string errorMsg;
        using (BeginBusy())
        {
            StatusMessage = $"Adding partition to disk {diskNumber}...";
            var diskResult = await _partitionService.AddPartitionAsync(
                diskNumber, sizeMb, offsetKb, vm.Label, vm.FileSystem, ct);

            if (diskResult.Success)
            {
                await RefreshAsync(ct);
                StatusMessage = "Partition added successfully.";
                return;
            }
            errorMsg = ExtractDiskpartError(diskResult.Output);
        }

        StatusMessage = "Failed to add partition.";
        _dialogService.ShowError("Add Partition Failed", errorMsg);
    }

    private async Task DeletePartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null || SelectedPartition is null) return;
        if (!TryGetDiskpartPartitionNumber(SelectedPartition, "Delete Partition Failed", out var partitionNumber)) return;

        var diskNumber = SelectedDisk.DiskNumber;
        var desc = $"Disk {diskNumber}, Partition {partitionNumber} ({SelectedPartition.DisplaySize})";
        var isSystemOrBoot = SelectedPartition.IsBootable || SelectedPartition.IsActive;

        var vm = new DeletePartitionViewModel(desc, isSystemOrBoot);
        var result = _dialogService.ShowDeletePartitionDialog(vm);
        if (result != true) return;

        string errorMsg;
        using (BeginBusy())
        {
            StatusMessage = $"Deleting partition {partitionNumber} on disk {diskNumber}...";
            var diskResult = await _partitionService.DeletePartitionAsync(
                diskNumber, partitionNumber, vm.ForceOverride, ct);

            if (diskResult.Success)
            {
                await RefreshAsync(ct);
                StatusMessage = "Partition deleted successfully.";
                return;
            }
            errorMsg = ExtractDiskpartError(diskResult.Output);
        }

        StatusMessage = "Failed to delete partition.";
        _dialogService.ShowError("Delete Partition Failed", errorMsg);
    }

    private async Task ResizePartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null || SelectedPartition is null) return;

        // Capture the target now: everything below must act on the partition the user clicked,
        // even if the selection changes while diskpart runs.
        var disk = SelectedDisk;
        var partition = SelectedPartition;
        var currentSizeMb = partition.SizeBytes / (1024 * 1024);

        // Compute contiguous free space immediately after this partition, not total disk free.
        // Total disk free overshoots due to GPT overhead and non-adjacent free regions.
        var partEnd = partition.StartingOffset + partition.SizeBytes;
        var nextStart = disk.Partitions
            .Where(p => p.StartingOffset >= partEnd)
            .OrderBy(p => p.StartingOffset)
            .Select(p => (long?)p.StartingOffset)
            .FirstOrDefault();
        var adjacentFreeBytes = Math.Max(0, (nextStart ?? DiskAlignment.UsableEnd(disk.SizeBytes)) - partEnd);

        // Extend grows the partition and then the NTFS file system inside it; anything else
        // would leave the new space stranded, so only offer it where it can finish.
        string? extendBlockedReason =
            !string.Equals(partition.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase)
                ? $"Extend is only supported for NTFS volumes (this one is {partition.FileSystem ?? "not formatted"})."
            : string.IsNullOrEmpty(partition.DriveLetter)
                ? "Extend needs the volume to have a drive letter."
            : null;
        var availableMb = extendBlockedReason is null ? adjacentFreeBytes / DiskAlignment.MiB : 0;

        var partitionNumber = partition.PartitionNumber;
        long maxShrinkMb;
        if (partitionNumber is null)
        {
            maxShrinkMb = 0; // diskpart can't address this partition: shrink disabled, extend still allowed
        }
        else
        {
            using (BeginBusy())
            {
                StatusMessage = "Checking resize limits…";
                try
                {
                    maxShrinkMb = await _partitionService.QueryShrinkMaxAsync(
                        disk.DiskNumber, partitionNumber.Value, ct);
                }
                catch
                {
                    maxShrinkMb = currentSizeMb - 8; // fallback: allow full range
                }
                StatusMessage = "Ready";
            }
        }

        var vm = new ResizePartitionViewModel(currentSizeMb, availableMb, maxShrinkMb, extendBlockedReason);
        var result = _dialogService.ShowResizePartitionDialog(vm);
        if (result != true) return;

        string errorMsg;
        using (BeginBusy())
        {
            DiskpartResult diskResult;
            if (vm.Operation == ResizeOperation.Shrink)
            {
                if (!TryGetDiskpartPartitionNumber(partition, "Resize Partition Failed", out var shrinkPartitionNumber)) return;
                StatusMessage = $"Shrinking partition {shrinkPartitionNumber} on disk {disk.DiskNumber}...";
                diskResult = await _partitionService.ShrinkPartitionAsync(
                    disk.DiskNumber, shrinkPartitionNumber, vm.DeltaMb, ct);
            }
            else
            {
                StatusMessage = $"Extending partition on disk {disk.DiskNumber}...";
                // Use raw IOCTL_DISK_GROW_PARTITION + FSCTL_EXTEND_VOLUME instead of diskpart.
                // diskpart's extend relies on VDS which caches stale layouts after raw moves;
                // the raw IOCTLs go directly to partmgr and NTFS, bypassing VDS entirely.
                long growByBytes = vm.NewSizeMb >= vm.MaxSizeMb
                    ? adjacentFreeBytes          // exact bytes for max extend
                    : vm.DeltaMb * 1024L * 1024L;
                try
                {
                    await _moveService.ExtendPartitionRawAsync(
                        disk.DiskNumber,
                        partition.StartingOffset,
                        growByBytes,
                        partition.DriveLetter,
                        ct);
                    diskResult = new DiskpartResult(true, string.Empty, null);
                }
                catch (Exception ex)
                {
                    diskResult = new DiskpartResult(false, ex.Message, null);
                }
            }

            if (diskResult.Success)
            {
                await RefreshAsync(ct);
                StatusMessage = $"Partition {(vm.Operation == ResizeOperation.Shrink ? "shrunk" : "extended")} successfully.";
                return;
            }
            errorMsg = ExtractDiskpartError(diskResult.Output);
        }

        StatusMessage = "Failed to resize partition.";
        _dialogService.ShowError("Resize Partition Failed", errorMsg);
    }

    private async Task MovePartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null || SelectedPartition is null) return;

        // Moving these breaks booting: boot records and BCD refer to them by position, and the
        // running Windows volume can't be locked anyway.
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        if (SelectedPartition.IsBootable || SelectedPartition.IsActive ||
            (SelectedPartition.DriveLetter is not null &&
             string.Equals(SelectedPartition.DriveLetter, systemDrive, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogService.ShowError("Move Partition",
                "Boot, system and Windows partitions can't be moved: Windows locates them by their position on the disk.");
            return;
        }

        var allPartitions = SelectedDisk.Partitions.Select(p => p.Partition).ToList();
        var regions = _moveService.GetFreeSpaceRegions(
            SelectedDisk.Disk, allPartitions, SelectedPartition.SizeBytes);

        if (regions.Count == 0)
        {
            _dialogService.ShowError("No Free Space",
                "There is no contiguous free space large enough to move this partition.\n" +
                "Free up space on this disk first.");
            return;
        }

        var desc = $"Disk {SelectedDisk.DiskNumber}, Partition {SelectedPartition.PartitionNumber?.ToString() ?? "?"} ({SelectedPartition.DisplaySize}) — currently at offset {SelectedPartition.DisplayOffset}";
        var srcOffset = SelectedPartition.StartingOffset;
        var sizeBytes = SelectedPartition.SizeBytes;
        var driveLetter = SelectedPartition.DriveLetter;
        var diskNumber = SelectedDisk.DiskNumber;

        var vm = new MovePartitionViewModel(
            desc,
            regions,
            (destOffset, progress, token) => _moveService.MovePartitionAsync(
                diskNumber, driveLetter, srcOffset, destOffset, sizeBytes, progress, token),
            srcOffset,
            sizeBytes);

        _dialogService.ShowMovePartitionDialog(vm);

        // Refresh however the dialog was closed (Close button or X): after a completed or failed
        // move the old layout is stale, and acting on it could overwrite the moved partition.
        if (!vm.IsComplete && !vm.IsCancelled && !vm.IsFailed) return;
        await RefreshAsync(ct);
        StatusMessage = vm.IsComplete ? "Partition moved successfully."
                      : vm.IsFailed   ? "Move failed. See the message in the Move dialog."
                      : "Move cancelled. The partition was not moved.";
    }

    private async Task WipeDiskAsync(CancellationToken ct)
    {
        if (SelectedDisk is null) return;
        var disk = SelectedDisk;

        // diskpart refuses this too, but never offer to wipe the disk Windows is running from.
        if (disk.IsSystemDisk)
        {
            StatusMessage = "Cannot wipe the system disk.";
            _dialogService.ShowError("Wipe Disk",
                $"Disk {disk.DiskNumber} holds the running Windows installation and cannot be wiped.");
            return;
        }

        var desc = $"Disk {disk.DiskNumber} — {disk.Model} ({disk.DisplaySize})";
        var partitions = disk.Partitions
            .OrderBy(p => p.StartingOffset)
            .Select(DescribePartition)
            .ToList();

        var vm = new WipeDiskViewModel(desc, partitions);
        var result = _dialogService.ShowWipeDiskDialog(vm);
        if (result != true || !vm.Confirmed) return;

        string errorMsg;
        using (BeginBusy())
        {
            StatusMessage = $"Wiping disk {disk.DiskNumber}...";
            var diskResult = await _partitionService.CleanDiskAsync(disk.DiskNumber, ct);

            if (diskResult.Success)
            {
                await RefreshAsync(ct);
                StatusMessage = $"Disk {disk.DiskNumber} wiped. It is now empty and uninitialized.";
                return;
            }
            errorMsg = ExtractDiskpartError(diskResult.Output);
        }

        StatusMessage = "Failed to wipe disk.";
        _dialogService.ShowError("Wipe Disk Failed", errorMsg);
    }

    private static string DescribePartition(PartitionItemViewModel p)
    {
        var name = p.PartitionNumber is int n ? $"Partition {n}" : "Partition";
        var letter = string.IsNullOrEmpty(p.DriveLetter) ? string.Empty : $" — {p.DriveLetter}";
        var label = string.IsNullOrEmpty(p.VolumeName) ? string.Empty : $" \"{p.VolumeName}\"";
        return $"{name}{letter}{label} ({p.DisplaySize}, {p.Type})";
    }

    /// <summary>
    /// diskpart's <c>select partition N</c> needs the real partition number, not the WMI index.
    /// Refuse the operation rather than guess, since a wrong number targets a different partition.
    /// </summary>
    private bool TryGetDiskpartPartitionNumber(PartitionItemViewModel partition, string errorTitle, out int partitionNumber)
    {
        if (partition.PartitionNumber is int number)
        {
            partitionNumber = number;
            return true;
        }

        partitionNumber = 0;
        StatusMessage = "Could not determine the partition number.";
        _dialogService.ShowError(errorTitle,
            "Could not determine this partition's number for diskpart. Click Refresh and try again.");
        return false;
    }

    private void HandleError(Exception ex)
    {
        StatusMessage = $"Error: {ex.Message}";
        _dialogService.ShowError("Unexpected Error", ex.Message);
    }

    private static string ExtractDiskpartError(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Prefer a known diskpart error phrase (some, like "The specified partition is not valid.",
        // don't contain the word "error"), then fall back to any line mentioning "error".
        var idx = Array.FindIndex(lines, DiskpartService.ContainsError);
        if (idx < 0)
            idx = Array.FindIndex(lines, l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return output.Trim();
        // VDS errors are two lines: "Virtual Disk Service error:" + detail on the next line
        var errorLine = lines[idx].Trim();
        if (idx + 1 < lines.Length && errorLine.EndsWith(':'))
            errorLine = errorLine + " " + lines[idx + 1].Trim();
        return errorLine;
    }
}
