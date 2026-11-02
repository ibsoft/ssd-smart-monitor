namespace SsdHealthInspector;

public static class DiskDiscovery
{
    public static Task<List<DiskSnapshot>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = NativeDiskReader.Discover();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);
}
