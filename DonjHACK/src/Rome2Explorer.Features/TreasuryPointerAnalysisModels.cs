namespace Rome2Explorer.Features;

public static class TreasuryPointerAnalysisVerdicts
{
    public const string NoPointerFound = "NoPointerFound";
    public const string RawCandidateOnly = "RawCandidateOnly";
    public const string ProbableStructure = "ProbableStructure";
    public const string AmbiguousStructure = "AmbiguousStructure";
    public const string NeedsRestartValidation = "NeedsRestartValidation";
}

public sealed record TreasuryPointerAnalysisResult(
    DateTimeOffset CreatedAt,
    string FeatureId,
    string CandidateId,
    ulong CandidateAddress,
    string CandidateAddressHex,
    string ValueType,
    int CandidateCount,
    IReadOnlyList<int> ValueHistory,
    int ContextByteCount,
    int ContextNonZeroByteCount,
    string ContextStartAddressHex,
    string ContextBytesHexPreview,
    int TestedBaseCount,
    int ScannedRegionCount,
    ulong ScannedBytes,
    bool HitLimitReached,
    string OverallVerdict,
    StructureBaseCandidate? BestStructureBase,
    IReadOnlyList<StructureBaseCandidate> StructureBases,
    IReadOnlyList<PointerHit> PointerHits,
    IReadOnlyList<PointerChainCandidate> PointerChains,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record StructureBaseCandidate(
    ulong BaseAddress,
    string BaseAddressHex,
    int TreasuryOffset,
    string TreasuryOffsetHex,
    string RegionBaseHex,
    string RegionProtection,
    string RegionType,
    int DirectPointerHitCount,
    int CandidatePointerHitCount,
    int PointerChainCount,
    double Score,
    string Status,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record PointerHit(
    string PointerId,
    int Level,
    ulong PointerAddress,
    string PointerAddressHex,
    ulong TargetAddress,
    string TargetAddressHex,
    string TargetKind,
    ulong SourceRegionBase,
    string SourceRegionBaseHex,
    string SourceRegionProtection,
    string SourceRegionType,
    string? SourceModuleName,
    string? SourceModuleOffsetHex,
    IReadOnlyList<string> Evidence);

public sealed record PointerChainCandidate(
    string ChainId,
    int Depth,
    ulong RootPointerAddress,
    string RootPointerAddressHex,
    ulong IntermediatePointerAddress,
    string IntermediatePointerAddressHex,
    ulong FinalTargetAddress,
    string FinalTargetAddressHex,
    string FinalTargetKind,
    double Score,
    IReadOnlyList<string> Evidence);
