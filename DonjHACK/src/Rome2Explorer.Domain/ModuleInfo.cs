namespace Rome2Explorer.Domain;

public sealed record ModuleInfo(
    string Name,
    ulong BaseAddress,
    long Size,
    string? Path);
