using System.Numerics;

namespace SsdHealthInspector;

public sealed class DiskSnapshot
{
    public int Index { get; set; }
    public string Model { get; set; } = "";
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.Now;
    public string Firmware { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string SmartSource { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string DeviceID { get; set; } = "";
    public string BusType { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string WindowsHealthStatus { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool? PredictFailure { get; set; }
    public int[]? SmartData { get; set; }
    public int[]? SmartThresholds { get; set; }
    public bool? TrimSupported { get; set; }
    public bool? IncursSeekPenalty { get; set; }
    public List<string> Diagnostics { get; set; } = new();
    public int? Wear { get; set; }
    public int? Temperature { get; set; }
    public long? PowerOnHours { get; set; }
    public ulong? ReadErrorsUncorrected { get; set; }
    public ulong? WriteErrorsUncorrected { get; set; }
    public NvmeHealth? Nvme { get; set; }
    public string NvmeDiagnostic { get; set; } = "";
    public List<SmartAttribute> Attributes => SmartParser.Parse(SmartData, SmartThresholds);
    public bool IsNvme => BusType.Equals("NVMe", StringComparison.OrdinalIgnoreCase) ||
                          Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
    {
        var sizeGb = SizeBytes > 0 ? $" ({SizeBytes / 1_000_000_000d:0.#} GB)" : "";
        return $"Disk {Index}  |  {Model.Trim()}{sizeGb}";
    }
}

public sealed class SmartAttribute
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int Current { get; init; }
    public int Worst { get; init; }
    public int? Threshold { get; init; }
    public ulong Raw { get; init; }
    public bool ThresholdFailed => Threshold is > 0 && Current <= Threshold.Value;
}

public sealed class NvmeHealth
{
    public byte CriticalWarning { get; init; }
    public int? TemperatureC { get; init; }
    public int AvailableSpare { get; init; }
    public int SpareThreshold { get; init; }
    public int PercentageUsed { get; init; }
    public BigInteger MediaErrors { get; init; }
    public BigInteger ErrorLogEntries { get; init; }
    public BigInteger PowerOnHours { get; init; }
    public BigInteger UnsafeShutdowns { get; init; }
}

public enum DecisionLevel
{
    Unknown = 0,
    Healthy = 1,
    Monitor = 2,
    ReplaceSoon = 3,
    ReplaceNow = 4
}

public sealed class HealthDecision
{
    public DecisionLevel Level { get; init; }
    public string Headline { get; init; } = "";
    public string Recommendation { get; init; } = "";
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public string Evidence { get; init; } = "";
}
