using DiskpartGUI.Models;
using DiskpartGUI.ViewModels.Infrastructure;

namespace DiskpartGUI.ViewModels;

public sealed class FreeSpaceItemViewModel : ViewModelBase, IDiskBarItem
{
    private double _relativeWidth;

    public long SizeBytes { get; }
    public long StartingOffset { get; }
    public string DisplaySize { get; }
    public string DisplayOffset { get; }
    public string Type => "Unallocated";

    public double RelativeWidth
    {
        get => _relativeWidth;
        set => SetProperty(ref _relativeWidth, value);
    }

    // Null/default values for DataGrid column compatibility
    public int? PartitionIndex => null;
    public string? DriveLetter => null;
    public string? VolumeName => null;
    public string? FileSystem => null;
    public bool IsBootable => false;

    public FreeSpaceItemViewModel(FreeSpaceRegion region)
    {
        SizeBytes = region.SizeBytes;
        StartingOffset = region.StartOffsetBytes;
        DisplaySize = region.DisplaySize;
        DisplayOffset = region.DisplayOffset;
    }
}
