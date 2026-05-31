namespace Rome2Explorer.Domain;

public sealed record SuspectedStructure(
    string? StructureType,
    ulong? BaseAddress,
    int? FieldOffset,
    double Confidence);
