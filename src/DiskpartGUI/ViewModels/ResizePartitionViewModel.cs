using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public enum ResizeOperation { Shrink, Extend }

public sealed class ResizePartitionViewModel : ViewModelBase
{
    private long _newSizeMb;

    public long CurrentSizeMb { get; }
    public long MaxSizeMb { get; }   // max total size after extend (available + current)
    public long MaxShrinkMb { get; } // from shrink querymax; 0 = unknown/fallback
    public long MinSizeMb { get; } = 8; // minimum partition size in MB

    public long MinNewSizeMb => Math.Max(MinSizeMb, CurrentSizeMb - MaxShrinkMb);

    public bool HasShrinkLimit => MaxShrinkMb > 0;

    public string ShrinkLimitText => MaxShrinkMb > 0
        ? $"Maximum shrinkable: {(MaxShrinkMb >= 1024 ? $"{MaxShrinkMb / 1024:N0} GB" : $"{MaxShrinkMb:N0} MB")}"
        : string.Empty;

    public long NewSizeMb
    {
        get => _newSizeMb;
        set
        {
            if (SetProperty(ref _newSizeMb, value))
            {
                OnPropertyChanged(nameof(Operation));
                OnPropertyChanged(nameof(DeltaMb));
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(OperationDescription));
            }
        }
    }

    public ResizeOperation Operation => NewSizeMb < CurrentSizeMb ? ResizeOperation.Shrink : ResizeOperation.Extend;

    public long DeltaMb => Math.Abs(NewSizeMb - CurrentSizeMb);

    public bool IsValid => NewSizeMb != CurrentSizeMb
                        && NewSizeMb >= MinNewSizeMb
                        && NewSizeMb <= MaxSizeMb;

    public string OperationDescription => Operation == ResizeOperation.Shrink
        ? $"Shrink by {DeltaMb:N0} MB"
        : $"Extend by {DeltaMb:N0} MB";

    public ResizePartitionViewModel(long currentSizeMb, long availableFreeMb, long maxShrinkMb)
    {
        CurrentSizeMb = currentSizeMb;
        MaxSizeMb     = currentSizeMb + availableFreeMb;
        MaxShrinkMb   = maxShrinkMb;
        _newSizeMb    = currentSizeMb;
    }
}
