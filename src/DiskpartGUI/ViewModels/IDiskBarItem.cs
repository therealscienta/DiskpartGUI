namespace DiskpartGUI.ViewModels;

public interface IDiskBarItem
{
    long SizeBytes { get; }
    double RelativeWidth { get; set; }
}
