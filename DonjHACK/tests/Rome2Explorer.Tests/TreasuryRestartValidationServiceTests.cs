using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Tests;

public sealed class TreasuryRestartValidationServiceTests
{
    [Fact]
    public void Validate_ClassifiesSameOffsetDifferentAddressAsValidatedAfterRestart()
    {
        var candidate = CreateCandidate(0x50120, 0x50000);
        var session = CreateSession(candidate);
        var snapshot = CreateSnapshot(processId: 2, startTime: new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero));
        var reference = CreateReference(
            address: 0x40120,
            regionOffsetHex: "0x120",
            treasuryOffset: 0x120,
            processId: 1,
            startTime: new DateTimeOffset(2026, 5, 31, 21, 0, 0, TimeSpan.Zero));
        var pointer = CreatePointerResult(candidate, 0x50000, 0x120);

        var result = new TreasuryRestartValidationService(() => new DateTimeOffset(2026, 5, 31, 22, 5, 0, TimeSpan.Zero))
            .Validate(session, candidate, snapshot, new[] { reference }, pointer);

        Assert.Equal(TreasuryRestartValidationStatuses.ValidatedAfterRestart, result.OverallStatus);
        var ranked = Assert.Single(result.RankedCandidates);
        Assert.Equal(TreasuryRestartValidationStatuses.ValidatedAfterRestart, ranked.Status);
        Assert.Contains(ranked.Evidence, item => item.Contains("offset treasury conserve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DetectsUnstableRawAddressAsBroken()
    {
        var candidate = CreateCandidate(0x50220, 0x50000);
        var session = CreateSession(candidate);
        var snapshot = CreateSnapshot(processId: 2, startTime: new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero));
        var reference = CreateReference(
            address: 0x40120,
            regionOffsetHex: "0x120",
            treasuryOffset: null,
            processId: 1,
            startTime: new DateTimeOffset(2026, 5, 31, 21, 0, 0, TimeSpan.Zero));

        var result = new TreasuryRestartValidationService()
            .Validate(session, candidate, snapshot, new[] { reference }, currentPointerAnalysis: null);

        Assert.Equal(TreasuryRestartValidationStatuses.Broken, result.OverallStatus);
        Assert.Equal(TreasuryRestartValidationStatuses.Broken, result.RankedCandidates[0].Status);
    }

    [Fact]
    public void Validate_ClassifiesEquivalentCandidatesAsAmbiguous()
    {
        var first = CreateCandidate(0x50120, 0x50000);
        var second = CreateCandidate(0x60120, 0x60000);
        var session = CreateSession(first, second);
        var snapshot = CreateSnapshot(processId: 2, startTime: new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero));
        var reference = CreateReference(
            address: 0x40120,
            regionOffsetHex: "0x120",
            treasuryOffset: null,
            processId: 1,
            startTime: new DateTimeOffset(2026, 5, 31, 21, 0, 0, TimeSpan.Zero));

        var result = new TreasuryRestartValidationService()
            .Validate(session, first, snapshot, new[] { reference }, currentPointerAnalysis: null);

        Assert.Equal(TreasuryRestartValidationStatuses.Ambiguous, result.OverallStatus);
        Assert.Equal(result.RankedCandidates[0].Score, result.RankedCandidates[1].Score);
    }

    [Fact]
    public void Validate_WithoutPreviousReferenceExportsRawBaseline()
    {
        var candidate = CreateCandidate(0x50120, 0x50000);
        var session = CreateSession(candidate);
        var snapshot = CreateSnapshot(processId: 2, startTime: new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero));

        var result = new TreasuryRestartValidationService()
            .Validate(session, candidate, snapshot, Array.Empty<TreasuryValidationReference>(), currentPointerAnalysis: null);

        Assert.Equal(TreasuryRestartValidationStatuses.RawOnly, result.OverallStatus);
        Assert.Equal("CurrentSession", result.CurrentReference.SourceKind);
        Assert.Contains(result.Warnings, item => item.Contains("point de depart", StringComparison.OrdinalIgnoreCase));
    }

    private static TreasuryValidationReference CreateReference(
        ulong address,
        string regionOffsetHex,
        int? treasuryOffset,
        int processId,
        DateTimeOffset startTime)
    {
        return new TreasuryValidationReference(
            CreatedAt: startTime,
            SourceKind: "StructureExport",
            SourcePath: "old.json",
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: "old-candidate",
            Address: address,
            AddressHex: $"0x{address:X}",
            Architecture: "X86",
            ProcessId: processId,
            ProcessStartTime: startTime,
            ProcessPath: @"C:\Games\Total War - Rome 2\Rome2.exe",
            ValueHistory: new[] { 50000, 150000 },
            RegionBaseHex: "0x40000",
            RegionOffsetHex: regionOffsetHex,
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            PointerVerdict: treasuryOffset.HasValue ? TreasuryPointerAnalysisVerdicts.ProbableStructure : null,
            StructureBaseHex: treasuryOffset.HasValue ? $"0x{address - (ulong)treasuryOffset.Value:X}" : null,
            TreasuryOffset: treasuryOffset,
            TreasuryOffsetHex: treasuryOffset.HasValue ? $"0x{treasuryOffset.Value:X}" : null,
            PointerScore: treasuryOffset.HasValue ? 82 : null,
            PointerHitCount: treasuryOffset.HasValue ? 3 : 0,
            PointerChainCount: treasuryOffset.HasValue ? 1 : 0,
            WriteSucceeded: true,
            WrittenValue: 150000,
            Evidence: new[] { "ancienne preuve test" },
            Warnings: Array.Empty<string>());
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

    private static MemoryMapSnapshot CreateSnapshot(int processId, DateTimeOffset startTime)
    {
        var process = new Rome2ProcessInfo(
            ProcessId: processId,
            Name: "Rome2",
            Path: @"C:\Games\Total War - Rome 2\Rome2.exe",
            Architecture: ProcessArchitecture.X86,
            StartTime: startTime,
            MainWindowTitle: "Total War: ROME 2");
        var summary = new MemoryMapSummary(1, 1, 0x1000, 0x1000, 0);

        return new MemoryMapSnapshot(startTime, process, Array.Empty<ModuleInfo>(), Array.Empty<MemoryRegionInfo>(), summary);
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
