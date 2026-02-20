namespace DiskpartGUI.Models;

public sealed record FreeSpaceRegion(long StartOffsetBytes, long SizeBytes)
{
    public string DisplayOffset => BytesToHuman(StartOffsetBytes);
    public string DisplaySize => BytesToHuman(SizeBytes);

    public override string ToString() => $"{DisplaySize} free at {DisplayOffset}";

    private static string BytesToHuman(long bytes)
    {
        if (bytes >= 1_099_511_627_776L) return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}
