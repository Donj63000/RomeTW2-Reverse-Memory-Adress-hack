namespace Rome2Explorer.Features;

public static class TreasuryWriteStatuses
{
    public const string Ready = "Pret";
    public const string Refused = "Ecriture refusee";
    public const string Success = "Ecriture OK";
    public const string Overwritten = "Valeur ecrasee par le jeu";
    public const string Error = "Erreur ecriture";
    public const string AmbiguousCandidate = "Candidat ambigu";
}

public sealed record TreasuryWriteResult(
    DateTimeOffset CreatedAt,
    string FeatureId,
    bool Success,
    string Status,
    string Message,
    string? CandidateId,
    ulong? Address,
    string? AddressHex,
    string Type,
    int CandidateCount,
    bool IsAmbiguousSelection,
    int? ExpectedCurrentValue,
    int DesiredValue,
    int? ValueBefore,
    int? ValueAfterWrite,
    int? ValueAfterStabilityDelay,
    string? RegionBaseHex,
    string? RegionOffsetHex,
    string? RegionState,
    string? RegionProtection,
    string? RegionType,
    IReadOnlyList<string> CandidateEvidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);
