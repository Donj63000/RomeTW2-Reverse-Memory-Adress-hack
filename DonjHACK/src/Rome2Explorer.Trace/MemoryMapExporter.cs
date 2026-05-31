using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;

namespace Rome2Explorer.Trace;

public sealed class MemoryMapExporter
{
    private readonly Func<DateTimeOffset> _clock;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public MemoryMapExporter()
        : this(() => DateTimeOffset.Now)
    {
    }

    public MemoryMapExporter(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public string Export(MemoryMapSnapshot snapshot, string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var exportDirectory = DonjHackPathResolver.ResolveMemoryMapDirectory(donjHackRoot);
        Directory.CreateDirectory(exportDirectory);

        var payload = new MemoryMapExportEnvelope(
            Snapshot: snapshot,
            Counters: new MemoryMapExportCounters(
                ProcessId: snapshot.Process.ProcessId,
                ModuleCount: snapshot.Modules.Count,
                RegionCount: snapshot.Regions.Count,
                ReadableRegionCount: snapshot.Regions.Count(region => region.IsReadable),
                WritableRegionCount: snapshot.Regions.Count(region => region.IsWritable),
                ExecutableRegionCount: snapshot.Regions.Count(region => region.IsExecutable),
                TotalReadableBytes: snapshot.Summary.TotalReadableBytes,
                TotalWritableBytes: snapshot.Summary.TotalWritableBytes,
                TotalExecutableBytes: snapshot.Summary.TotalExecutableBytes));

        return WriteWithoutOverwrite(exportDirectory, payload);
    }

    private string WriteWithoutOverwrite(string exportDirectory, MemoryMapExportEnvelope payload)
    {
        var timestamp = _clock().ToString("yyyyMMdd-HHmmss");

        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var fileName = suffix == 0
                ? $"memory-map-{timestamp}.json"
                : $"memory-map-{timestamp}-{suffix:000}.json";
            var exportPath = Path.Combine(exportDirectory, fileName);

            try
            {
                using var stream = new FileStream(exportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, payload, JsonOptions);
                return exportPath;
            }
            catch (IOException) when (File.Exists(exportPath))
            {
                continue;
            }
        }

        throw new IOException($"Impossible de creer un export unique dans {exportDirectory} pour le timestamp {timestamp}.");
    }
}
