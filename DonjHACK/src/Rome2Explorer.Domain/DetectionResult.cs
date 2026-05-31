namespace Rome2Explorer.Domain;

public sealed record DetectionResult(
    string FeatureId,
    ulong? Address,
    string? StructureName,
    ulong? StructureBase,
    int? FieldOffset,
    OwnerKind Owner,
    double Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings,
    DetectionStatus Status);
