using Rome2Explorer.Domain;

namespace Rome2Explorer.Features;

public sealed record KnownValueScanSession(
    string FeatureId,
    string ValueType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<int> ValueHistory,
    IReadOnlyList<Candidate> Candidates,
    KnownValueScanCounters Counters,
    IReadOnlyList<string> Warnings)
{
    public int? CurrentValue => ValueHistory.Count == 0 ? null : ValueHistory[^1];
}
