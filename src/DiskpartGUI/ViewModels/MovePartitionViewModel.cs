using System.Collections.ObjectModel;
using DiskpartGUI.Models;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed record MoveDestinationItem(
    FreeSpaceRegion Region,
    string DirectionLabel,
    long DestinationOffsetBytes)
{
    /// <summary>Actual offset where the partition will be placed.</summary>
    public string DisplayDestinationOffset => BytesToHuman(DestinationOffsetBytes);
    public string DisplaySize => Region.DisplaySize;

    private static string BytesToHuman(long bytes)
    {
        if (bytes >= 1_099_511_627_776L) return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}

public sealed class MovePartitionViewModel : ViewModelBase
{
    private readonly Func<long, IProgress<MoveProgress>, CancellationToken, Task> _moveOperation;
    private CancellationTokenSource? _cts;

    private MoveDestinationItem? _selectedDestination;
    private bool _isMoving;
    private bool _isComplete;
    private bool _isCancelled;
    private double _progressPercent;
    private string _progressStatus = string.Empty;

    // ── Read-only info ────────────────────────────────────────────────────────

    public string PartitionDescription { get; }
    public ObservableCollection<MoveDestinationItem> AvailableDestinations { get; }

    // ── State ─────────────────────────────────────────────────────────────────

    public MoveDestinationItem? SelectedDestination
    {
        get => _selectedDestination;
        set
        {
            if (SetProperty(ref _selectedDestination, value))
            {
                OnPropertyChanged(nameof(CanMove));
                MoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsMoving
    {
        get => _isMoving;
        private set
        {
            if (SetProperty(ref _isMoving, value))
            {
                OnPropertyChanged(nameof(CanMove));
                OnPropertyChanged(nameof(ShowProgress));
                OnPropertyChanged(nameof(ShowConfigure));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(ShowClose));
            }
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            if (SetProperty(ref _isComplete, value))
            {
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(ShowClose));
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        private set
        {
            if (SetProperty(ref _isCancelled, value))
            {
                OnPropertyChanged(nameof(ShowClose));
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string ProgressStatus
    {
        get => _progressStatus;
        private set => SetProperty(ref _progressStatus, value);
    }

    // ── Derived UI state ──────────────────────────────────────────────────────

    public bool CanMove    => SelectedDestination is not null && !IsMoving && !IsComplete && !IsCancelled;
    public bool ShowProgress  => IsMoving || IsComplete || IsCancelled;
    public bool ShowConfigure => !ShowProgress;
    public bool CanCancel  => IsMoving && !IsComplete;
    public bool ShowClose  => IsComplete || IsCancelled;

    public string StatusMessage => IsComplete   ? "Move completed successfully."
                                 : IsCancelled  ? "Move cancelled. The original partition is unchanged."
                                 : IsMoving     ? "Moving partition…"
                                 : string.Empty;

    // ── Close callback (set by dialog code-behind) ────────────────────────────

    public Action? RequestClose { get; set; }

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand MoveCommand   { get; }
    public RelayCommand      CancelCommand { get; }
    public RelayCommand      CloseCommand  { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MovePartitionViewModel(
        string partitionDescription,
        IReadOnlyList<FreeSpaceRegion> availableRegions,
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> moveOperation,
        long sourceOffsetBytes = 0L,
        long partitionSizeBytes = 0L)
    {
        PartitionDescription = partitionDescription;
        _moveOperation       = moveOperation;

        AvailableDestinations = new ObservableCollection<MoveDestinationItem>(
            availableRegions.Select(r =>
            {
                bool movingRight = r.StartOffsetBytes >= sourceOffsetBytes;
                // For right moves: slide partition to the far end of the free space
                // so the freed gap is contiguous with whatever is to the left.
                // For left moves: slide to the far start of the free space.
                long destOffset = movingRight
                    ? r.StartOffsetBytes + r.SizeBytes - partitionSizeBytes
                    : r.StartOffsetBytes;
                string dirLabel = movingRight ? "→ right" : "← left";
                return new MoveDestinationItem(r, dirLabel, destOffset);
            }));

        MoveCommand   = new AsyncRelayCommand(ExecuteMoveAsync, () => CanMove);
        CancelCommand = new RelayCommand(ExecuteCancel, () => CanCancel);
        CloseCommand  = new RelayCommand(() => RequestClose?.Invoke());
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task ExecuteMoveAsync(CancellationToken _)
    {
        if (SelectedDestination is null) return;

        _cts = new CancellationTokenSource();
        IsMoving = true;
        ProgressPercent = 0;
        ProgressStatus  = "Starting…";

        try
        {
            var progress = new Progress<MoveProgress>(p =>
            {
                ProgressPercent = p.Percent;
                ProgressStatus  = p.StatusText;
            });

            await _moveOperation(SelectedDestination.DestinationOffsetBytes, progress, _cts.Token);
            IsComplete = true;
        }
        catch (OperationCanceledException)
        {
            IsCancelled = true;
        }
        finally
        {
            IsMoving = false;
            _cts.Dispose();
            _cts = null;

            MoveCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    private void ExecuteCancel()
    {
        _cts?.Cancel();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
