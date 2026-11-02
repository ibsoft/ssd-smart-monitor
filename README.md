# SSD Health Inspector — SMART Report edition

Windows 10/11 x64, .NET 8 Windows Forms. Report-style interface based on the supplied HDDScan example, using this application's own name and live readings.

## Build and run

1. Extract the ZIP on a Windows build PC with the .NET 8 SDK installed.
2. Run `BUILD-WINDOWS-X64.cmd`.
3. The self-contained single-file application is `publish\SsdHealthInspector.exe`.
4. Run that EXE as administrator on the PC whose disks you want to inspect. No .NET installation is required on target PCs.

This archive contains source code, not a prebuilt EXE.

## Report layout

Select a physical disk at the top. The default SMART Report tab shows:

- State, model, firmware, serial number, capacity, scan timestamp and detected vendor.
- Coloured indicators, decimal attribute IDs, attribute names, normalized Value / Worst, 48-bit Raw (hex) and Threshold.
- Samsung SSD labels for the attributes shown in the reference screenshot.
- Raw hexadecimal formatted as `00000000-01A1`; hover over a raw value for decimal.
- Save HTML report exports the selected disk to a self-contained printable report. Open the HTML in a browser and print to PDF if desired.
- Export JSON exports every scanned disk. CLI JSON arguments are retained.

The graphic and HDDScan branding are not included. Values are read from the selected disk, never copied from the reference screenshot. Capacity is shown in GB and bytes; no sector count is guessed from an assumed sector size. Raw temperature stays hexadecimal to preserve the actual counter.

## Indicators and interpretation

Green: an available threshold was not crossed, with no selected counter warning.
Amber: a counter needs review (for example non-zero CRC errors).
Red: a normalized value reached or fell below a positive threshold.
Grey: no threshold was returned.

A zero threshold is displayed as `000` and does not trigger a threshold failure. A missing threshold is displayed as `—` and is not converted to zero. Green dots do not guarantee remaining life or absence of all faults. Vendor-specific counters should be interpreted using the manufacturer's documentation.

A Windows failure-prediction flag of False, without detailed health data, now produces an unknown assessment. Missing ATA thresholds also prevent a healthy verdict when ATA is the only detailed source. Confirmed warnings still take priority. NVMe health is retained in Findings and Diagnostics / NVMe and in the HTML / JSON exports; NVMe drives do not expose an ATA attribute table.

## SMART reading changes

- SMART requests now use the read/write handle access required by the Windows IOCTL; this is an access permission, not a disk-write operation.
- Only SMART READ DATA and READ THRESHOLDS commands are sent. No tests, erase operations or configuration commands are issued.
- Added ATA_PASS_THROUGH fallback with pointer-size-aware headers and validation of returned ATA status, length, page revision and checksum.
- Thresholds use the same permitted handle and fallback path.
- Disk capacity uses a readable handle.
- Diagnostics describe failed reads and unsupported access. Some USB / RAID bridges still block both methods; no universal bridge support is claimed.
- Fixed CLI exit-code aggregation so an unknown drive is not masked by another healthy drive.

Microsoft API references:
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddscsi/ns-ntddscsi-_ata_pass_through_ex
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddscsi/ni-ntddscsi-ioctl_ata_pass_through

## Validation

All application C# files were compiled together using the .NET 8 Roslyn compiler with .NET 8 and Windows Desktop reference assemblies. Seventeen executable regression checks passed for parsing, threshold decisions, report values, HTML escaping, JSON, NVMe evaluation and SMART page validation.

The normal SDK publish command could not run in the build environment because its process-information API is unavailable. Full self-contained publishing, native Windows UI rendering and live SSD/driver access have not been tested here. The supplied Windows build script performs the self-contained publish on your Windows build PC.

Run regression checks on a machine with the .NET 8 SDK:

    dotnet run --project tests\Regression.csproj

After building, verify both Samsung disks: model/serial match the selected device, firmware/capacity are populated, attributes and thresholds match a vendor tool, and HTML/JSON exports preserve the readings. If no attributes appear, copy the Diagnostics tab text for troubleshooting.

## CLI

    SsdHealthInspector.exe --json
    SsdHealthInspector.exe --json --output health.json

Exit codes: 0 = no warning detected; 1 = warning or unknown; 2 = replace soon; 3 = replace now; 4 = scan error.
