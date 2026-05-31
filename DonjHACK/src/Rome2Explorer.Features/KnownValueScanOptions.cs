namespace Rome2Explorer.Features;

public sealed record KnownValueScanOptions(
    int ChunkSizeBytes = 1024 * 1024,
    int MaxCandidates = 200_000)
{
    public static KnownValueScanOptions Default { get; } = new();
}
