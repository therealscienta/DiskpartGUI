using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskpartGUI.Interop;

/// <summary>
/// Win32 P/Invoke for raw disk and volume operations.
/// All public helpers throw <see cref="Win32Exception"/> on failure.
/// </summary>
internal static class NativeDisk
{
    // ── IOCTL codes ──────────────────────────────────────────────────────────
    internal const uint FSCTL_LOCK_VOLUME              = 0x00090018;
    internal const uint FSCTL_UNLOCK_VOLUME            = 0x0009001C;
    internal const uint FSCTL_DISMOUNT_VOLUME          = 0x00090020;
    internal const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX = 0x00070050;
    internal const uint IOCTL_DISK_SET_DRIVE_LAYOUT_EX = 0x0007C054;
    // Tells partmgr/VDS to re-read the disk layout and notify all subscribers
    internal const uint IOCTL_DISK_UPDATE_PROPERTIES   = 0x00070140;
    // Grows a partition entry directly in partmgr — synchronous, bypasses VDS cache
    // CTL_CODE(IOCTL_DISK_BASE=7, 0x34, METHOD_BUFFERED=0, FILE_READ|WRITE_ACCESS=3)
    internal const uint IOCTL_DISK_GROW_PARTITION      = 0x0007C0D0;
    // Queries NTFS volume metadata (NumberSectors, BytesPerCluster, etc.)
    // CTL_CODE(FILE_DEVICE_FILE_SYSTEM=9, 25, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0)
    internal const uint FSCTL_GET_NTFS_VOLUME_DATA     = 0x00090064;
    // Extends the NTFS filesystem to a new total sector count
    // CTL_CODE(FILE_DEVICE_FILE_SYSTEM=9, 60, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0)
    internal const uint FSCTL_EXTEND_VOLUME            = 0x000900F0;
    // Byte length of a disk or volume
    // CTL_CODE(IOCTL_DISK_BASE=7, 0x17, METHOD_BUFFERED=0, FILE_READ_ACCESS=1)
    internal const uint IOCTL_DISK_GET_LENGTH_INFO     = 0x0007405C;
    // Which disk(s) and byte range(s) a volume occupies
    // CTL_CODE(IOCTL_VOLUME_BASE='V'=0x56, 0, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0)
    internal const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

    internal const uint PARTITION_STYLE_MBR = 0;
    internal const uint PARTITION_STYLE_GPT = 1;

    // ── Access / share / creation flags ──────────────────────────────────────
    private const uint GENERIC_READ       = 0x80000000;
    private const uint GENERIC_WRITE      = 0x40000000;
    private const uint FILE_SHARE_READ    = 0x00000001;
    private const uint FILE_SHARE_WRITE   = 0x00000002;
    private const uint OPEN_EXISTING      = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

    // ── DRIVE_LAYOUT_INFORMATION_EX byte-buffer offsets ──────────────────────
    // struct layout (all little-endian):
    //   [0]  PartitionStyle   : uint   (4)
    //   [4]  PartitionCount   : uint   (4)
    //   [8]  Union (MBR/GPT)  : 40 bytes
    //   [48] PartitionEntry[0]: 144 bytes each
    internal const int DriveLayoutHeaderSize  = 48;
    internal const int PartitionEntrySize     = 144;

    // Offsets within each PARTITION_INFORMATION_EX entry:
    //   [0]  PartitionStyle   : uint  (4) + 4 padding
    //   [8]  StartingOffset   : long  (8)
    //   [16] PartitionLength  : long  (8)
    //   [24] PartitionNumber  : uint  (4)
    //   [28] RewritePartition : byte  (1)
    //   [29] IsServicePartition: byte (1) + 2 padding
    //   [32] Union (MBR or GPT, 112 bytes)
    internal const int EntryStartingOffsetOffset = 8;
    internal const int EntryLengthOffset         = 16;
    internal const int EntryRewriteOffset        = 28;

    // ── P/Invoke declarations ─────────────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        byte[] lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile, long liDistanceToMove,
        out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "FindFirstVolumeW")]
    private static extern IntPtr FindFirstVolume(char[] lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "FindNextVolumeW")]
    private static extern bool FindNextVolume(IntPtr hFindVolume, char[] lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindVolumeClose(IntPtr hFindVolume);

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>Opens \\.\PhysicalDriveN for raw sector access.</summary>
    internal static SafeFileHandle OpenDisk(int diskNumber, bool readWrite)
    {
        var access = readWrite ? GENERIC_READ | GENERIC_WRITE : GENERIC_READ;
        var handle = CreateFile(
            $@"\\.\PhysicalDrive{diskNumber}",
            access,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Cannot open PhysicalDrive{diskNumber}.");
        return handle;
    }

    /// <summary>Opens \\.\X: for volume-level IOCTL calls.</summary>
    internal static SafeFileHandle OpenVolume(string driveLetter)
    {
        // driveLetter may be "C:" or "C:\", normalise to "C:"
        var letter = driveLetter.TrimEnd('\\').TrimEnd('/');
        if (letter.Length == 1) letter += ":";

        var handle = CreateFile(
            $@"\\.\{letter}",
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Cannot open volume {letter}.");
        return handle;
    }

    /// <summary>Opens a volume by device path (e.g. <c>\\?\Volume{guid}</c>, no trailing slash) for lock/dismount.</summary>
    internal static SafeFileHandle OpenVolumeDevice(string devicePath)
    {
        var handle = CreateFile(
            devicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open volume {devicePath}.");
        return handle;
    }

    /// <summary>
    /// Finds the mounted volume that lives in the partition starting at
    /// <paramref name="partitionOffset"/> on disk <paramref name="diskNumber"/>, whether or not
    /// it has a drive letter. Returns its device path (<c>\\?\Volume{guid}</c>) or null if no
    /// volume sits there (e.g. an MSR or unrecognised partition).
    /// </summary>
    internal static string? FindVolumeDevicePath(int diskNumber, long partitionOffset)
    {
        var name = new char[260];
        var find = FindFirstVolume(name, (uint)name.Length);
        if (find == INVALID_HANDLE_VALUE) return null;
        try
        {
            do
            {
                // FindFirstVolume returns "\\?\Volume{guid}\"; the device itself has no trailing slash.
                var end = Array.IndexOf(name, '\0');
                var volumeName = new string(name, 0, end < 0 ? name.Length : end);
                var devicePath = volumeName.TrimEnd('\\');
                if (VolumeStartsAt(devicePath, diskNumber, partitionOffset))
                    return devicePath;
            }
            while (FindNextVolume(find, name, (uint)name.Length));
        }
        finally
        {
            FindVolumeClose(find);
        }
        return null;
    }

    private static bool VolumeStartsAt(string devicePath, int diskNumber, long partitionOffset)
    {
        // Access 0 is enough for IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS (FILE_ANY_ACCESS).
        using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return false;

        // VOLUME_DISK_EXTENTS: [0] NumberOfDiskExtents (DWORD) + 4 padding,
        // then DISK_EXTENT[]: { DWORD DiskNumber; 4 padding; LARGE_INTEGER StartingOffset; LARGE_INTEGER ExtentLength } = 24 bytes
        var buf = new byte[8 + 24 * 8];
        if (!DeviceIoControl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                IntPtr.Zero, 0, buf, (uint)buf.Length, out _, IntPtr.Zero))
            return false;

        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0));
        for (int i = 0; i < count && 8 + 24 * (i + 1) <= buf.Length; i++)
        {
            int pos = 8 + 24 * i;
            if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(pos)) == (uint)diskNumber &&
                BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(pos + 8)) == partitionOffset)
                return true;
        }
        return false;
    }

    /// <summary>Byte length of a disk or volume (IOCTL_DISK_GET_LENGTH_INFO).</summary>
    internal static long GetLength(SafeFileHandle handle)
    {
        var buf = new byte[8];
        if (!DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO,
                IntPtr.Zero, 0, buf, (uint)buf.Length, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "IOCTL_DISK_GET_LENGTH_INFO failed.");
        return BinaryPrimitives.ReadInt64LittleEndian(buf);
    }

    /// <summary>Forces data still in the OS or drive write cache onto the disk.</summary>
    internal static void Flush(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "FlushFileBuffers failed.");
    }

    /// <summary>Sends a no-input/no-output IOCTL (e.g. lock/dismount/unlock).</summary>
    internal static void IoctlSimple(SafeFileHandle handle, uint ioctl, string description)
    {
        if (!DeviceIoControl(handle, ioctl,
                IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), description);
    }

    /// <summary>Reads the full drive layout into a byte buffer.</summary>
    internal static byte[] GetDriveLayout(SafeFileHandle diskHandle)
    {
        // Start with a buffer large enough for 128 partition entries.
        var bufSize = (uint)(DriveLayoutHeaderSize + 128 * PartitionEntrySize);
        var buf = new byte[bufSize];

        if (!DeviceIoControl(diskHandle, IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                IntPtr.Zero, 0, buf, bufSize, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "IOCTL_DISK_GET_DRIVE_LAYOUT_EX failed.");
        return buf;
    }

    /// <summary>Writes a modified layout buffer back to the disk.</summary>
    internal static void SetDriveLayout(SafeFileHandle diskHandle, byte[] layout)
    {
        if (!DeviceIoControl(diskHandle, IOCTL_DISK_SET_DRIVE_LAYOUT_EX,
                layout, (uint)layout.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "IOCTL_DISK_SET_DRIVE_LAYOUT_EX failed.");
    }

    /// <summary>Seeks to an absolute byte offset on the disk handle.</summary>
    internal static void SeekTo(SafeFileHandle handle, long offsetBytes)
    {
        if (!SetFilePointerEx(handle, offsetBytes, out _, 0 /* FILE_BEGIN */))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SetFilePointerEx to {offsetBytes} failed.");
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes from the current position.</summary>
    internal static void ReadBytes(SafeFileHandle handle, byte[] buffer, int count)
    {
        if (!ReadFile(handle, buffer, (uint)count, out var read, IntPtr.Zero) || read != count)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"ReadFile: requested {count}, got {read}.");
    }

    /// <summary>Writes exactly <paramref name="count"/> bytes at the current position.</summary>
    internal static void WriteBytes(SafeFileHandle handle, byte[] buffer, int count)
    {
        if (!WriteFile(handle, buffer, (uint)count, out var written, IntPtr.Zero) || written != count)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"WriteFile: requested {count}, wrote {written}.");
    }

    // ── Layout buffer helpers (BinaryPrimitives, no unsafe) ──────────────────

    internal static uint GetPartitionStyle(byte[] layout)
        => BinaryPrimitives.ReadUInt32LittleEndian(layout.AsSpan(0));

    internal static int GetPartitionCount(byte[] layout)
        => (int)BinaryPrimitives.ReadUInt32LittleEndian(layout.AsSpan(4));

    /// <summary>
    /// GPT only: the byte range partitions may occupy (between the primary and backup GPT).
    /// DRIVE_LAYOUT_INFORMATION_GPT at [8]: GUID DiskId (16), LARGE_INTEGER StartingUsableOffset
    /// at [24], LARGE_INTEGER UsableLength at [32], ULONG MaxPartitionCount at [40].
    /// </summary>
    internal static (long Start, long Length) GetGptUsableRange(byte[] layout)
        => (BinaryPrimitives.ReadInt64LittleEndian(layout.AsSpan(24)),
            BinaryPrimitives.ReadInt64LittleEndian(layout.AsSpan(32)));

    internal static long ReadEntryLength(byte[] layout, int entryIndex)
    {
        int pos = DriveLayoutHeaderSize + entryIndex * PartitionEntrySize + EntryLengthOffset;
        return BinaryPrimitives.ReadInt64LittleEndian(layout.AsSpan(pos));
    }

    internal static long ReadEntryStartingOffset(byte[] layout, int entryIndex)
    {
        int pos = DriveLayoutHeaderSize + entryIndex * PartitionEntrySize + EntryStartingOffsetOffset;
        return BinaryPrimitives.ReadInt64LittleEndian(layout.AsSpan(pos));
    }

    internal static void WriteEntryStartingOffset(byte[] layout, int entryIndex, long value)
    {
        int pos = DriveLayoutHeaderSize + entryIndex * PartitionEntrySize + EntryStartingOffsetOffset;
        BinaryPrimitives.WriteInt64LittleEndian(layout.AsSpan(pos), value);
    }

    internal static void SetEntryRewritePartition(byte[] layout, int entryIndex)
    {
        int pos = DriveLayoutHeaderSize + entryIndex * PartitionEntrySize + EntryRewriteOffset;
        layout[pos] = 1;
    }

    internal static uint ReadEntryPartitionNumber(byte[] layout, int entryIndex)
    {
        int pos = DriveLayoutHeaderSize + entryIndex * PartitionEntrySize + 24; // PartitionNumber offset
        return BinaryPrimitives.ReadUInt32LittleEndian(layout.AsSpan(pos));
    }

    /// <summary>
    /// Grows the on-disk partition entry by <paramref name="bytesToGrow"/> bytes.
    /// Calls IOCTL_DISK_GROW_PARTITION which updates partmgr synchronously — no VDS involvement.
    /// </summary>
    internal static void GrowPartition(SafeFileHandle diskHandle, uint partitionNumber, long bytesToGrow)
    {
        // DISK_GROW_PARTITION struct layout:
        //   [0..3]  PartitionNumber (ULONG)
        //   [4..7]  padding (LARGE_INTEGER is 8-byte aligned)
        //   [8..15] BytesToGrow (LARGE_INTEGER)
        var buf = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), partitionNumber);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(8), bytesToGrow);

        if (!DeviceIoControl(diskHandle, IOCTL_DISK_GROW_PARTITION,
                buf, (uint)buf.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "IOCTL_DISK_GROW_PARTITION failed.");
    }

    /// <summary>
    /// Reads NumberSectors and BytesPerSector from FSCTL_GET_NTFS_VOLUME_DATA.
    /// Fails (ERROR_INVALID_FUNCTION) on anything that isn't NTFS.
    /// </summary>
    internal static (long NumberSectors, long BytesPerSector) GetNtfsVolumeData(SafeFileHandle volumeHandle)
    {
        // NTFS_VOLUME_DATA_BUFFER layout:
        //   [8]  NumberSectors  : LONGLONG  — sector count using the volume's own sector size
        //   [40] BytesPerSector : DWORD     — NTFS logical sector size (512 on most disks, 4096 on 4Kn)
        var ntfsData = new byte[120];
        if (!DeviceIoControl(volumeHandle, FSCTL_GET_NTFS_VOLUME_DATA,
                IntPtr.Zero, 0, ntfsData, (uint)ntfsData.Length, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "This volume is not NTFS (FSCTL_GET_NTFS_VOLUME_DATA failed).");

        return (BinaryPrimitives.ReadInt64LittleEndian(ntfsData.AsSpan(8)),
                BinaryPrimitives.ReadUInt32LittleEndian(ntfsData.AsSpan(40)));
    }

    /// <summary>
    /// Extends the NTFS filesystem to fill its (already grown) partition. The target size comes
    /// from the volume's current length rather than "old size + delta", so it is also correct
    /// when retrying after an earlier attempt failed. NTFS keeps its backup boot sector in the
    /// partition's last sector, so the filesystem spans every sector but that one.
    /// </summary>
    /// <exception cref="IOException">The volume doesn't report the larger size yet (PnP still settling).</exception>
    internal static void ExtendNtfsToFillVolume(SafeFileHandle volumeHandle)
    {
        var (currentSectors, bytesPerSector) = GetNtfsVolumeData(volumeHandle);
        long newSectors = GetLength(volumeHandle) / bytesPerSector - 1;

        if (newSectors <= currentSectors)
            throw new IOException("The volume does not report the larger partition size yet.");

        var buf = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(0), newSectors);

        if (!DeviceIoControl(volumeHandle, FSCTL_EXTEND_VOLUME,
                buf, (uint)buf.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "FSCTL_EXTEND_VOLUME failed.");
    }
}
