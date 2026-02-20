using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class AddPartitionViewModel : ViewModelBase
{
    private long _sizeMb;
    private string _label = "New Volume";
    private string _fileSystem = "NTFS";

    public long AvailableSpaceMb { get; }

    public long SizeMb
    {
        get => _sizeMb;
        set
        {
            if (SetProperty(ref _sizeMb, value))
                OnPropertyChanged(nameof(IsValid));
        }
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string FileSystem
    {
        get => _fileSystem;
        set => SetProperty(ref _fileSystem, value);
    }

    public IReadOnlyList<string> FileSystems { get; } = ["NTFS", "FAT32", "exFAT"];

    public bool IsValid => SizeMb > 0 && SizeMb <= AvailableSpaceMb;

    public AddPartitionViewModel(long availableSpaceMb)
    {
        AvailableSpaceMb = availableSpaceMb;
        _sizeMb = Math.Min(1024, availableSpaceMb); // default to 1 GB or max available
    }
}
