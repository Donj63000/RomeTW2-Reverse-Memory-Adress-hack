namespace Rome2Explorer.Features;

public sealed record TreasuryPointerAnalysisOptions(
    int ContextBeforeBytes = 0x200,
    int ContextAfterBytes = 0x400,
    int MaxStructureOffset = 0x400,
    int Alignment = 4,
    int ChunkSizeBytes = 1024 * 1024,
    int MaxPointerHits = 10_000,
    int MaxPointerChains = 10_000,
    int MaxBaseCandidates = 128)
{
    public static TreasuryPointerAnalysisOptions Default { get; } = new();
}
