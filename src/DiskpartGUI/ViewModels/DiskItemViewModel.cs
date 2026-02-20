using System.Collections.ObjectModel;
using DiskpartGUI.Models;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class DiskItemViewModel : ViewModelBase
{
    private PartitionItemViewModel? _selectedPartition;

    public DiskInfo Disk { get; }

    public int DiskNumber => Disk.DiskNumber;
    public string Model => Disk.Model;
    public long SizeBytes => Disk.SizeBytes;
    public string MediaType => Disk.MediaType;
    public string InterfaceType => Disk.InterfaceType;
    public string Status => Disk.Status;

    public string DisplaySize => BytesToHuman(SizeBytes);
    public string Header => $"Disk {DiskNumber}  —  {Model}  ({DisplaySize})";

    public ObservableCollection<PartitionItemViewModel> Partitions { get; } = [];

    public PartitionItemViewModel? SelectedPartition
    {
        get => _selectedPartition;
        set => SetProperty(ref _selectedPartition, value);
    }

    public DiskItemViewModel(DiskInfo disk)
    {
        Disk = disk;
    }

    private static string BytesToHuman(long bytes)
    {
        if (bytes >= 1_099_511_627_776L) return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}
