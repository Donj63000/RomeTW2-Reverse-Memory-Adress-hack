namespace Rome2Explorer.Domain;

public sealed record Rome2ProcessInfo(
    int ProcessId,
    string Name,
    string? Path,
    ProcessArchitecture Architecture,
    DateTimeOffset? StartTime,
    string? MainWindowTitle);
