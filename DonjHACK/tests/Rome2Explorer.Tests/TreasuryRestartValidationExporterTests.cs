using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class TreasuryRestartValidationExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Export_WritesNormalizedValidationBundle()
    {
        var outputRoot = CreateOutputRoot();
        var result = CreateValidationResult();

        var bundle = new TreasuryRestartValidationExporter(() => new DateTimeOffset(2026, 5, 31, 22, 30, 0, TimeSpan.Zero))
            .Export(result, outputRoot);

        Assert.True(File.Exists(bundle.ManifestPath));
        Assert.True(File.Exists(bundle.RankedCandidatesPath));
        Assert.True(File.Exists(bundle.ValidationReportPath));
        Assert.True(File.Exists(bundle.FullExportPath));
        Assert.EndsWith("manifest.json", bundle.ManifestPath);
        Assert.EndsWith("ranked-candidates.json", bundle.RankedCandidatesPath);
        Assert.EndsWith("validation-report.json", bundle.ValidationReportPath);

        var envelope = JsonSerializer.Deserialize<TreasuryRestartValidationExportEnvelope>(File.ReadAllText(bundle.FullExportPath), JsonOptions);
        Assert.NotNull(envelope);
        Assert.Equal(TreasuryRestartValidationStatuses.ProbableAfterReload, envelope!.Result.OverallStatus);
    }

    [Fact]
    public void Export_DoesNotOverwriteSameSecondBundles()
    {
        var outputRoot = CreateOutputRoot();
        var fixedClock = new DateTimeOffset(2026, 5, 31, 22, 30, 0, TimeSpan.Zero);
        var exporter = new TreasuryRestartValidationExporter(() => fixedClock);
        var result = CreateValidationResult();

        var first = exporter.Export(result, outputRoot);
        var second = exporter.Export(result, outputRoot);

        Assert.NotEqual(first.BundleDirectory, second.BundleDirectory);
        Assert.True(Directory.Exists(first.BundleDirectory));
        Assert.True(Directory.Exists(second.BundleDirectory));
    }

    [Fact]
    public void LoadReferences_ReadsWriteAndStructureExports()
    {
        var outputRoot = CreateOutputRoot();
        var process = CreateProcess();
        var summary = new MemoryMapSummary(1, 1, 0x1000, 0x1000, 0);
        var candidate = CreateCandidate(0x50120, 0x50000);
        var session = CreateSession(candidate);
        var write = CreateWriteResult(candidate);
        var pointer = CreatePointerResult(candidate, 0x50000, 0x120);

        new TreasuryWriteExporter(() => new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero))
            .Export(process, summary, session, write, outputRoot);
        new TreasuryPointerAnalysisExporter(() => new DateTimeOffset(2026, 5, 31, 22, 1, 0, TimeSpan.Zero))
            .Export(process, summary, session, pointer, outputRoot);

        var load = new TreasuryRestartValidationExporter().LoadReferences(outputRoot);

        Assert.Contains(load.References, reference => reference.SourceKind == "WriteExport" && reference.WriteSucceeded == true);
        Assert.Contains(load.References, reference => reference.SourceKind == "StructureExport" && reference.TreasuryOffset == 0x120);
        Assert.Empty(load.Warnings);
    }

    private static string CreateOutputRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "restart-validation-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static TreasuryRestartValidationResult CreateValidationResult()
    {
        var process = CreateProcess();
        var summary = new MemoryMapSummary(1, 1, 0x1000, 0x1000, 0);
        var current = new TreasuryValidationReference(
            CreatedAt: new DateTimeOffset(2026, 5, 31, 22, 30, 0, TimeSpan.Zero),
            SourceKind: "CurrentSession",
            SourcePath: "runtime",
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: "cand",
            Address: 0x50120,
            AddressHex: "0x50120",
            Architecture: "X86",
            ProcessId: 2,
            ProcessStartTime: process.StartTime,
            ProcessPath: process.Path,
            ValueHistory: new[] { 150000 },
            RegionBaseHex: "0x50000",
            RegionOffsetHex: "0x120",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            PointerVerdict: TreasuryPointerAnalysisVerdicts.ProbableStructure,
            StructureBaseHex: "0x50000",
            TreasuryOffset: 0x120,
            TreasuryOffsetHex: "0x120",
            PointerScore: 82,
            PointerHitCount: 2,
            PointerChainCount: 1,
            WriteSucceeded: null,
            WrittenValue: null,
            Evidence: new[] { "current" },
            Warnings: Array.Empty<string>());
        var ranked = new TreasuryValidationCandidateResult(
            CandidateId: "cand",
            Address: 0x50120,
            AddressHex: "0x50120",
            IsSelected: true,
            Status: TreasuryRestartValidationStatuses.ProbableAfterReload,
            Score: 75,
            RegionBaseHex: "0x50000",
            RegionOffsetHex: "0x120",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            PointerStatus: TreasuryPointerAnalysisVerdicts.ProbableStructure,
            StructureBaseHex: "0x50000",
            TreasuryOffsetHex: "0x120",
            MatchedReferenceCount: 1,
            StrongestReferenceKind: "StructureExport",
            StrongestReferencePath: "old.json",
            Evidence: new[] { "offset treasury conserve" },
            Warnings: Array.Empty<string>());

        return new TreasuryRestartValidationResult(
            CreatedAt: current.CreatedAt,
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            Scenario: "test",
            OverallStatus: TreasuryRestartValidationStatuses.ProbableAfterReload,
            OverallVerdict: "test verdict",
            Process: process,
            MemorySummary: summary,
            CurrentReference: current,
            ReferenceCount: 1,
            ReferencesUsed: Array.Empty<TreasuryValidationReference>(),
            RankedCandidates: new[] { ranked },
            Evidence: new[] { "test evidence" },
            Warnings: Array.Empty<string>());
    }

    private static Rome2ProcessInfo CreateProcess()
    {
        return new Rome2ProcessInfo(
            ProcessId: 2,
            Name: "Rome2",
            Path: @"C:\Games\Total War - Rome 2\Rome2.exe",
            Architecture: ProcessArchitecture.X86,
            StartTime: new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero),
            MainWindowTitle: "Total War: ROME 2");
    }

    private static KnownValueScanSession CreateSession(params Candidate[] candidates)
    {
        var now = DateTimeOffset.Now;
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: now,
            UpdatedAt: now,
            ValueHistory: new[] { 50000, 150000 },
            Candidates: candidates,
            Counters: new KnownValueScanCounters(1, 1, 0, 4, 0, candidates.Length, candidates.Length),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(ulong address, ulong regionBase)
    {
        var region = new MemoryRegionInfo(regionBase, 0x1000, "MEM_COMMIT", "PAGE_READWRITE", "MEM_PRIVATE", true, true, false);
        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: $"treasury-int32-0x{address:X8}",
            Address: address,
            Type: "Int32",
            ObservedValue: "150000",
            ExpectedValue: "150000",
            Region: region,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: new[] { "test evidence" },
            Warnings: Array.Empty<string>(),
            Confidence: 0.80);
    }

    private static TreasuryWriteResult CreateWriteResult(Candidate candidate)
    {
        return new TreasuryWriteResult(
            CreatedAt: DateTimeOffset.Now,
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            Success: true,
            Status: TreasuryWriteStatuses.Success,
            Message: "test write",
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            AddressHex: $"0x{candidate.Address:X}",
            Type: "Int32",
            CandidateCount: 1,
            IsAmbiguousSelection: false,
            ExpectedCurrentValue: 50000,
            DesiredValue: 150000,
            ValueBefore: 50000,
            ValueAfterWrite: 150000,
            ValueAfterStabilityDelay: 150000,
            RegionBaseHex: "0x50000",
            RegionOffsetHex: "0x120",
            RegionState: "MEM_COMMIT",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            CandidateEvidence: candidate.Evidence,
            Evidence: new[] { "WriteProcessMemory OK" },
            Warnings: Array.Empty<string>());
    }

    private static TreasuryPointerAnalysisResult CreatePointerResult(Candidate candidate, ulong baseAddress, int offset)
    {
        var best = new StructureBaseCandidate(
            BaseAddress: baseAddress,
            BaseAddressHex: $"0x{baseAddress:X}",
            TreasuryOffset: offset,
            TreasuryOffsetHex: $"0x{offset:X}",
            RegionBaseHex: $"0x{baseAddress:X}",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            DirectPointerHitCount: 2,
            CandidatePointerHitCount: 1,
            PointerChainCount: 1,
            Score: 82,
            Status: "Probable",
            Evidence: new[] { "test pointer" },
            Warnings: Array.Empty<string>());

        return new TreasuryPointerAnalysisResult(
            CreatedAt: DateTimeOffset.Now,
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: candidate.CandidateId,
            CandidateAddress: candidate.Address,
            CandidateAddressHex: $"0x{candidate.Address:X}",
            ValueType: "Int32",
            CandidateCount: 1,
            ValueHistory: new[] { 50000, 150000 },
            ContextByteCount: 256,
            ContextNonZeroByteCount: 12,
            ContextStartAddressHex: $"0x{candidate.Address - 0x80:X}",
            ContextBytesHexPreview: "00",
            TestedBaseCount: 128,
            ScannedRegionCount: 1,
            ScannedBytes: 0x1000,
            HitLimitReached: false,
            OverallVerdict: TreasuryPointerAnalysisVerdicts.ProbableStructure,
            BestStructureBase: best,
            StructureBases: new[] { best },
            PointerHits: Array.Empty<PointerHit>(),
            PointerChains: Array.Empty<PointerChainCandidate>(),
            Evidence: new[] { "pointer evidence" },
            Warnings: Array.Empty<string>());
    }
}
