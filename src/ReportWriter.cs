using System.Text.Json;
using System.Text.Json.Serialization;

namespace SsdHealthInspector;

public static class ReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string CreateJson(IReadOnlyList<DiskSnapshot> disks)
    {
        var report = new
        {
            schemaVersion = "1.1",
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            computer = Environment.MachineName,
            application = "SsdHealthInspector",
            diskCount = disks.Count,
            summary = new
            {
                replaceNow = disks.Count(d => HealthEvaluator.Evaluate(d).Level == DecisionLevel.ReplaceNow),
                replaceSoon = disks.Count(d => HealthEvaluator.Evaluate(d).Level == DecisionLevel.ReplaceSoon),
                monitor = disks.Count(d => HealthEvaluator.Evaluate(d).Level == DecisionLevel.Monitor),
                healthy = disks.Count(d => HealthEvaluator.Evaluate(d).Level == DecisionLevel.Healthy),
                unknown = disks.Count(d => HealthEvaluator.Evaluate(d).Level == DecisionLevel.Unknown)
            },
            disks = disks.Select(d =>
            {
                var decision = HealthEvaluator.Evaluate(d);
                return new
                {
                    physicalDrive = d.Index,
                    devicePath = d.DeviceID,
                    model = d.Model,
                    serialNumber = d.SerialNumber,
                    firmware = d.Firmware,
                    scannedAt = d.ScannedAt,
                    smartSource = d.SmartSource,
                    capacityBytes = d.SizeBytes,
                    busType = d.BusType,
                    mediaType = d.MediaType,
                    trimSupported = d.TrimSupported,
                    incursSeekPenalty = d.IncursSeekPenalty,
                    status = decision.Level.ToString(),
                    recommendation = decision.Headline,
                    explanation = decision.Recommendation,
                    evidence = decision.Evidence,
                    findings = decision.Findings,
                    windowsSmartPredictFailure = d.PredictFailure,
                    nvmeSmart = d.Nvme is null ? null : new
                    {
                        criticalWarningBits = $"0x{d.Nvme.CriticalWarning:X2}",
                        temperatureCelsius = d.Nvme.TemperatureC,
                        availableSparePercent = d.Nvme.AvailableSpare,
                        spareThresholdPercent = d.Nvme.SpareThreshold,
                        lifetimeUsedPercent = d.Nvme.PercentageUsed,
                        mediaErrors = d.Nvme.MediaErrors.ToString(),
                        errorLogEntries = d.Nvme.ErrorLogEntries.ToString(),
                        powerOnHours = d.Nvme.PowerOnHours.ToString(),
                        unsafeShutdowns = d.Nvme.UnsafeShutdowns.ToString()
                    },
                    ataSmartAttributes = d.Attributes.Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        current = a.Current,
                        worst = a.Worst,
                        threshold = a.Threshold,
                        rawValue = a.Raw.ToString(),
                        rawHex = a.Raw.ToString("X12"),
                        thresholdFailed = a.ThresholdFailed
                    }).ToArray(),
                    diagnostics = d.Diagnostics.Concat(
                        string.IsNullOrWhiteSpace(d.NvmeDiagnostic) ? Array.Empty<string>() :
                        new[] { d.NvmeDiagnostic }).ToArray()
                };
            }).ToArray()
        };
        return JsonSerializer.Serialize(report, Options);
    }
}
