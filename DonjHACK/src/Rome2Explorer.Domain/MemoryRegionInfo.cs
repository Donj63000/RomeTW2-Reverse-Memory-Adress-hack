namespace Rome2Explorer.Domain;

public sealed record MemoryRegionInfo(
    ulong BaseAddress,
    ulong Size,
    string State,
    string Protection,
    string Type,
    bool IsReadable,
    bool IsWritable,
    bool IsExecutable);
