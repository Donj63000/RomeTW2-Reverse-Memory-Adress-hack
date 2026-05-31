namespace Rome2Explorer.Memory;

public sealed record MemoryProtectionClassification(
    string State,
    string Protection,
    string Type,
    bool IsReadable,
    bool IsWritable,
    bool IsExecutable);
