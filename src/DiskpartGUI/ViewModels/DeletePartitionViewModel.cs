using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class DeletePartitionViewModel : ViewModelBase
{
    private bool _forceOverride;
    private bool _confirmed;

    public string PartitionDescription { get; }
    public bool IsSystemOrBoot { get; }

    public string WarningMessage => IsSystemOrBoot
        ? "WARNING: This partition is marked as bootable or active. Deleting it may make the system unbootable."
        : "This action is permanent and cannot be undone.";

    public bool ForceOverride
    {
        get => _forceOverride;
        set
        {
            if (SetProperty(ref _forceOverride, value))
                OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public bool Confirmed
    {
        get => _confirmed;
        set
        {
            if (SetProperty(ref _confirmed, value))
                OnPropertyChanged(nameof(CanConfirm));
        }
    }

    // If system/boot partition, user must explicitly check ForceOverride before confirming
    public bool CanConfirm => !IsSystemOrBoot || ForceOverride;

    public DeletePartitionViewModel(string partitionDescription, bool isSystemOrBoot)
    {
        PartitionDescription = partitionDescription;
        IsSystemOrBoot = isSystemOrBoot;
    }
}
