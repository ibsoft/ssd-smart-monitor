namespace SsdHealthInspector;

/// <summary>
/// Conservative risk rules: no data never means healthy, errors are explained,
/// and SSD life-used >=100% is not incorrectly equated with immediate failure.
/// </summary>
public static class HealthEvaluator
{
    public static HealthDecision Evaluate(DiskSnapshot disk)
    {
        DecisionLevel level = DecisionLevel.Unknown;
        bool evidence = false;
        var findings = new List<string>();
        var sources = new List<string>();

        void Raise(DecisionLevel minimum, string message)
        {
            if (minimum > level) level = minimum;
            findings.Add(message);
        }

        if (disk.Nvme is { } nvme)
        {
            evidence = true;
            level = DecisionLevel.Healthy;
            sources.Add("NVMe SMART/Health Information Log 0x02");

            if ((nvme.CriticalWarning & 0x04) != 0)
                Raise(DecisionLevel.ReplaceNow, "NVMe controller reports degraded reliability. Back up and replace the drive.");
            if ((nvme.CriticalWarning & 0x08) != 0)
                Raise(DecisionLevel.ReplaceNow, "NVMe media is in read-only mode. Back up and replace the drive.");
            if ((nvme.CriticalWarning & 0x01) != 0)
                Raise(DecisionLevel.ReplaceSoon, "NVMe available spare has fallen below the manufacturer threshold.");
            if ((nvme.CriticalWarning & 0x10) != 0)
                Raise(DecisionLevel.ReplaceSoon, "NVMe volatile-memory backup has failed (if present).");
            if ((nvme.CriticalWarning & 0x02) != 0)
                Raise(DecisionLevel.Monitor, "NVMe reports a temperature threshold warning; check cooling immediately.");
            if ((nvme.CriticalWarning & 0xE0) != 0)
                Raise(DecisionLevel.Monitor, "NVMe SMART has manufacturer/reserved warning bits set; investigate.");

            if (nvme.SpareThreshold > 0 && nvme.AvailableSpare < nvme.SpareThreshold)
                Raise(DecisionLevel.ReplaceSoon, $"NVMe available spare {nvme.AvailableSpare}% is below its {nvme.SpareThreshold}% threshold.");
            if (nvme.PercentageUsed >= 100)
                Raise(DecisionLevel.ReplaceSoon, $"Estimated SSD endurance is exhausted ({nvme.PercentageUsed}% used). This does not prove immediate failure.");
            else if (nvme.PercentageUsed >= 90)
                Raise(DecisionLevel.ReplaceSoon, $"SSD lifetime used {nvme.PercentageUsed}%: plan replacement soon.");
            else if (nvme.PercentageUsed >= 80)
                Raise(DecisionLevel.Monitor, $"SSD lifetime used {nvme.PercentageUsed}%: track wear and plan replacement.");

            if (nvme.MediaErrors > 0)
                Raise(DecisionLevel.Monitor, $"NVMe reports {nvme.MediaErrors:N0} lifetime uncorrectable media/data errors. Check whether the count is increasing.");
            if (nvme.TemperatureC is >= 70)
                Raise(DecisionLevel.Monitor, $"SSD temperature is {nvme.TemperatureC}°C: improve cooling and verify manufacturer limits.");
        }

        if (disk.PredictFailure is bool failing)
        {
            sources.Add("Native Windows storage failure prediction IOCTL");
            if (failing)
            {
                evidence = true;
                Raise(DecisionLevel.ReplaceNow, "Drive firmware reports SMART predicted failure.");
            }
            else findings.Add("Windows reports no predicted failure. This flag alone does not establish drive health.");
        }

        var attributes = disk.Attributes;
        if (attributes.Count != 0)
        {
            evidence = true;
            sources.Add($"ATA SMART attributes ({attributes.Count})");
            if (level == DecisionLevel.Unknown) level = DecisionLevel.Healthy;
            foreach (var attribute in attributes.Where(a => a.ThresholdFailed))
            {
                // Vendor-defined ATA attributes are not uniformly critical.
                // Treat unrecognized threshold crossings as warnings, not proof of failure.
                var severity = attribute.Id is 5 or 10 or 171 or 172 or 179 or 181 or 182 or 187 or 197 or 198
                    ? DecisionLevel.ReplaceNow : DecisionLevel.Monitor;
                Raise(severity, $"SMART attribute {attribute.Id} ({attribute.Name}) crossed its manufacturer threshold (value {attribute.Current}, threshold {attribute.Threshold}).");
            }

            // Check only standard ATA/SATA attributes with established semantics.
            // Vendor-specific flash wear attributes are displayed but NOT guessed.
            foreach (var a in attributes.Where(a => a.Raw > 0 && (a.Id is 197 or 198)))
                Raise(DecisionLevel.ReplaceSoon, $"SMART attribute {a.Id} ({a.Name}) is non-zero ({a.Raw}). Confirm vendor interpretation; back up.");
            foreach (var a in attributes.Where(a => a.Raw > 0 && (a.Id is 5 or 187)))
                Raise(DecisionLevel.Monitor, $"SMART attribute {a.Id} ({a.Name}) is non-zero ({a.Raw}). Review vendor meaning and whether this value is increasing.");
        }

        if (!string.IsNullOrWhiteSpace(disk.WindowsHealthStatus))
        {
            sources.Add("Windows Storage HealthStatus");
            string status = disk.WindowsHealthStatus.Trim();
            if (status.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
            {
                evidence = true;
                Raise(DecisionLevel.ReplaceNow, "Windows Storage reports this physical disk as Unhealthy.");
            }
            else if (status.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                evidence = true;
                Raise(DecisionLevel.ReplaceSoon, "Windows Storage reports a warning for this physical disk.");
            }
            else if (status.Equals("Healthy", StringComparison.OrdinalIgnoreCase) && !evidence)
            {
                // Windows 'Healthy' is not a substitute for direct SMART telemetry.
                findings.Add("Windows reports Healthy, but direct SMART evidence is unavailable.");
            }
        }

        if (disk.Wear is >= 0 and <= 255)
        {
            evidence = true;
            sources.Add("Windows Storage reliability wear counter");
            if (level == DecisionLevel.Unknown) level = DecisionLevel.Healthy;
            if (disk.Wear >= 90)
                Raise(DecisionLevel.ReplaceSoon, $"Windows reports {disk.Wear}% SSD wear used: plan replacement.");
            else if (disk.Wear >= 80)
                Raise(DecisionLevel.Monitor, $"Windows reports {disk.Wear}% SSD wear used: monitor closely.");
        }

        if (disk.ReadErrorsUncorrected is > 0)
        {
            evidence = true;
            sources.Add("Windows Storage reliability counters");
            Raise(DecisionLevel.ReplaceSoon, $"Windows reports {disk.ReadErrorsUncorrected:N0} uncorrected read errors. Protect data and investigate.");
        }
        if (disk.WriteErrorsUncorrected is > 0)
        {
            evidence = true;
            sources.Add("Windows Storage reliability counters");
            Raise(DecisionLevel.ReplaceSoon, $"Windows reports {disk.WriteErrorsUncorrected:N0} uncorrected write errors. Protect data and investigate.");
        }
        if (disk.Nvme is null && disk.Temperature is >= 70)
        {
            evidence = true;
            Raise(DecisionLevel.Monitor, $"Windows reports {disk.Temperature}°C: investigate cooling and device limits.");
        }

        if (disk.Nvme is null && attributes.Count > 0 && attributes.Any(a => a.Threshold is null))
        {
            findings.Add("Some SMART thresholds are missing; threshold assessment is incomplete.");
            if (level == DecisionLevel.Healthy) level = DecisionLevel.Unknown;
        }

        if (!evidence)
        {
            level = DecisionLevel.Unknown;
            findings.Add("No reliable SMART result was obtained. A missing reading is not proof that the SSD is healthy.");
            if (disk.IsNvme && !string.IsNullOrWhiteSpace(disk.NvmeDiagnostic))
                findings.Add(disk.NvmeDiagnostic);
        }
        else if (findings.Count == 0)
        {
            findings.Add("No failure or replacement indicators detected in the data available at scan time.");
        }

        var (headline, recommendation) = level switch
        {
            DecisionLevel.ReplaceNow => ("REPLACE NOW", "Back up important data immediately and replace this drive. Do not rely on continued operation."),
            DecisionLevel.ReplaceSoon => ("REPLACE SOON", "Back up data now, verify errors and schedule replacement. Avoid storing the only copy on this drive."),
            DecisionLevel.Monitor => ("MONITOR / INVESTIGATE", "No definitive immediate replacement signal, but warnings require follow-up and reliable backups."),
            DecisionLevel.Healthy => ("NO REPLACEMENT INDICATED", "Available health data shows no replacement trigger. Keep backups: SMART cannot predict every failure."),
            _ => ("UNABLE TO DETERMINE", "SMART information is unavailable or incomplete. Do not assume the SSD is healthy; test with a vendor tool or direct connection.")
        };

        return new HealthDecision
        {
            Level = level,
            Headline = headline,
            Recommendation = recommendation,
            Findings = findings,
            Evidence = sources.Count > 0 ? string.Join("; ", sources.Distinct()) : "No direct SMART/Storage counters"
        };
    }
}
