namespace Rome2Explorer.Domain;

public sealed record Rome2ProcessCandidate(
    int ProcessId,
    string Name,
    string? Path,
    DateTimeOffset? StartTime,
    string? MainWindowTitle,
    bool HasMainWindow,
    bool IsRecommended,
    string RecommendationReason);
