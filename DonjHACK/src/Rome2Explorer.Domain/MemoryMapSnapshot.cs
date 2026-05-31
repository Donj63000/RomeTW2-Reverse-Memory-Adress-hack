namespace Rome2Explorer.Domain;

public sealed record MemoryMapSnapshot(
    DateTimeOffset CreatedAt,
    Rome2ProcessInfo Process,
    IReadOnlyList<ModuleInfo> Modules,
    IReadOnlyList<MemoryRegionInfo> Regions,
    MemoryMapSummary Summary);
