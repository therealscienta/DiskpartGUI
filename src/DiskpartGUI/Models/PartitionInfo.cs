namespace DiskpartGUI.Models;

public sealed record PartitionInfo(
    int DiskIndex,
    int PartitionIndex,
    long StartingOffset,
    long SizeBytes,
    string Type,
    bool IsBootable,
    bool IsActive,
    string? DriveLetter
);
