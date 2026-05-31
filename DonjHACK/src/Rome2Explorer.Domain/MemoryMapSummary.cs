namespace Rome2Explorer.Domain;

public sealed record MemoryMapSummary(
    int ModuleCount,
    int RegionCount,
    ulong TotalReadableBytes,
    ulong TotalWritableBytes,
    ulong TotalExecutableBytes);
