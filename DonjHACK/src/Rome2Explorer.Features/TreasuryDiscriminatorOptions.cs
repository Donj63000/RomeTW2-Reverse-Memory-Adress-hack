namespace Rome2Explorer.Features;

public sealed record TreasuryDiscriminatorOptions(
    TimeSpan ObservationDuration,
    TimeSpan PollInterval,
    int ContextBeforeBytes,
    int ContextAfterBytes,
    int MaxCandidates)
{
    public static TreasuryDiscriminatorOptions Default { get; } = new(
        ObservationDuration: TimeSpan.FromSeconds(10),
        PollInterval: TimeSpan.FromMilliseconds(250),
        ContextBeforeBytes: 128,
        ContextAfterBytes: 128,
        MaxCandidates: 10);
}
