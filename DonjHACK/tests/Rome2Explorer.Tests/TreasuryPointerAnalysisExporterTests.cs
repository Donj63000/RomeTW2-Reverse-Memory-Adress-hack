using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class TreasuryPointerAnalysisExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Export_WritesTypedJsonUnderStructuresDirectory()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "structure-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var result = CreateResult(session);

        var exportPath = new TreasuryPointerAnalysisExporter().Export(process, summary, session, result, outputRoot);

        Assert.True(File.Exists(exportPath));
        Assert.Contains(Path.Combine("evidence", "structures"), exportPath);

        var json = File.ReadAllText(exportPath);
        var envelope = JsonSerializer.Deserialize<TreasuryPointerAnalysisExportEnvelope>(json, JsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal(KnownValueScanner.TreasuryFeatureId, envelope.Result.FeatureId);
        Assert.Equal(TreasuryPointerAnalysisVerdicts.ProbableStructure, envelope.Result.OverallVerdict);
        Assert.Equal("0x1000", envelope.Result.BestStructureBase!.BaseAddressHex);
    }

    [Fact]
    public void Export_DoesNotOverwriteSameSecondExports()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "structure-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var result = CreateResult(session);
        var fixedClock = new DateTimeOffset(2026, 5, 31, 21, 0, 0, TimeSpan.Zero);
        var exporter = new TreasuryPointerAnalysisExporter(() => fixedClock);

        var firstPath = exporter.Export(process, summary, session, result, outputRoot);
        var secondPath = exporter.Export(process, summary, session, result, outputRoot);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.NotEqual(firstPath, secondPath);
        Assert.EndsWith("treasury-structure-20260531-210000.json", firstPath);
        Assert.EndsWith("treasury-structure-20260531-210000-001.json", secondPath);
    }

    private static KnownValueScanSession CreateSession()
    {
        var region = new MemoryRegionInfo(0x1000, 0x1000, "MEM_COMMIT", "PAGE_READWRITE", "MEM_PRIVATE", true, true, false);
        var candidate = new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: "treasury-int32-0x00001120",
            Address: 0x1120,
            Type: "Int32",
            ObservedValue: "4200",
            ExpectedValue: "4200",
            Region: region,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: new[] { "test evidence" },
            Warnings: Array.Empty<string>(),
            Confidence: 0.80);

        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: DateTimeOffset.Now,
            UpdatedAt: DateTimeOffset.Now,
            ValueHistory: new[] { 4500, 4200 },
            Candidates: new[] { candidate },
            Counters: new KnownValueScanCounters(1, 1, 0, 16, 0, 1, 1),
            Warnings: Array.Empty<string>());
    }

    private static TreasuryPointerAnalysisResult CreateResult(KnownValueScanSession session)
    {
        var best = new StructureBaseCandidate(
            BaseAddress: 0x1000,
            BaseAddressHex: "0x1000",
            TreasuryOffset: 0x120,
            TreasuryOffsetHex: "0x120",
            RegionBaseHex: "0x1000",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            DirectPointerHitCount: 1,
            CandidatePointerHitCount: 0,
            PointerChainCount: 0,
            Score: 80,
            Status: "Probable",
            Evidence: new[] { "test base evidence" },
            Warnings: Array.Empty<string>());

        return new TreasuryPointerAnalysisResult(
            CreatedAt: DateTimeOffset.Now,
            FeatureId: session.FeatureId,
            CandidateId: session.Candidates[0].CandidateId,
            CandidateAddress: session.Candidates[0].Address,
            CandidateAddressHex: "0x1120",
            ValueType: "Int32",
            CandidateCount: 1,
            ValueHistory: session.ValueHistory,
            ContextByteCount: 16,
            ContextNonZeroByteCount: 4,
            ContextStartAddressHex: "0x1000",
            ContextBytesHexPreview: "01020304",
            TestedBaseCount: 257,
            ScannedRegionCount: 1,
            ScannedBytes: 256,
            HitLimitReached: false,
            OverallVerdict: TreasuryPointerAnalysisVerdicts.ProbableStructure,
            BestStructureBase: best,
            StructureBases: new[] { best },
            PointerHits: Array.Empty<PointerHit>(),
            PointerChains: Array.Empty<PointerChainCandidate>(),
            Evidence: new[] { "test result evidence" },
            Warnings: Array.Empty<string>());
    }
}
