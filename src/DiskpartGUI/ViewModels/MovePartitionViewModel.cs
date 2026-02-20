using System.Collections.ObjectModel;
using DiskpartGUI.Models;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class MovePartitionViewModel : ViewModelBase
{
    private readonly Func<long, IProgress<MoveProgress>, CancellationToken, Task> _moveOperation;
    private CancellationTokenSource? _cts;

    private FreeSpaceRegion? _selectedRegion;
    private bool _isMoving;
    private bool _isComplete;
    private bool _isCancelled;
    private double _progressPercent;
    private string _progressStatus = string.Empty;

    // ── Read-only info ────────────────────────────────────────────────────────

    public string PartitionDescription { get; }
    public ObservableCollection<FreeSpaceRegion> AvailableRegions { get; }

    // ── State ─────────────────────────────────────────────────────────────────

    public FreeSpaceRegion? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (SetProperty(ref _selectedRegion, value))
                OnPropertyChanged(nameof(CanMove));
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

    public bool CanMove    => SelectedRegion is not null && !IsMoving && !IsComplete && !IsCancelled;
    public bool ShowProgress  => IsMoving || IsComplete || IsCancelled;
    public bool ShowConfigure => !ShowProgress;
    public bool CanCancel  => IsMoving && !IsComplete;
    public bool ShowClose  => !IsMoving;

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
        Func<long, IProgress<MoveProgress>, CancellationToken, Task> moveOperation)
    {
        PartitionDescription = partitionDescription;
        AvailableRegions     = new ObservableCollection<FreeSpaceRegion>(availableRegions);
        _moveOperation       = moveOperation;

        MoveCommand   = new AsyncRelayCommand(ExecuteMoveAsync, () => CanMove);
        CancelCommand = new RelayCommand(ExecuteCancel, () => CanCancel);
        CloseCommand  = new RelayCommand(() => RequestClose?.Invoke());
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task ExecuteMoveAsync(CancellationToken _)
    {
        if (SelectedRegion is null) return;

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

            await _moveOperation(SelectedRegion.StartOffsetBytes, progress, _cts.Token);
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
