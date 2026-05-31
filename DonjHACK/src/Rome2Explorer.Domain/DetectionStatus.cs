namespace Rome2Explorer.Domain;

public enum DetectionStatus
{
    Unknown = 0,
    Candidate = 1,
    Probable = 2,
    Validated = 3,
    ValidatedAfterRestart = 4,
    Ambiguous = 5,
    BrokenSignature = 6,
    UnsafeToWrite = 7,
    RequiresHook = 8,
    ProductionReady = 9
}
