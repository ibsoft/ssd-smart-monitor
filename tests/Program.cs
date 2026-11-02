using SsdHealthInspector;
using System.Reflection;
using System.Text.Json;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
DiskSnapshot Make(int id = 177, int current = 72, int? threshold = 0, ulong raw = 417)
{
    int[] data = new int[512]; data[0] = 1; data[2] = id; data[5] = current; data[6] = current;
    for (int i = 0; i < 6; i++) data[7 + i] = (int)((raw >> (i * 8)) & 255);
    int[]? limits = threshold is null ? null : new int[512];
    if (limits is not null) { limits[0] = 1; limits[2] = id; limits[3] = threshold!.Value; }
    return new DiskSnapshot { Model = "Samsung SSD 860 EVO 500GB", SmartData = data, SmartThresholds = limits };
}
Check(HealthEvaluator.Evaluate(new() { PredictFailure = false }).Level == DecisionLevel.Unknown, "Negative prediction alone is unknown");
Check(HealthEvaluator.Evaluate(new() { PredictFailure = true }).Level == DecisionLevel.ReplaceNow, "Positive prediction is urgent");
var d = Make();
Check(d.Attributes[0].Raw == 417 && d.Attributes[0].Current == 72, "ATA entry offsets and little endian raw");
Check(SmartReport.Raw(d.Attributes[0]) == "00000000-01A1", "HDDScan style raw hex");
Check(HealthEvaluator.Evaluate(d).Level == DecisionLevel.Healthy, "Available thresholds without warning");
Check(HealthEvaluator.Evaluate(Make(threshold: null)).Level == DecisionLevel.Unknown, "Missing thresholds do not show healthy");
Check(SmartReport.State(Make(threshold: null).Attributes[0]) == "UNKNOWN", "Unknown attribute status");
Check(HealthEvaluator.Evaluate(Make(5, 10, 10, 1)).Level == DecisionLevel.ReplaceNow, "Threshold equality fails");
Check(SmartReport.State(Make(199, 100, 0, 5).Attributes[0]) == "REVIEW", "CRC errors get review indicator");
Check(SmartParser.Parse(new int[10], null).Count == 0, "Short SMART buffer rejected");
Check(SmartReport.Name(d, d.Attributes[0]) == "Wear Leveling Count", "Samsung label");
d.Model = "Other <script>alert(1)</script>";
var html = SmartReport.Html(d);
Check(!html.Contains("<script>") && html.Contains("&lt;script&gt;"), "HTML encodes drive strings");
Check(html.Contains("00000000-01A1") && html.Contains("<td>072</td>"), "HTML preserves values");
var n = new DiskSnapshot { Nvme = new NvmeHealth { AvailableSpare = 100, PercentageUsed = 10 } };
Check(HealthEvaluator.Evaluate(n).Level == DecisionLevel.Healthy, "NVMe assessment retained");
using var json = JsonDocument.Parse(ReportWriter.CreateJson(new[] { d }));
Check(json.RootElement.GetProperty("disks")[0].GetProperty("ataSmartAttributes")[0].GetProperty("rawHex").GetString() == "0000000001A1", "JSON raw hex");
var validate = typeof(NativeDiskReader).GetMethod("ValidSmartPage", BindingFlags.NonPublic | BindingFlags.Static)!;
byte[] page = new byte[512]; page[0] = 1; page[2] = 177; page[511] = unchecked((byte)(-178));
Check((bool)validate.Invoke(null, new object[] { page, (byte)0xD0 })!, "Valid SMART page accepted");
page[50] = 1;
Check(!(bool)validate.Invoke(null, new object[] { page, (byte)0xD0 })!, "Corrupt SMART checksum rejected");
Console.WriteLine($"{count} regression checks passed.");
