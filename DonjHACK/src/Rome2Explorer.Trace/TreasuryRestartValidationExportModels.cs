using Rome2Explorer.Features;

namespace Rome2Explorer.Trace;

public sealed record TreasuryRestartValidationBundleResult(
    string BundleDirectory,
    string ManifestPath,
    string RankedCandidatesPath,
    string ValidationReportPath,
    string FullExportPath,
    TreasuryRestartValidationResult Result);

public sealed record TreasuryRestartValidationExportEnvelope(
    DateTimeOffset CreatedAt,
    TreasuryRestartValidationResult Result);

public sealed record TreasuryValidationReferenceLoadResult(
    IReadOnlyList<TreasuryValidationReference> References,
    IReadOnlyList<string> Warnings);
