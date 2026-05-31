using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Trace;

public sealed record TreasuryDiscriminatorExportEnvelope(
    DateTimeOffset CreatedAt,
    Rome2ProcessInfo Process,
    MemoryMapSummary MemorySummary,
    KnownValueScanSession Session,
    TreasuryDiscriminatorResult Result);
