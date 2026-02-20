namespace DiskpartGUI.Models;

public sealed record DiskInfo(
    int DiskNumber,
    string Model,
    long SizeBytes,
    string MediaType,
    string Status,
    string InterfaceType
);
