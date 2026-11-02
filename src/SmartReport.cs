using System.Net;
using System.Text;

namespace SsdHealthInspector;

public static class SmartReport
{
    public static bool IsSamsung(DiskSnapshot disk) => disk.Model.Contains("Samsung SSD", StringComparison.OrdinalIgnoreCase);
    public static string Name(DiskSnapshot disk, SmartAttribute a) => IsSamsung(disk) ? a.Id switch
    {
        5 => "Reallocation Sector Count", 9 => "Power-On Hours Count", 12 => "Device Power Cycle Count",
        177 => "Wear Leveling Count", 179 => "Used Rsvd Block Count Total", 181 => "Program Fail Count Total",
        182 => "Erase Fail Count Total", 183 => "Runtime Bad Block", 187 => "Reported Uncorrectable Error",
        190 => "Airflow Temperature", 195 => "ECC Rate", 199 => "UltraDMA CRC Errors Count",
        235 => "POR Recovery Count", 241 => "Total Host Writes", _ => a.Name
    } : a.Name;
    public static string Raw(SmartAttribute a)
    {
        string hex = a.Raw.ToString("X12");
        return hex[..8] + "-" + hex[8..];
    }
    public static string State(SmartAttribute a) => a.ThresholdFailed ? "FAIL" :
        a.Id is 5 or 187 or 197 or 198 or 199 && a.Raw > 0 ? "REVIEW" :
        a.Threshold is null ? "UNKNOWN" : "OK";
    public static string Colour(string state) => state switch
    {
        "FAIL" => "#bb2330", "REVIEW" => "#a96a00", "OK" => "#138333", _ => "#727985"
    };
    public static string Vendor(DiskSnapshot d) => IsSamsung(d) ? "Samsung" :
        string.IsNullOrWhiteSpace(d.Vendor) ? "Not reported" : d.Vendor;
    public static string Details(DiskSnapshot d) =>
        $"Model: {d.Model}\r\nFirmware: {(d.Firmware.Length > 0 ? d.Firmware : "Not reported")}\r\n" +
        $"Serial: {d.SerialNumber}\r\nCapacity: {(d.SizeBytes > 0 ? $"{d.SizeBytes / 1_000_000_000d:0.##} GB ({d.SizeBytes:N0} bytes)" : "Not reported")}\r\n" +
        $"Report by: SSD Health Inspector\r\nScan date: {d.ScannedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n" +
        $"Detected vendor: {Vendor(d)}     Bus: {d.BusType}";

    public static string Html(DiskSnapshot disk)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        var decision = HealthEvaluator.Evaluate(disk);
        var b = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>SSD S.M.A.R.T. Report</title><style>body{font:13px Arial,sans-serif;color:#001748;margin:24px;max-width:950px}h1{font-size:19px;color:navy;margin-bottom:32px}pre{font:inherit;white-space:pre-wrap;line-height:1.5}table{border-collapse:collapse;width:100%;margin:24px 0}td,th{border:1px solid #434b59;padding:5px 7px;text-align:center}th{background:#f2f5fa;font-weight:normal}td:nth-child(3){text-align:left}code{white-space:nowrap}.dot{font-size:20px;line-height:1}small{color:#596273}.note{padding:12px;background:#f4f6f9}@media print{body{margin:0}tr{break-inside:avoid}thead{display:table-header-group}}</style><body><h1>SSD Health Inspector S.M.A.R.T. Report</h1>");
        b.Append($"<p><strong>State: {E(decision.Headline)}</strong></p><pre>{E(Details(disk))}</pre>");
        b.Append("<table><thead><tr><th>State</th><th>Num</th><th>Attribute Name</th><th>Value</th><th>Worst</th><th>Raw (hex)</th><th>Threshold</th></tr></thead><tbody>");
        foreach (var a in disk.Attributes)
        {
            string state = State(a);
            b.Append($"<tr><td title=\"{state}\"><span class=\"dot\" style=\"color:{Colour(state)}\">●</span> <small>{state}</small></td><td>{a.Id:000}</td><td>{E(Name(disk, a))}</td><td>{a.Current:000}</td><td>{a.Worst:000}</td><td><code>{Raw(a)}</code></td><td>{a.Threshold?.ToString("000") ?? "—"}</td></tr>");
        }
        if (disk.Attributes.Count == 0) b.Append("<tr><td colspan=\"7\">No ATA SMART attributes available. See diagnostics below.</td></tr>");
        b.Append("</tbody></table><small>Green: no threshold crossing detected · Amber: review counter · Red: threshold crossed · Grey: threshold unavailable. Raw counters are vendor-specific; a green dot is not a lifetime guarantee.</small>");
        b.Append($"<p class=\"note\">{E(decision.Recommendation)}</p><h2>Findings</h2><ul>");
        foreach (string f in decision.Findings) b.Append($"<li>{E(f)}</li>");
        b.Append($"</ul><p>SMART source: {E(disk.SmartSource)}</p>");
        if (disk.Nvme is { } n) b.Append($"<h2>NVMe health</h2><pre>Critical warning: 0x{n.CriticalWarning:X2}\nTemperature: {n.TemperatureC} °C\nLifetime used: {n.PercentageUsed}%\nSpare: {n.AvailableSpare}%\nMedia errors: {n.MediaErrors}\nPower-on hours: {n.PowerOnHours}</pre>");
        b.Append($"<h2>Diagnostics</h2><pre>{E(string.Join("\n", disk.Diagnostics))}\n{E(disk.NvmeDiagnostic)}</pre></body></html>");
        return b.ToString();
    }
}
