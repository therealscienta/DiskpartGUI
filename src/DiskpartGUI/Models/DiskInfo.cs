namespace DiskpartGUI.Models;

/// <param name="IsSystemDisk">
/// True when the disk holds the running Windows installation or its system (EFI) partition
/// (<c>MSFT_Disk.IsBoot</c> / <c>IsSystem</c>). Such a disk must never be wiped.
/// </param>
public sealed record DiskInfo(
    int DiskNumber,
    string Model,
    long SizeBytes,
    string MediaType,
    string Status,
    string InterfaceType,
    bool IsSystemDisk = false
);
