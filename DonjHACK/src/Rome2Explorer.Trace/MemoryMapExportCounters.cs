namespace Rome2Explorer.Trace;

public sealed record MemoryMapExportCounters(
    int ProcessId,
    int ModuleCount,
    int RegionCount,
    int ReadableRegionCount,
    int WritableRegionCount,
    int ExecutableRegionCount,
    ulong TotalReadableBytes,
    ulong TotalWritableBytes,
    ulong TotalExecutableBytes);
