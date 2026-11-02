using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SsdHealthInspector;

/// <summary>
/// Read-only Windows disk enumeration / ATA SMART access using kernel32 DeviceIoControl.
/// No WMI PowerShell process, no smartctl, and no vendor DLL are used.
/// Some RAID/USB drivers do not forward SMART commands; report that honestly.
/// </summary>
internal static class NativeDiskReader
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlDiskGetLengthInfo = 0x0007405C;
    private const uint IoctlStoragePredictFailure = 0x002D1100;
    private const uint SmartRcvDriveData = 0x0007C088;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control,
        [In] byte[] input, int inputSize, [Out] byte[] output, int outputSize,
        out int bytesReturned, IntPtr overlapped);

    internal static SafeFileHandle OpenDisk(int index, uint access = 0) =>
        CreateFile($@"\\.\PhysicalDrive{index}", access, ShareReadWriteDelete,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    internal static bool Query(SafeFileHandle handle, uint code, byte[] input, byte[] output,
        out int returned) =>
        DeviceIoControl(handle, code, input, input.Length, output, output.Length,
            out returned, IntPtr.Zero);

    internal static List<DiskSnapshot> Discover()
    {
        List<DiskSnapshot> disks = new();
        for (int i = 0; i < 64; i++)
        {
            using var handle = OpenDisk(i);
            if (handle.IsInvalid) continue;

            var disk = new DiskSnapshot
            {
                Index = i,
                DeviceID = $@"\\.\PhysicalDrive{i}",
                Model = $"Physical disk {i}",
                MediaType = "Unknown"
            };

            var query = new byte[12]; // STORAGE_PROPERTY_QUERY: header plus AdditionalParameters[1]
            var response = new byte[4096];
            if (Query(handle, IoctlStorageQueryProperty, query, response, out int received) && received >= 36)
            {
                string vendor = ReadAscii(response, (int)BitConverter.ToUInt32(response, 12), received);
                string product = ReadAscii(response, (int)BitConverter.ToUInt32(response, 16), received);
                disk.Model = (vendor.Equals("ATA", StringComparison.OrdinalIgnoreCase) ? "" : vendor + " ") + product;
                disk.Model = disk.Model.Trim();
                if (disk.Model.Length == 0) disk.Model = $"Physical disk {i}";
                disk.Vendor = vendor.Equals("ATA", StringComparison.OrdinalIgnoreCase) ? "" : vendor;
                disk.Firmware = ReadAscii(response, (int)BitConverter.ToUInt32(response, 20), received);
                disk.SerialNumber = ReadAscii(response, (int)BitConverter.ToUInt32(response, 24), received);
                uint busCode = BitConverter.ToUInt32(response, 28);
                disk.BusType = busCode switch
                {
                    1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS",
                    11 => "SATA", 12 => "SD", 14 => "Virtual", 15 => "File-backed",
                    16 => "Storage Spaces", 17 => "NVMe", 20 => "NVMe-oF", _ => $"Bus {busCode}"
                };
            }

            var length = new byte[8];
            using var lengthHandle = OpenDisk(i, GenericRead);
            if (!lengthHandle.IsInvalid && Query(lengthHandle, IoctlDiskGetLengthInfo, Array.Empty<byte>(), length, out int len) && len >= 8)
            {
                long bytes = BitConverter.ToInt64(length, 0);
                if (bytes > 0) disk.SizeBytes = bytes;
            }

            disk.TrimSupported = ReadDescriptorBool(handle, 8); // StorageDeviceTrimProperty
            bool? seeks = ReadDescriptorBool(handle, 7);        // StorageDeviceSeekPenaltyProperty
            disk.IncursSeekPenalty = seeks;
            disk.MediaType = disk.BusType == "NVMe" || seeks == false ? "SSD" :
                seeks == true ? "HDD" : "Unknown";
            if (disk.MediaType == "Unknown" &&
                (disk.Model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                 disk.Model.Contains("Solid State", StringComparison.OrdinalIgnoreCase)))
                disk.MediaType = "SSD (model name)";

            var predict = new byte[516];
            if (Query(handle, IoctlStoragePredictFailure, Array.Empty<byte>(), predict,
                    out int pbytes) && pbytes >= 516)
                disk.PredictFailure = BitConverter.ToUInt32(predict, 0) != 0;

            // ATA SMART is only requested on likely ATA/SATA SSDs or disk classes
            // that often expose an ATA controller. USB and RAID may block it.
            if (disk.BusType is "SATA" or "ATA" or "USB" or "RAID" or "SCSI" ||
                (disk.BusType != "NVMe" && disk.MediaType.StartsWith("SSD", StringComparison.OrdinalIgnoreCase)))
            {
                // Both SMART_RCV_DRIVE_DATA and ATA_PASS_THROUGH require read/write
                // handle access. The commands below only read SMART data/threshold pages.
                using var smartHandle = OpenDisk(i, GenericRead | GenericWrite);
                if (smartHandle.IsInvalid)
                    disk.Diagnostics.Add($"SMART handle: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                else
                {
                    bool readData = TryReadAta(smartHandle, 0xD0, out byte[] smart);
                    string source = "SMART_RCV_DRIVE_DATA";
                    if (!readData)
                    {
                        disk.Diagnostics.Add("Legacy SMART read did not return a valid page; trying ATA pass-through.");
                        readData = TryAtaPassThrough(smartHandle, 0xD0, out smart, out string reason);
                        source = "ATA_PASS_THROUGH";
                        if (!readData) disk.Diagnostics.Add(reason);
                    }
                    if (readData)
                    {
                        disk.SmartData = smart.Select(b => (int)b).ToArray();
                        disk.SmartSource = source;
                        if (TryReadAta(smartHandle, 0xD1, out byte[] thresholds) ||
                            TryAtaPassThrough(smartHandle, 0xD1, out thresholds, out _))
                            disk.SmartThresholds = thresholds.Select(b => (int)b).ToArray();
                        else disk.Diagnostics.Add("SMART thresholds unavailable; attribute status is unknown unless another warning is present.");
                    }
                }
                if (disk.Attributes.Count == 0)
                    disk.Diagnostics.Add("ATA attributes unavailable. Run as administrator; the storage driver or adapter may block SMART commands.");
            }

            if (disk.IsNvme)
            {
                disk.Nvme = NvmeReader.TryRead(i, out string reason);
                disk.NvmeDiagnostic = reason;
            }

            if (disk.Nvme is null && disk.SmartData is null && disk.PredictFailure is null)
                disk.Diagnostics.Add("No direct SMART data available (driver, RAID bridge, permissions, or unsupported hardware).");
            disks.Add(disk);
        }
        return disks;
    }

    private static bool? ReadDescriptorBool(SafeFileHandle disk, uint propertyId)
    {
        byte[] query = new byte[12];
        BitConverter.GetBytes(propertyId).CopyTo(query, 0);
        byte[] buffer = new byte[64];
        // 12-byte descriptor: Version, Size, boolean (byte) at offset 8.
        if (Query(disk, IoctlStorageQueryProperty, query, buffer, out int n) && n >= 9)
            return buffer[8] != 0;
        return null;
    }

    private static bool TryReadAta(SafeFileHandle disk, byte feature, out byte[] data)
    {
        data = Array.Empty<byte>();
        byte[] input = new byte[33];  // SENDCMDINPARAMS, including 1 trailing byte
        byte[] result = new byte[16 + 512]; // SENDCMDOUTPARAMS (16-byte header) + SMART page
        BitConverter.GetBytes(512).CopyTo(input, 0);
        input[4] = feature;   // IDEREGS.bFeaturesReg: 0xD0 attributes / 0xD1 thresholds
        input[5] = 1;         // sector count
        input[6] = 1;         // sector number
        input[7] = 0x4F;      // cylinder low: SMART signature
        input[8] = 0xC2;      // cylinder high
        input[9] = 0xA0;      // drive/head
        input[10] = 0xB0;     // ATA SMART command
        if (!Query(disk, SmartRcvDriveData, input, result, out int returned) || returned < result.Length)
            return false;
        if (result[4] != 0 || result[5] != 0) return false; // DRIVERSTATUS.bDriverError
        var body = result.AsSpan(16, 512);
        if (!ValidSmartPage(body.ToArray(), feature)) return false;
        data = body.ToArray();
        return true;
    }

    private static bool ValidSmartPage(byte[] page, byte feature)
    {
        if (page.Length != 512 || (page[0] == 0 && page[1] == 0)) return false;
        if ((page.Sum(b => (int)b) & 255) != 0) return false;
        return Enumerable.Range(0, 30).Any(i => page[2 + i * 12] is > 0 and < 255);
    }

    private static bool TryAtaPassThrough(SafeFileHandle disk, byte feature,
        out byte[] data, out string reason)
    {
        data = Array.Empty<byte>();
        reason = "";
        int header = IntPtr.Size == 8 ? 48 : 40;
        int pointerOffset = IntPtr.Size == 8 ? 24 : 20;
        int taskOffset = IntPtr.Size == 8 ? 40 : 32;
        const int dataOffset = 512;
        byte[] input = new byte[header];
        byte[] output = new byte[dataOffset + 512];
        BitConverter.GetBytes((ushort)header).CopyTo(input, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(input, 2); // ATA_FLAGS_DATA_IN
        BitConverter.GetBytes(512u).CopyTo(input, 8);
        BitConverter.GetBytes(5u).CopyTo(input, 12);
        if (IntPtr.Size == 8) BitConverter.GetBytes((ulong)dataOffset).CopyTo(input, pointerOffset);
        else BitConverter.GetBytes((uint)dataOffset).CopyTo(input, pointerOffset);
        input[taskOffset] = feature;
        input[taskOffset + 1] = 1;
        input[taskOffset + 2] = 1;
        input[taskOffset + 3] = 0x4F;
        input[taskOffset + 4] = 0xC2;
        input[taskOffset + 5] = 0xA0;
        input[taskOffset + 6] = 0xB0;
        if (!Query(disk, 0x0004D02C, input, output, out int received))
        {
            reason = $"ATA pass-through: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }
        if (received < output.Length || BitConverter.ToUInt32(output, 8) < 512 ||
            (output[taskOffset + 6] & 0xA1) != 0)
        {
            reason = "ATA pass-through returned an error or incomplete SMART page.";
            return false;
        }
        data = output.AsSpan(dataOffset, 512).ToArray();
        if (!ValidSmartPage(data, feature))
        {
            data = Array.Empty<byte>();
            reason = "ATA SMART page failed revision, checksum or attribute validation.";
            return false;
        }
        return true;
    }

    private static string ReadAscii(byte[] buffer, int offset, int bytesReturned)
    {
        if (offset <= 0 || offset >= bytesReturned || offset >= buffer.Length) return "";
        int end = offset;
        int limit = Math.Min(bytesReturned, buffer.Length);
        while (end < limit && buffer[end] != 0 && end - offset < 256) end++;
        return Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
    }
}
