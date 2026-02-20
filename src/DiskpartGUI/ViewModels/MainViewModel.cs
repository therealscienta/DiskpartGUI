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
    private bool _isLoading;
    private string _statusMessage = "Ready";

    public ObservableCollection<DiskItemViewModel> Disks { get; } = [];

    public DiskItemViewModel? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                ((AsyncRelayCommand)AddPartitionCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)DeletePartitionCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)ResizePartitionCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)MovePartitionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public PartitionItemViewModel? SelectedPartition => SelectedDisk?.SelectedPartition;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

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

        RefreshCommand        = new AsyncRelayCommand(RefreshAsync, onError: HandleError);
        AddPartitionCommand   = new AsyncRelayCommand(AddPartitionAsync, () => SelectedDisk is not null, onError: HandleError);
        DeletePartitionCommand = new AsyncRelayCommand(DeletePartitionAsync, () => SelectedPartition is not null, onError: HandleError);
        ResizePartitionCommand = new AsyncRelayCommand(ResizePartitionAsync, () => SelectedPartition is not null, onError: HandleError);
        MovePartitionCommand  = new AsyncRelayCommand(MovePartitionAsync, () => SelectedPartition is not null, onError: HandleError);
        AboutCommand          = new RelayCommand(() => _dialogService.ShowAboutDialog());
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IsLoading = true;
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
                    if (e.PropertyName == nameof(DiskItemViewModel.SelectedPartition))
                    {
                        OnPropertyChanged(nameof(SelectedPartition));
                        ((AsyncRelayCommand)DeletePartitionCommand).RaiseCanExecuteChanged();
                        ((AsyncRelayCommand)ResizePartitionCommand).RaiseCanExecuteChanged();
                        ((AsyncRelayCommand)MovePartitionCommand).RaiseCanExecuteChanged();
                    }
                };

                var partitions = await _diskService.GetPartitionsAsync(disk.DiskNumber, ct);
                foreach (var partition in partitions)
                {
                    var logicalDisk = await _diskService.GetLogicalDiskAsync(disk.DiskNumber, partition.PartitionIndex, ct);
                    diskVm.Partitions.Add(new PartitionItemViewModel(partition, logicalDisk));
                }
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
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AddPartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null) return;

        // Approximate available space: total disk - sum of partition sizes
        var usedBytes = SelectedDisk.Partitions.Sum(p => p.SizeBytes);
        var availableMb = (SelectedDisk.SizeBytes - usedBytes) / (1024 * 1024);
        if (availableMb <= 0)
        {
            _dialogService.ShowError("No Space Available", "There is no unallocated space on this disk.");
            return;
        }

        var vm = new AddPartitionViewModel(availableMb);
        var result = _dialogService.ShowAddPartitionDialog(vm);
        if (result != true) return;

        StatusMessage = "Adding partition...";
        var diskResult = await _partitionService.AddPartitionAsync(
            SelectedDisk.DiskNumber, vm.SizeMb, vm.Label, vm.FileSystem, ct);

        if (diskResult.Success)
        {
            StatusMessage = "Partition added successfully.";
            await RefreshAsync(ct);
        }
        else
        {
            var errorMsg = ExtractDiskpartError(diskResult.Output);
            StatusMessage = $"Failed to add partition.";
            _dialogService.ShowError("Add Partition Failed", errorMsg);
        }
    }

    private async Task DeletePartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null || SelectedPartition is null) return;

        var desc = $"Disk {SelectedDisk.DiskNumber}, Partition {SelectedPartition.PartitionIndex} ({SelectedPartition.DisplaySize})";
        var isSystemOrBoot = SelectedPartition.IsBootable || SelectedPartition.IsActive;

        var vm = new DeletePartitionViewModel(desc, isSystemOrBoot);
        var result = _dialogService.ShowDeletePartitionDialog(vm);
        if (result != true) return;

        StatusMessage = "Deleting partition...";
        var diskResult = await _partitionService.DeletePartitionAsync(
            SelectedDisk.DiskNumber, SelectedPartition.PartitionIndex, vm.ForceOverride, ct);

        if (diskResult.Success)
        {
            StatusMessage = "Partition deleted successfully.";
            await RefreshAsync(ct);
        }
        else
        {
            var errorMsg = ExtractDiskpartError(diskResult.Output);
            StatusMessage = "Failed to delete partition.";
            _dialogService.ShowError("Delete Partition Failed", errorMsg);
        }
    }

    private async Task ResizePartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null || SelectedPartition is null) return;

        var currentSizeMb = SelectedPartition.SizeBytes / (1024 * 1024);
        var usedBytes = SelectedDisk.Partitions.Sum(p => p.SizeBytes);
        var availableMb = (SelectedDisk.SizeBytes - usedBytes) / (1024 * 1024);

        StatusMessage = "Checking resize limits…";
        long maxShrinkMb;
        try
        {
            maxShrinkMb = await _partitionService.QueryShrinkMaxAsync(
                SelectedDisk.DiskNumber, SelectedPartition.PartitionIndex, ct);
        }
        catch
        {
            maxShrinkMb = currentSizeMb - 8; // fallback: allow full range
        }

        var vm = new ResizePartitionViewModel(currentSizeMb, availableMb, maxShrinkMb);
        var result = _dialogService.ShowResizePartitionDialog(vm);
        if (result != true) return;

        DiskpartResult diskResult;
        if (vm.Operation == ResizeOperation.Shrink)
        {
            StatusMessage = "Shrinking partition...";
            diskResult = await _partitionService.ShrinkPartitionAsync(
                SelectedDisk.DiskNumber, SelectedPartition.PartitionIndex, vm.DeltaMb, ct);
        }
        else
        {
            StatusMessage = "Extending partition...";
            diskResult = await _partitionService.ExtendPartitionAsync(
                SelectedDisk.DiskNumber, SelectedPartition.PartitionIndex, vm.DeltaMb, ct);
        }

        if (diskResult.Success)
        {
            StatusMessage = $"Partition {(vm.Operation == ResizeOperation.Shrink ? "shrunk" : "extended")} successfully.";
            await RefreshAsync(ct);
        }
        else
        {
            var errorMsg = ExtractDiskpartError(diskResult.Output);
            StatusMessage = "Failed to resize partition.";
            _dialogService.ShowError("Resize Partition Failed", errorMsg);
        }
    }

    private async Task MovePartitionAsync(CancellationToken ct)
    {
        if (SelectedDisk is null || SelectedPartition is null) return;

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

        var desc = $"Disk {SelectedDisk.DiskNumber}, Partition {SelectedPartition.PartitionIndex} ({SelectedPartition.DisplaySize})";
        var srcOffset = SelectedPartition.StartingOffset;
        var sizeBytes = SelectedPartition.SizeBytes;
        var driveLetter = SelectedPartition.DriveLetter;
        var diskNumber = SelectedDisk.DiskNumber;

        var vm = new MovePartitionViewModel(
            desc,
            regions,
            (destOffset, progress, token) => _moveService.MovePartitionAsync(
                diskNumber, driveLetter, srcOffset, destOffset, sizeBytes, progress, token));

        var result = _dialogService.ShowMovePartitionDialog(vm);
        if (result == true)
        {
            StatusMessage = "Partition moved successfully.";
            await RefreshAsync(ct);
        }
    }

    private void HandleError(Exception ex)
    {
        StatusMessage = $"Error: {ex.Message}";
        _dialogService.ShowError("Unexpected Error", ex.Message);
    }

    private static string ExtractDiskpartError(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var errorLine = lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        return errorLine?.Trim() ?? output.Trim();
    }
}
