namespace DiskpartGUI.Models;

public sealed record MoveProgress(long BytesCopied, long TotalBytes, double SpeedBytesPerSecond)
{
    public double Percent => TotalBytes > 0 ? BytesCopied * 100.0 / TotalBytes : 0;

    public string StatusText =>
        $"{BytesToHuman(BytesCopied)} / {BytesToHuman(TotalBytes)}  —  {BytesToHuman((long)SpeedBytesPerSecond)}/s";

    private static string BytesToHuman(long bytes)
    {
        if (bytes >= 1_099_511_627_776L) return $"{bytes / 1_099_511_627_776.0:F1} TB";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}
