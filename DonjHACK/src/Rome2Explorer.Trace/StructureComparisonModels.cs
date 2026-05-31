using Rome2Explorer.Domain;

namespace Rome2Explorer.Trace;

public sealed record StructureComparisonBundleResult(
    string BundleDirectory,
    string ManifestPath,
    string RankedCandidatesPath,
    string ValidationReportPath,
    StructureComparisonReport Report);

public sealed record StructureComparisonReport(
    DateTimeOffset CreatedAt,
    string FeatureId,
    string OverallStatus,
    string OverallVerdict,
    int CaptureCount,
    int ScenarioCount,
    IReadOnlyList<string> InputCapturePaths,
    IReadOnlyList<StructureComparisonResult> Results,
    IReadOnlyList<string> Warnings);

public sealed record StructureComparisonResult(
    string CandidateId,
    ulong Address,
    string AddressHex,
    string Status,
    double Score,
    int CaptureCount,
    int ScenarioCount,
    StructureSuspectedBase SuspectedBase,
    IReadOnlyList<StructureFieldComparison> FieldOffsets,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings,
    string ComparisonSignature);

public sealed record StructureSuspectedBase(
    string? AddressHex,
    int? TreasuryFieldRelativeOffset,
    double Confidence,
    string Reason);

public sealed record StructureFieldComparison(
    int RelativeOffset,
    string RelativeOffsetHex,
    int ObservationCount,
    int MatchKnownValueCount,
    int DistinctValueCount,
    int? FirstValue,
    int? LastValue,
    double Score,
    string Status,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record StructureComparisonManifest(
    DateTimeOffset CreatedAt,
    string FeatureId,
    string OverallStatus,
    int CaptureCount,
    int CandidateCount,
    IReadOnlyList<string> InputCapturePaths,
    IReadOnlyList<string> OutputFiles);

public sealed record RankedStructureCandidates(
    DateTimeOffset CreatedAt,
    string FeatureId,
    IReadOnlyList<StructureComparisonResult> Candidates);

internal sealed record LuaCaptureCandidateObservation(
    LuaMemoryCaptureEnvelope Capture,
    CapturedCandidateWindow Window,
    Candidate? SessionCandidate,
    ulong Address);
