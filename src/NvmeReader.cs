using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SsdHealthInspector;

/// <summary>
/// Read-only Windows NVMe SMART/Health Information Log (log page 0x02).
/// IOCTL_STORAGE_QUERY_PROPERTY / StorageDeviceProtocolSpecificProperty.
/// No vendor DLLs, no smartctl, and no writes to the drive.
/// </summary>
public static class NvmeReader
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int ProtocolDataOffset = 8;
    private const int ProtocolStructBytes = 40;
    private const int SmartLogBytes = 512;
    private const int BufferSize = ProtocolDataOffset + ProtocolStructBytes + SmartLogBytes;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint controlCode,
        [In] byte[] input, int inputSize,
        [Out] byte[] output, int outputSize,
        out int bytesReturned, IntPtr overlapped);

    public static NvmeHealth? TryRead(int diskNumber, out string diagnostic)
    {
        diagnostic = "";
        if (diskNumber < 0)
        {
            diagnostic = "Invalid disk number.";
            return null;
        }

        // Zero desired access works for many property queries, and avoids
        // requesting read access unnecessarily. UAC elevation is still required.
        using var handle = CreateFile($@"\\.\PhysicalDrive{diskNumber}",
            0, 0x00000007, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            diagnostic = "Cannot open physical drive: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return null;
        }

        var input = new byte[BufferSize];
        var output = new byte[BufferSize];

        // STORAGE_PROPERTY_QUERY (8 bytes): PropertyId 50, QueryType 0.
        // STORAGE_PROTOCOL_SPECIFIC_DATA starts immediately after the 8-byte header.
        WriteUInt32(input, 0, 50);      // StorageDeviceProtocolSpecificProperty
        WriteUInt32(input, 8, 3);      // ProtocolTypeNvme
        WriteUInt32(input, 12, 2);     // NVMeDataTypeLogPage
        WriteUInt32(input, 16, 2);     // NVMe SMART log page 0x02
        WriteUInt32(input, 24, ProtocolStructBytes);  // offset from protocol data structure
        WriteUInt32(input, 28, SmartLogBytes);

        if (!DeviceIoControl(handle, IoctlStorageQueryProperty,
                input, input.Length, output, output.Length,
                out int returned, IntPtr.Zero))
        {
            diagnostic = "NVMe SMART query not available: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return null;
        }

        // STORAGE_PROTOCOL_DATA_DESCRIPTOR: Version, Size, ProtocolSpecificData.
        if (returned < 8 + ProtocolStructBytes)
        {
            diagnostic = "NVMe query returned an incomplete descriptor.";
            return null;
        }
        int dataOffset = checked((int)BitConverter.ToUInt32(output, ProtocolDataOffset + 16));
        int dataLength = checked((int)BitConverter.ToUInt32(output, ProtocolDataOffset + 20));
        int start = ProtocolDataOffset + dataOffset;
        if (dataOffset < ProtocolStructBytes || dataLength < SmartLogBytes ||
            start > output.Length - SmartLogBytes || start > returned - SmartLogBytes)
        {
            diagnostic = "NVMe query did not return a complete 512-byte SMART health log.";
            return null;
        }

        if (output.AsSpan(start, SmartLogBytes).IndexOfAnyExcept((byte)0) < 0)
        {
            diagnostic = "NVMe SMART returned an empty health log; no verdict can be trusted.";
            return null;
        }

        ushort kelvin = BitConverter.ToUInt16(output, start + 1);
        // A valid NVMe composite temperature is reported in Kelvin.
        int? celsius = kelvin is >= 200 and <= 600 ? kelvin - 273 : null;
        int spare = output[start + 3];
        int spareThreshold = output[start + 4];
        int used = output[start + 5];
        if (spare > 100 || spareThreshold > 100)
        {
            diagnostic = "NVMe SMART returned implausible spare-capacity values.";
            return null;
        }

        diagnostic = "NVMe SMART/Health log page 0x02 read successfully.";
        return new NvmeHealth
        {
            CriticalWarning = output[start],
            TemperatureC = celsius,
            AvailableSpare = spare,
            SpareThreshold = spareThreshold,
            PercentageUsed = used,
            PowerOnHours = UInt128(output, start + 128),
            UnsafeShutdowns = UInt128(output, start + 144),
            MediaErrors = UInt128(output, start + 160),
            ErrorLogEntries = UInt128(output, start + 176)
        };
    }

    private static BigInteger UInt128(byte[] data, int offset) =>
        new BigInteger(new ReadOnlySpan<byte>(data, offset, 16), isUnsigned: true, isBigEndian: false);

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BitConverter.GetBytes(value).CopyTo(data, offset);
}
