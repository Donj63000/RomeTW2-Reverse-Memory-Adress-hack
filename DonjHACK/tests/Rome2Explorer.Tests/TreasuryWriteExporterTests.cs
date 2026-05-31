using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class TreasuryWriteExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Export_WritesTypedJsonUnderWritesDirectory()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "write-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var result = CreateResult(session);

        var exportPath = new TreasuryWriteExporter().Export(process, summary, session, result, outputRoot);

        Assert.True(File.Exists(exportPath));
        Assert.Contains(Path.Combine("evidence", "writes"), exportPath);

        var json = File.ReadAllText(exportPath);
        var envelope = JsonSerializer.Deserialize<TreasuryWriteExportEnvelope>(json, JsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal(KnownValueScanner.TreasuryFeatureId, envelope.Result.FeatureId);
        Assert.Equal(TreasuryWriteStatuses.Success, envelope.Result.Status);
        Assert.Equal(150000, envelope.Result.ValueAfterStabilityDelay);
    }

    [Fact]
    public void Export_DoesNotOverwriteSameSecondExports()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "write-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var result = CreateResult(session);
        var fixedClock = new DateTimeOffset(2026, 5, 31, 19, 0, 0, TimeSpan.Zero);
        var exporter = new TreasuryWriteExporter(() => fixedClock);

        var firstPath = exporter.Export(process, summary, session, result, outputRoot);
        var secondPath = exporter.Export(process, summary, session, result, outputRoot);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.NotEqual(firstPath, secondPath);
        Assert.EndsWith("treasury-write-20260531-190000.json", firstPath);
        Assert.EndsWith("treasury-write-20260531-190000-001.json", secondPath);
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
            Confidence: 0.65);

        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: DateTimeOffset.Now,
            UpdatedAt: DateTimeOffset.Now,
            ValueHistory: new[] { 5000, 4500 },
            Candidates: new[] { candidate },
            Counters: new KnownValueScanCounters(1, 1, 0, 16, 0, 1, 1),
            Warnings: Array.Empty<string>());
    }

    private static TreasuryWriteResult CreateResult(KnownValueScanSession session)
    {
        var candidate = session.Candidates[0];
        return new TreasuryWriteResult(
            CreatedAt: DateTimeOffset.Now,
            FeatureId: session.FeatureId,
            Success: true,
            Status: TreasuryWriteStatuses.Success,
            Message: "Ecriture verifiee.",
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            AddressHex: "0x1004",
            Type: "Int32",
            CandidateCount: 1,
            IsAmbiguousSelection: false,
            ExpectedCurrentValue: 4500,
            DesiredValue: 150000,
            ValueBefore: 4500,
            ValueAfterWrite: 150000,
            ValueAfterStabilityDelay: 150000,
            RegionBaseHex: "0x1000",
            RegionOffsetHex: "0x4",
            RegionState: "MEM_COMMIT",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            CandidateEvidence: candidate.Evidence,
            Evidence: new[] { "WriteProcessMemory OK" },
            Warnings: Array.Empty<string>());
    }
}
