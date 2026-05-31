using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class CandidateScanExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void ExportTreasuryCandidates_WritesTypedJsonUnderCandidatesDirectory()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "candidate-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();

        var exportPath = new CandidateScanExporter().ExportTreasuryCandidates(process, summary, session, outputRoot);

        Assert.True(File.Exists(exportPath));
        Assert.Contains(Path.Combine("evidence", "candidates"), exportPath);

        var json = File.ReadAllText(exportPath);
        var envelope = JsonSerializer.Deserialize<CandidateScanExportEnvelope>(json, JsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal(KnownValueScanner.TreasuryFeatureId, envelope.Session.FeatureId);
        Assert.Equal(4500, envelope.Session.ValueHistory[0]);
        Assert.Single(envelope.Session.Candidates);
        Assert.Equal(1, envelope.Session.Counters.CandidatesAfter);
    }

    [Fact]
    public void ExportTreasuryCandidates_DoesNotOverwriteSameSecondExports()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "candidate-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var fixedClock = new DateTimeOffset(2026, 5, 31, 2, 40, 0, TimeSpan.Zero);
        var exporter = new CandidateScanExporter(() => fixedClock);

        var firstPath = exporter.ExportTreasuryCandidates(process, summary, session, outputRoot);
        var secondPath = exporter.ExportTreasuryCandidates(process, summary, session, outputRoot);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.NotEqual(firstPath, secondPath);
        Assert.EndsWith("treasury-candidates-20260531-024000.json", firstPath);
        Assert.EndsWith("treasury-candidates-20260531-024000-001.json", secondPath);
    }

    private static KnownValueScanSession CreateSession()
    {
        var region = new MemoryRegionInfo(0x1000, 0x100, "MEM_COMMIT", "PAGE_READWRITE", "MEM_PRIVATE", true, true, false);
        var candidate = new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: "treasury-int32-0x00001004",
            Address: 0x1004,
            Type: "Int32",
            ObservedValue: "4500",
            ExpectedValue: "4500",
            Region: region,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: new[] { "test evidence" },
            Warnings: Array.Empty<string>(),
            Confidence: 0.15);

        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: DateTimeOffset.Now,
            UpdatedAt: DateTimeOffset.Now,
            ValueHistory: new[] { 4500 },
            Candidates: new[] { candidate },
            Counters: new KnownValueScanCounters(1, 1, 0, 16, 0, 0, 1),
            Warnings: Array.Empty<string>());
    }
}
