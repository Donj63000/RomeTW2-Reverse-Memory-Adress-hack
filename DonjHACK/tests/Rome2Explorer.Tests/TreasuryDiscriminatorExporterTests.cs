using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class TreasuryDiscriminatorExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Export_WritesTypedJsonUnderDiscriminatorDirectory()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "discriminator-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var result = CreateResult(session);

        var exportPath = new TreasuryDiscriminatorExporter().Export(process, summary, session, result, outputRoot);

        Assert.True(File.Exists(exportPath));
        Assert.Contains(Path.Combine("evidence", "discriminator"), exportPath);

        var json = File.ReadAllText(exportPath);
        var envelope = JsonSerializer.Deserialize<TreasuryDiscriminatorExportEnvelope>(json, JsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal(KnownValueScanner.TreasuryFeatureId, envelope.Result.FeatureId);
        Assert.Equal("Toujours ambigu - analyse pointeur necessaire", envelope.Result.OverallVerdict);
        Assert.Single(envelope.Result.Candidates);
        Assert.Single(envelope.Result.Log);
    }

    [Fact]
    public void Export_DoesNotOverwriteSameSecondExports()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "discriminator-output", Guid.NewGuid().ToString("N"));
        var process = new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2");
        var summary = new MemoryMapSummary(1, 1, 16, 16, 0);
        var session = CreateSession();
        var result = CreateResult(session);
        var fixedClock = new DateTimeOffset(2026, 5, 31, 4, 10, 0, TimeSpan.Zero);
        var exporter = new TreasuryDiscriminatorExporter(() => fixedClock);

        var firstPath = exporter.Export(process, summary, session, result, outputRoot);
        var secondPath = exporter.Export(process, summary, session, result, outputRoot);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.NotEqual(firstPath, secondPath);
        Assert.EndsWith("treasury-discriminator-20260531-041000.json", firstPath);
        Assert.EndsWith("treasury-discriminator-20260531-041000-001.json", secondPath);
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

    private static TreasuryDiscriminatorResult CreateResult(KnownValueScanSession session)
    {
        var createdAt = DateTimeOffset.Now;
        var candidate = new TreasuryDiscriminatorCandidateResult(
            CandidateId: session.Candidates[0].CandidateId,
            Address: session.Candidates[0].Address,
            AddressHex: "0x1004",
            Type: "Int32",
            RegionBaseHex: "0x1000",
            RegionOffsetHex: "0x4",
            RegionState: "MEM_COMMIT",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            InitialValue: 5000,
            FinalValue: 4500,
            SuccessfulReads: 4,
            FailedReads: 0,
            ChangeCount: 1,
            FirstChangeSampleIndex: 1,
            FirstChangeAt: createdAt,
            StableAfterFirstChange: true,
            ContextStartAddress: 0x1000,
            ContextStartAddressHex: "0x1000",
            ContextByteCount: 16,
            ContextNonZeroByteCount: 12,
            ContextBytesHex: "01020304",
            Score: 80,
            Verdict: "Toujours ambigu",
            IsFavorite: false,
            IsSynchronized: true,
            Reasons: new[] { "test reason" },
            Warnings: Array.Empty<string>());
        var log = new TreasuryDiscriminatorLogEntry(createdAt, "Conclusion", null, null, "Toujours ambigu - analyse pointeur necessaire");

        return new TreasuryDiscriminatorResult(
            CreatedAt: createdAt,
            FeatureId: session.FeatureId,
            ValueHistory: session.ValueHistory,
            ObservationDuration: TimeSpan.FromSeconds(10),
            PollInterval: TimeSpan.FromMilliseconds(250),
            CandidateCount: 1,
            OverallVerdict: "Toujours ambigu - analyse pointeur necessaire",
            FavoriteCandidateId: null,
            FavoriteAddressHex: null,
            IsAmbiguous: true,
            Candidates: new[] { candidate },
            Samples: Array.Empty<TreasuryDiscriminatorSample>(),
            Log: new[] { log });
    }
}
