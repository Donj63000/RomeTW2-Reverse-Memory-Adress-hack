using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Tests;

public sealed class TreasuryScanGuidanceTests
{
    [Fact]
    public void Build_NullSession_ExplainsInitialProcedure()
    {
        var guidance = TreasuryScanGuidance.Build(null);

        Assert.Equal("Aucun scan lance", guidance.CurrentStep);
        Assert.Contains("Attache Rome2", guidance.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start Scan", guidance.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InitialScanWithMultipleCandidates_ExplainsCannotKnowCorrectCandidate()
    {
        var session = CreateSession(new[] { 67000 }, CreateCandidate(0x1000), CreateCandidate(0x2000));

        var guidance = TreasuryScanGuidance.Build(session);

        Assert.Equal("Scan initial termine - 2 candidats", guidance.CurrentStep);
        Assert.Contains("Impossible de savoir lequel est bon", guidance.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remplace 67000", guidance.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plusieurs zones memoire", guidance.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RefineWithMultipleCandidates_GuidesToDiscriminator()
    {
        var session = CreateSession(new[] { 67000, 64208 }, CreateCandidate(0x1000, 0.40), CreateCandidate(0x2000, 0.40));

        var guidance = TreasuryScanGuidance.Build(session);

        Assert.Equal("Refine termine - 2 candidats synchronises", guidance.CurrentStep);
        Assert.Contains("scan exact ne suffit plus", guidance.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Departager candidats", guidance.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("67000 -> 64208", guidance.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("analyse pointeur", guidance.ExpectedResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RefineWithSingleCandidate_MarksVeryLikelyAndAsksFinalValidation()
    {
        var session = CreateSession(new[] { 67000, 64500 }, CreateCandidate(0x1000, 0.40));

        var guidance = TreasuryScanGuidance.Build(session);

        Assert.Equal("Candidat tres probable", guidance.CurrentStep);
        Assert.Contains("une seule adresse", guidance.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valider definitivement", guidance.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RefineWithNoCandidates_AsksRestart()
    {
        var session = CreateSession(new[] { 67000, 64500 });

        var guidance = TreasuryScanGuidance.Build(session);

        Assert.Equal("Refine sans resultat", guidance.CurrentStep);
        Assert.Contains("recommence depuis Start Scan", guidance.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCandidateStatus_ReturnsExpectedStatuses()
    {
        var rawCandidate = CreateCandidate(0x1000, 0.15);
        var plausibleCandidate = CreateCandidate(0x1000, 0.40);
        var likelyCandidateByHistory = CreateCandidate(0x1000, 0.65);

        Assert.Equal(
            TreasuryScanGuidance.RawStatus,
            TreasuryScanGuidance.GetCandidateStatus(CreateSession(new[] { 67000 }, rawCandidate), rawCandidate));

        Assert.Equal(
            TreasuryScanGuidance.PlausibleStatus,
            TreasuryScanGuidance.GetCandidateStatus(CreateSession(new[] { 67000, 64500 }, plausibleCandidate), plausibleCandidate));

        Assert.Equal(
            TreasuryScanGuidance.VeryLikelyStatus,
            TreasuryScanGuidance.GetCandidateStatus(CreateSession(new[] { 67000, 64500, 61200 }, likelyCandidateByHistory), likelyCandidateByHistory));
    }

    private static KnownValueScanSession CreateSession(IReadOnlyList<int> valueHistory, params Candidate[] candidates)
    {
        var now = DateTimeOffset.Now;
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: now,
            UpdatedAt: now,
            ValueHistory: valueHistory,
            Candidates: candidates,
            Counters: new KnownValueScanCounters(
                RegionsConsidered: 1,
                RegionsScanned: 1,
                RegionsSkipped: 0,
                BytesScanned: 4,
                ReadFailures: 0,
                CandidatesBefore: candidates.Length,
                CandidatesAfter: candidates.Length),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(ulong address, double confidence = 0.15)
    {
        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: $"treasury-int32-0x{address:X8}",
            Address: address,
            Type: "Int32",
            ObservedValue: "67000",
            ExpectedValue: "67000",
            Region: null,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Confidence: confidence);
    }
}
