using Rome2Explorer.Domain;

namespace Rome2Explorer.Features;

public static class TreasuryRestartValidationStatuses
{
    public const string RawOnly = "RawOnly";
    public const string SameSessionOnly = "SameSessionOnly";
    public const string ProbableAfterReload = "ProbableAfterReload";
    public const string ValidatedAfterRestart = "ValidatedAfterRestart";
    public const string Ambiguous = "Ambiguous";
    public const string Broken = "Broken";
}

public sealed record TreasuryValidationReference(
    DateTimeOffset CreatedAt,
    string SourceKind,
    string SourcePath,
    string FeatureId,
    string? CandidateId,
    ulong? Address,
    string? AddressHex,
    string? Architecture,
    int? ProcessId,
    DateTimeOffset? ProcessStartTime,
    string? ProcessPath,
    IReadOnlyList<int> ValueHistory,
    string? RegionBaseHex,
    string? RegionOffsetHex,
    string? RegionProtection,
    string? RegionType,
    string? PointerVerdict,
    string? StructureBaseHex,
    int? TreasuryOffset,
    string? TreasuryOffsetHex,
    double? PointerScore,
    int PointerHitCount,
    int PointerChainCount,
    bool? WriteSucceeded,
    int? WrittenValue,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record TreasuryRestartValidationResult(
    DateTimeOffset CreatedAt,
    string FeatureId,
    string Scenario,
    string OverallStatus,
    string OverallVerdict,
    Rome2ProcessInfo Process,
    MemoryMapSummary MemorySummary,
    TreasuryValidationReference CurrentReference,
    int ReferenceCount,
    IReadOnlyList<TreasuryValidationReference> ReferencesUsed,
    IReadOnlyList<TreasuryValidationCandidateResult> RankedCandidates,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record TreasuryValidationCandidateResult(
    string CandidateId,
    ulong Address,
    string AddressHex,
    bool IsSelected,
    string Status,
    double Score,
    string RegionBaseHex,
    string RegionOffsetHex,
    string RegionProtection,
    string RegionType,
    string PointerStatus,
    string? StructureBaseHex,
    string? TreasuryOffsetHex,
    int MatchedReferenceCount,
    string? StrongestReferenceKind,
    string? StrongestReferencePath,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record TreasuryValidationManifest(
    DateTimeOffset CreatedAt,
    string FeatureId,
    string Scenario,
    string OverallStatus,
    int CandidateCount,
    int ReferenceCount,
    IReadOnlyList<string> OutputFiles);

public sealed record RankedTreasuryValidationCandidates(
    DateTimeOffset CreatedAt,
    string FeatureId,
    IReadOnlyList<TreasuryValidationCandidateResult> Candidates);
