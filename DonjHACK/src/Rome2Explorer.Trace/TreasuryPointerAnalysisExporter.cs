using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Trace;

public sealed class TreasuryPointerAnalysisExporter
{
    private readonly Func<DateTimeOffset> _clock;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TreasuryPointerAnalysisExporter()
        : this(() => DateTimeOffset.Now)
    {
    }

    public TreasuryPointerAnalysisExporter(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public string Export(
        Rome2ProcessInfo process,
        MemoryMapSummary memorySummary,
        KnownValueScanSession session,
        TreasuryPointerAnalysisResult result,
        string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(memorySummary);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        var exportDirectory = DonjHackPathResolver.ResolveStructuresDirectory(donjHackRoot);
        Directory.CreateDirectory(exportDirectory);

        var payload = new TreasuryPointerAnalysisExportEnvelope(
            CreatedAt: _clock(),
            Process: process,
            MemorySummary: memorySummary,
            Session: session,
            Result: result);

        return WriteWithoutOverwrite(exportDirectory, payload);
    }

    private string WriteWithoutOverwrite(string exportDirectory, TreasuryPointerAnalysisExportEnvelope payload)
    {
        var timestamp = _clock().ToString("yyyyMMdd-HHmmss");

        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var fileName = suffix == 0
                ? $"treasury-structure-{timestamp}.json"
                : $"treasury-structure-{timestamp}-{suffix:000}.json";
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

        throw new IOException($"Impossible de creer un export structure unique dans {exportDirectory}.");
    }
}
