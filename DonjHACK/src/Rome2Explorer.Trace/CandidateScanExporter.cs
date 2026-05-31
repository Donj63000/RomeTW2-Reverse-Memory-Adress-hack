using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Trace;

public sealed class CandidateScanExporter
{
    private readonly Func<DateTimeOffset> _clock;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CandidateScanExporter()
        : this(() => DateTimeOffset.Now)
    {
    }

    public CandidateScanExporter(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public string ExportTreasuryCandidates(
        Rome2ProcessInfo process,
        MemoryMapSummary memorySummary,
        KnownValueScanSession session,
        string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(memorySummary);
        ArgumentNullException.ThrowIfNull(session);

        var exportDirectory = DonjHackPathResolver.ResolveCandidatesDirectory(donjHackRoot);
        Directory.CreateDirectory(exportDirectory);

        var payload = new CandidateScanExportEnvelope(
            CreatedAt: _clock(),
            Process: process,
            MemorySummary: memorySummary,
            Session: session);

        return WriteWithoutOverwrite(exportDirectory, payload);
    }

    private string WriteWithoutOverwrite(string exportDirectory, CandidateScanExportEnvelope payload)
    {
        var timestamp = _clock().ToString("yyyyMMdd-HHmmss");

        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var fileName = suffix == 0
                ? $"treasury-candidates-{timestamp}.json"
                : $"treasury-candidates-{timestamp}-{suffix:000}.json";
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

        throw new IOException($"Impossible de creer un export candidates unique dans {exportDirectory}.");
    }
}
