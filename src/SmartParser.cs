namespace SsdHealthInspector;

public static class SmartParser
{
    // ATA SMART READ DATA: 2-byte header followed by up to 30 entries of 12 bytes.
    // Attribute raw values and even their meaning may vary between manufacturers.
    private static readonly Dictionary<int, string> Names = new()
    {
        [5] = "Reallocated / retired sectors",
        [9] = "Power-on hours (vendor-specific)",
        [12] = "Power-cycle count",
        [170] = "Available reserved space (vendor-specific)",
        [171] = "Program failures (vendor-specific)",
        [172] = "Erase failures (vendor-specific)",
        [173] = "Wear leveling (vendor-specific)",
        [174] = "Unexpected power losses (vendor-specific)",
        [177] = "Wear-leveling count (vendor-specific)",
        [179] = "Used reserved block count (vendor-specific)",
        [181] = "Program fail count (vendor-specific)",
        [182] = "Erase fail count (vendor-specific)",
        [187] = "Reported uncorrectable errors",
        [188] = "Command timeouts",
        [190] = "Temperature / airflow (vendor-specific)",
        [194] = "Temperature (vendor-specific)",
        [196] = "Reallocation events",
        [197] = "Current pending sectors",
        [198] = "Offline uncorrectable sectors",
        [199] = "Interface CRC errors",
        [202] = "Lifetime / wear indicator (vendor-specific)",
        [231] = "SSD life / temperature (vendor-specific)",
        [232] = "Available reserved space (vendor-specific)",
        [233] = "Media wearout indicator (vendor-specific)",
        [241] = "Lifetime host writes (vendor-specific)",
        [242] = "Lifetime host reads (vendor-specific)"
    };

    public static List<SmartAttribute> Parse(int[]? data, int[]? thresholdData)
    {
        var result = new List<SmartAttribute>();
        if (data is null || data.Length < 362) return result;

        var thresholds = new Dictionary<int, int>();
        if (thresholdData is { Length: >= 362 })
        {
            for (int i = 0; i < 30; i++)
            {
                int offset = 2 + i * 12;
                int id = thresholdData[offset];
                if (id is > 0 and < 255) thresholds[id] = thresholdData[offset + 1];
            }
        }

        for (int i = 0; i < 30; i++)
        {
            int offset = 2 + i * 12;
            int id = data[offset];
            if (id is 0 or 255) continue;
            ulong raw = 0;
            for (int b = 0; b < 6; b++) raw |= (ulong)(data[offset + 5 + b] & 0xff) << (8 * b);
            result.Add(new SmartAttribute
            {
                Id = id,
                Name = Names.GetValueOrDefault(id, "Vendor-specific attribute"),
                Current = data[offset + 3] & 0xff,
                Worst = data[offset + 4] & 0xff,
                Raw = raw,
                Threshold = thresholds.TryGetValue(id, out int value) ? value : null
            });
        }
        return result;
    }
}
