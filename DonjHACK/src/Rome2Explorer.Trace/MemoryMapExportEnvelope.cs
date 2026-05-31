using Rome2Explorer.Domain;

namespace Rome2Explorer.Trace;

public sealed record MemoryMapExportEnvelope(
    MemoryMapSnapshot Snapshot,
    MemoryMapExportCounters Counters);
