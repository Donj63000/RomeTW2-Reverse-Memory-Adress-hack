namespace Rome2Explorer.Domain;

public sealed record Candidate(
    string FeatureId,
    string CandidateId,
    ulong Address,
    string Type,
    string? ObservedValue,
    string? ExpectedValue,
    MemoryRegionInfo? Region,
    SuspectedStructure? SuspectedStructure,
    OwnerKind Owner,
    double OwnerConfidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings,
    double Confidence);
