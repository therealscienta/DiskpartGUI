using DiskpartGUI.Models;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class PartitionItemViewModel : ViewModelBase
{
    public PartitionInfo Partition { get; }
    public LogicalDiskInfo? LogicalDisk { get; }

    public int PartitionIndex => Partition.PartitionIndex;
    public long SizeBytes => Partition.SizeBytes;
    public long StartingOffset => Partition.StartingOffset;
    public string Type => Partition.Type;
    public bool IsBootable => Partition.IsBootable;
    public bool IsActive => Partition.IsActive;

    public string? DriveLetter => LogicalDisk?.DeviceId;
    public string? FileSystem => LogicalDisk?.FileSystem;
    public string? VolumeName => LogicalDisk?.VolumeName;

    public string DisplaySize => BytesToHuman(SizeBytes);
    public string DisplayOffset => BytesToHuman(StartingOffset);

    private double _relativeWidth;
    public double RelativeWidth
    {
        get => _relativeWidth;
        set => SetProperty(ref _relativeWidth, value);
    }

    public PartitionItemViewModel(PartitionInfo partition, LogicalDiskInfo? logicalDisk)
    {
        Partition = partition;
        LogicalDisk = logicalDisk;
    }

    private static string BytesToHuman(long bytes)
    {
        if (bytes >= 1_099_511_627_776L) return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}
