using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Tests;

public sealed class AlgorithmeTests
{
    [Fact]
    public void ScoreTreasuryCandidates_PrefersCandidateWithHigherValidatedConfidence()
    {
        var weak = CreateCandidate(0x1000, confidence: 0.15);
        var strong = CreateCandidate(0x2000, confidence: 0.65, evidence: new[] { "Scan initial", "Refine OK" });
        var session = CreateSession(new[] { 67000, 64208, 63800 }, weak, strong);

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, CreateSnapshot(), Array.Empty<SavedMemoryFinding>());

        var favorite = scores.Single(score => score.IsAlgorithmFavorite);
        Assert.Equal(0x2000UL, favorite.Address);
        Assert.True(favorite.Score > scores.Single(score => score.Address == 0x1000UL).Score);
    }

    [Fact]
    public void ScoreTreasuryCandidates_BoostsAlreadySavedFinding()
    {
        var normal = CreateCandidate(0x3000, confidence: 0.40);
        var saved = CreateCandidate(0x4000, confidence: 0.40);
        var session = CreateSession(new[] { 67000, 64208 }, normal, saved);
        var savedFinding = CreateSavedFinding(0x4000);

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, CreateSnapshot(), new[] { savedFinding });

        Assert.Equal(0x4000UL, scores.Single(score => score.IsAlgorithmFavorite).Address);
        Assert.Contains(scores.Single(score => score.Address == 0x4000UL).Reasons, reason => reason.Contains("known finding compatible", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScoreTreasuryCandidates_MarksTieAsAmbiguousInsteadOfGreenFavorite()
    {
        var first = CreateCandidate(0x5000, confidence: 0.40);
        var second = CreateCandidate(0x6000, confidence: 0.40);
        var session = CreateSession(new[] { 67000, 64208 }, first, second);

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, CreateSnapshot(), Array.Empty<SavedMemoryFinding>());

        Assert.Empty(scores.Where(score => score.IsAlgorithmFavorite));
        Assert.Equal(2, scores.Count(score => score.IsAlgorithmAmbiguousTop));
        Assert.All(scores, score => Assert.Contains("ex aequo", score.Verdict, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScoreTreasuryCandidates_NeverMarksInitialScanAsGreenFavorite()
    {
        var candidate = CreateCandidate(0x7000, confidence: 0.90);
        var session = CreateSession(new[] { 67000 }, candidate);

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, CreateSnapshot(), Array.Empty<SavedMemoryFinding>());

        var score = scores.Single();
        Assert.False(score.IsAlgorithmFavorite);
        Assert.True(score.IsAlgorithmAmbiguousTop);
        Assert.Contains("refine obligatoire", score.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScoreTreasuryCandidates_KeepsDuplicateAddressesSeparatedByCandidateId()
    {
        var first = CreateCandidate(0x8000, confidence: 0.40, candidateId: "first-address-copy");
        var second = CreateCandidate(0x8000, confidence: 0.65, evidence: new[] { "Scan initial", "Refine OK" }, candidateId: "second-address-copy");
        var session = CreateSession(new[] { 67000, 64208 }, first, second);

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, CreateSnapshot(), Array.Empty<SavedMemoryFinding>());

        Assert.Equal(2, scores.Count);
        Assert.Contains(scores, score => score.CandidateId == "first-address-copy");
        Assert.Contains(scores, score => score.CandidateId == "second-address-copy");
    }

    [Fact]
    public void ScoreTreasuryCandidates_DoesNotBoostKnownFindingFromDifferentArchitecture()
    {
        var normal = CreateCandidate(0x9000, confidence: 0.40);
        var stale = CreateCandidate(0xA000, confidence: 0.40);
        var session = CreateSession(new[] { 67000, 64208 }, normal, stale);
        var staleFinding = CreateSavedFinding(0xA000, architecture: "X64");

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, CreateSnapshot(), new[] { staleFinding });

        var staleScore = scores.Single(score => score.Address == 0xA000UL);
        Assert.False(staleScore.IsAlgorithmFavorite);
        Assert.Contains(staleScore.Reasons, reason => reason.Contains("architecture differente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScoreTreasuryCandidates_UsesSafeModuleRangeWhenModuleEndWouldOverflow()
    {
        var candidate = CreateCandidate(ulong.MaxValue - 0x10, confidence: 0.40);
        var session = CreateSession(new[] { 67000, 64208 }, candidate);
        var module = new ModuleInfo("Rome2.exe", ulong.MaxValue - 0x20, 0x40, @"C:\Games\Rome2.exe");
        var snapshot = CreateSnapshot(modules: new[] { module });

        var scores = new Algorithme().ScoreTreasuryCandidates(session, session.Candidates, snapshot, Array.Empty<SavedMemoryFinding>());

        Assert.Contains(scores.Single().Reasons, reason => reason.Contains("offset module Rome2.exe+0x10", StringComparison.OrdinalIgnoreCase));
    }

    private static KnownValueScanSession CreateSession(IReadOnlyList<int> history, params Candidate[] candidates)
    {
        var now = DateTimeOffset.Now;
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: now,
            UpdatedAt: now,
            ValueHistory: history,
            Candidates: candidates,
            Counters: new KnownValueScanCounters(1, 1, 0, 4, 0, candidates.Length, candidates.Length),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(
        ulong address,
        double confidence,
        IReadOnlyList<string>? evidence = null,
        string? candidateId = null)
    {
        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: candidateId ?? $"candidate-0x{address:X}",
            Address: address,
            Type: "Int32",
            ObservedValue: "64208",
            ExpectedValue: "64208",
            Region: CreateRegion(address & 0xFFFF0000),
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: evidence ?? new[] { "Scan initial" },
            Warnings: Array.Empty<string>(),
            Confidence: confidence);
    }

    private static MemoryRegionInfo CreateRegion(ulong baseAddress)
    {
        return new MemoryRegionInfo(
            BaseAddress: baseAddress,
            Size: 0x10000,
            State: "MEM_COMMIT",
            Protection: "PAGE_READWRITE",
            Type: "MEM_PRIVATE",
            IsReadable: true,
            IsWritable: true,
            IsExecutable: false);
    }

    private static MemoryMapSnapshot CreateSnapshot(
        ProcessArchitecture architecture = ProcessArchitecture.X86,
        IReadOnlyList<ModuleInfo>? modules = null,
        string? path = @"C:\Games\Rome2.exe")
    {
        return new MemoryMapSnapshot(
            CreatedAt: DateTimeOffset.Now,
            Process: new Rome2ProcessInfo(13548, "Rome2", path, architecture, DateTimeOffset.Now, "Total War: ROME 2"),
            Modules: modules ?? Array.Empty<ModuleInfo>(),
            Regions: Array.Empty<MemoryRegionInfo>(),
            Summary: new MemoryMapSummary(0, 0, 0, 0, 0));
    }

    private static SavedMemoryFinding CreateSavedFinding(ulong address, string architecture = "X86")
    {
        return new SavedMemoryFinding(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            Label: $"treasury 0x{address:X}",
            SavedAt: DateTimeOffset.Now,
            ProcessId: 13548,
            ProcessName: "Rome2",
            Architecture: architecture,
            ProcessPath: null,
            Address: address,
            AddressHex: $"0x{address:X}",
            Type: "Int32",
            Status: "Plausible",
            ObservedValue: "64208",
            ExpectedValue: "64208",
            Confidence: 0.40,
            ModuleName: null,
            ModuleBaseHex: null,
            ModuleOffsetHex: null,
            RegionBaseHex: "0x0",
            RegionOffsetHex: "0x0",
            RegionState: "MEM_COMMIT",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            HookStatus: "Aucun hook - lecture seule",
            OffsetStatus: "region dynamique",
            PointerStatus: "Non resolu",
            StabilityStatus: "A revalider",
            ValueHistory: new[] { 67000, 64208 },
            Evidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>());
    }
}
