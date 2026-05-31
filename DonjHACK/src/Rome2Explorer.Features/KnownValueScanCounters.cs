namespace Rome2Explorer.Features;

public sealed record KnownValueScanCounters(
    int RegionsConsidered,
    int RegionsScanned,
    int RegionsSkipped,
    ulong BytesScanned,
    int ReadFailures,
    int CandidatesBefore,
    int CandidatesAfter);
