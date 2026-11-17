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

    /// <summary>
    /// True if this disk holds the running Windows installation: flagged by the Storage API, or
    /// (fallback) one of its partitions is mounted as the system drive.
    /// </summary>
    public bool IsSystemDisk => Disk.IsSystemDisk || Partitions.Any(p =>
        p.DriveLetter is not null &&
        string.Equals(p.DriveLetter, Environment.GetEnvironmentVariable("SystemDrive"), StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Largest usable (MiB-aligned) unallocated region, or null if there is none.</summary>
    public FreeSpaceRegion? LargestFreeRegion { get; private set; }

    /// <summary>
    /// Merges partitions and the unallocated gaps between them, in disk order. Gaps are
    /// MiB-aligned and exclude the reserved first MiB and the backup-GPT area at the end, so
    /// they show only space a partition can actually use.
    /// </summary>
    public void BuildDisplayItems()
    {
        const long MinFreeSpaceBytes = DiskAlignment.MiB;

        var sorted = Partitions
            .Where(p => p.SizeBytes > 0)
            .OrderBy(p => p.StartingOffset)
            .ToList();

        var items = new List<IDiskBarItem>();
        var free = new List<FreeSpaceRegion>();
        long cursor = DiskAlignment.LeadingReserveBytes;

        void AddGap(long start, long end)
        {
            start = DiskAlignment.AlignUp(start);
            end = DiskAlignment.AlignDown(end);
            if (end - start < MinFreeSpaceBytes) return;
            var region = new FreeSpaceRegion(start, end - start);
            free.Add(region);
            items.Add(new FreeSpaceItemViewModel(region));
        }

        foreach (var p in sorted)
        {
            AddGap(cursor, p.StartingOffset);
            items.Add(p);
            cursor = Math.Max(cursor, p.StartingOffset + p.SizeBytes);
        }
        AddGap(cursor, DiskAlignment.UsableEnd(Disk.SizeBytes));

        LargestFreeRegion = free.OrderByDescending(r => r.SizeBytes).FirstOrDefault();

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
