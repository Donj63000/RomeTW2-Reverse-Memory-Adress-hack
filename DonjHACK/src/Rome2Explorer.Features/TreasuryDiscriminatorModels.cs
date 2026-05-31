namespace Rome2Explorer.Features;

public sealed record TreasuryDiscriminatorResult(
    DateTimeOffset CreatedAt,
    string FeatureId,
    IReadOnlyList<int> ValueHistory,
    TimeSpan ObservationDuration,
    TimeSpan PollInterval,
    int CandidateCount,
    string OverallVerdict,
    string? FavoriteCandidateId,
    string? FavoriteAddressHex,
    bool IsAmbiguous,
    IReadOnlyList<TreasuryDiscriminatorCandidateResult> Candidates,
    IReadOnlyList<TreasuryDiscriminatorSample> Samples,
    IReadOnlyList<TreasuryDiscriminatorLogEntry> Log);

public sealed record TreasuryDiscriminatorCandidateResult(
    string CandidateId,
    ulong Address,
    string AddressHex,
    string Type,
    string RegionBaseHex,
    string RegionOffsetHex,
    string RegionState,
    string RegionProtection,
    string RegionType,
    int? InitialValue,
    int? FinalValue,
    int SuccessfulReads,
    int FailedReads,
    int ChangeCount,
    int? FirstChangeSampleIndex,
    DateTimeOffset? FirstChangeAt,
    bool StableAfterFirstChange,
    ulong ContextStartAddress,
    string ContextStartAddressHex,
    int ContextByteCount,
    int ContextNonZeroByteCount,
    string ContextBytesHex,
    double Score,
    string Verdict,
    bool IsFavorite,
    bool IsSynchronized,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings);

public sealed record TreasuryDiscriminatorSample(
    DateTimeOffset Timestamp,
    int SampleIndex,
    string CandidateId,
    ulong Address,
    string AddressHex,
    bool Success,
    int? Value,
    string? Warning);

public sealed record TreasuryDiscriminatorLogEntry(
    DateTimeOffset Timestamp,
    string EventType,
    string? CandidateId,
    string? AddressHex,
    string Message);
