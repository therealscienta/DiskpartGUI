using System.Buffers.Binary;
using System.ComponentModel;
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
    internal const uint FSCTL_LOCK_VOLUME             = 0x00090018;
    internal const uint FSCTL_UNLOCK_VOLUME           = 0x0009001C;
    internal const uint FSCTL_DISMOUNT_VOLUME         = 0x00090020;
    internal const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX = 0x00070050;
    internal const uint IOCTL_DISK_SET_DRIVE_LAYOUT_EX = 0x0007C050;

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

    internal static int GetPartitionCount(byte[] layout)
        => (int)BinaryPrimitives.ReadUInt32LittleEndian(layout.AsSpan(4));

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
}
