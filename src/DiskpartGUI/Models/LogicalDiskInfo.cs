namespace DiskpartGUI.Models;

public sealed record LogicalDiskInfo(
    string DeviceId,
    string? VolumeName,
    string? FileSystem,
    long SizeBytes,
    long FreeSpaceBytes
);
