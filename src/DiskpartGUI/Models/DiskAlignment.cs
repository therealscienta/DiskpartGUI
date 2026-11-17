namespace DiskpartGUI.Models;

/// <summary>
/// Reserved areas and alignment rules shared by the free-space display, Add Partition, Extend
/// and Move, so every feature agrees on where a partition may go.
/// </summary>
public static class DiskAlignment
{
    public const long MiB = 1024L * 1024;

    /// <summary>
    /// The first MiB holds the MBR / GPT header and partition entries. Windows never places a
    /// partition below it, and nothing may be written there.
    /// </summary>
    public const long LeadingReserveBytes = MiB;

    /// <summary>Room for the backup GPT at the end of the disk (33 sectors, 132 KiB on 4Kn disks).</summary>
    public const long TrailingReserveBytes = 33 * 4096;

    public static long AlignUp(long value, long alignment = MiB)
        => (value + alignment - 1) / alignment * alignment;

    public static long AlignDown(long value, long alignment = MiB)
        => value / alignment * alignment;

    /// <summary>Exclusive, MiB-aligned end offset a partition may extend to.</summary>
    public static long UsableEnd(long diskSizeBytes)
        => Math.Max(0, AlignDown(diskSizeBytes - TrailingReserveBytes));
}
