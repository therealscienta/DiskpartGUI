using System.Collections.ObjectModel;
using DiskpartGUI.Models;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class DiskItemViewModel : ViewModelBase
{
    private object? _selectedItem;

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
    public ObservableCollection<IDiskBarItem> DisplayItems { get; } = [];

    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                OnPropertyChanged(nameof(SelectedPartition));
        }
    }

    public PartitionItemViewModel? SelectedPartition => _selectedItem as PartitionItemViewModel;

    public DiskItemViewModel(DiskInfo disk)
    {
        Disk = disk;
    }

    public void BuildDisplayItems()
    {
        const long MinFreeSpaceBytes = 1L * 1024 * 1024; // 1 MB

        var sorted = Partitions
            .Where(p => p.SizeBytes > 0)
            .OrderBy(p => p.StartingOffset)
            .ToList();

        var items = new List<IDiskBarItem>();
        long cursor = 0L;

        foreach (var p in sorted)
        {
            long gap = p.StartingOffset - cursor;
            if (gap >= MinFreeSpaceBytes)
                items.Add(new FreeSpaceItemViewModel(new FreeSpaceRegion(cursor, gap)));
            items.Add(p);
            cursor = p.StartingOffset + p.SizeBytes;
        }

        long trailingGap = Disk.SizeBytes - cursor;
        if (trailingGap >= MinFreeSpaceBytes)
            items.Add(new FreeSpaceItemViewModel(new FreeSpaceRegion(cursor, trailingGap)));

        DisplayItems.Clear();
        foreach (var item in items)
            DisplayItems.Add(item);
    }

    private static string BytesToHuman(long bytes)
    {
        if (bytes >= 1_099_511_627_776L) return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}
