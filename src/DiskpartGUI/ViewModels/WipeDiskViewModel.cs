using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class WipeDiskViewModel : ViewModelBase
{
    private bool _confirmed;

    public string DiskDescription { get; }

    /// <summary>One line per partition that will be erased, e.g. "Partition 3 — E: (300 KB, Basic Data)".</summary>
    public IReadOnlyList<string> PartitionSummaries { get; }

    public bool HasPartitions => PartitionSummaries.Count > 0;

    public string ConfirmationText => $"I understand that everything on {DiskDescription} will be erased.";

    public string WarningMessage =>
        "Wipe removes every partition and volume on this disk and leaves it uninitialized. " +
        "This cannot be undone.";

    public bool Confirmed
    {
        get => _confirmed;
        set
        {
            if (SetProperty(ref _confirmed, value))
                OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public bool CanConfirm => Confirmed;

    public WipeDiskViewModel(string diskDescription, IReadOnlyList<string> partitionSummaries)
    {
        DiskDescription = diskDescription;
        PartitionSummaries = partitionSummaries;
    }
}
