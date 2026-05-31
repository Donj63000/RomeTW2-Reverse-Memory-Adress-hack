using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Trace;

public sealed class TreasuryRestartValidationExporter
{
    private readonly Func<DateTimeOffset> _clock;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TreasuryRestartValidationExporter()
        : this(() => DateTimeOffset.Now)
    {
    }

    public TreasuryRestartValidationExporter(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public TreasuryValidationReferenceLoadResult LoadReferences(string? donjHackRoot = null)
    {
        var references = new List<TreasuryValidationReference>();
        var warnings = new List<string>();
        var root = donjHackRoot ?? DonjHackPathResolver.ResolveRoot();

        LoadCandidates(root, references, warnings);
        LoadWrites(root, references, warnings);
        LoadStructures(root, references, warnings);
        LoadValidations(root, references, warnings);

        var deduped = references
            .GroupBy(BuildReferenceKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(reference => reference.CreatedAt).First())
            .OrderByDescending(reference => reference.CreatedAt)
            .ToArray();

        return new TreasuryValidationReferenceLoadResult(deduped, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public TreasuryRestartValidationBundleResult Export(TreasuryRestartValidationResult result, string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var exportRoot = DonjHackPathResolver.ResolveValidationsDirectory(donjHackRoot);
        Directory.CreateDirectory(exportRoot);
        var bundleDirectory = CreateUniqueBundleDirectory(exportRoot);
        var timestamp = result.CreatedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var manifestPath = Path.Combine(bundleDirectory, "manifest.json");
        var rankedCandidatesPath = Path.Combine(bundleDirectory, "ranked-candidates.json");
        var validationReportPath = Path.Combine(bundleDirectory, "validation-report.json");
        var fullExportPath = Path.Combine(bundleDirectory, $"treasury-validation-{timestamp}.json");
        var manifest = new TreasuryValidationManifest(
            CreatedAt: result.CreatedAt,
            FeatureId: result.FeatureId,
            Scenario: result.Scenario,
            OverallStatus: result.OverallStatus,
            CandidateCount: result.RankedCandidates.Count,
            ReferenceCount: result.ReferenceCount,
            OutputFiles: new[]
            {
                Path.GetFileName(manifestPath),
                Path.GetFileName(rankedCandidatesPath),
                Path.GetFileName(validationReportPath),
                Path.GetFileName(fullExportPath)
            });
        var ranked = new RankedTreasuryValidationCandidates(
            CreatedAt: result.CreatedAt,
            FeatureId: result.FeatureId,
            Candidates: result.RankedCandidates);
        var fullEnvelope = new TreasuryRestartValidationExportEnvelope(
            CreatedAt: _clock(),
            Result: result);

        WriteJson(manifestPath, manifest);
        WriteJson(rankedCandidatesPath, ranked);
        WriteJson(validationReportPath, result);
        WriteJson(fullExportPath, fullEnvelope);

        return new TreasuryRestartValidationBundleResult(
            BundleDirectory: bundleDirectory,
            ManifestPath: manifestPath,
            RankedCandidatesPath: rankedCandidatesPath,
            ValidationReportPath: validationReportPath,
            FullExportPath: fullExportPath,
            Result: result);
    }

    private void LoadCandidates(string root, List<TreasuryValidationReference> references, List<string> warnings)
    {
        foreach (var file in EnumerateFilesSafe(DonjHackPathResolver.ResolveCandidatesDirectory(root), "treasury-candidates-*.json"))
        {
            if (!TryReadJson<CandidateScanExportEnvelope>(file, warnings, out var envelope) || envelope is null)
            {
                continue;
            }

            foreach (var candidate in envelope.Session.Candidates)
            {
                references.Add(BuildReferenceFromCandidate(
                    "CandidatesExport",
                    file,
                    envelope.CreatedAt,
                    envelope.Process,
                    envelope.Session,
                    candidate,
                    null));
            }
        }
    }

    private void LoadWrites(string root, List<TreasuryValidationReference> references, List<string> warnings)
    {
        foreach (var file in EnumerateFilesSafe(DonjHackPathResolver.ResolveWritesDirectory(root), "treasury-write-*.json"))
        {
            if (!TryReadJson<TreasuryWriteExportEnvelope>(file, warnings, out var envelope) || envelope is null)
            {
                continue;
            }

            var candidate = envelope.Session.Candidates.FirstOrDefault(item =>
                string.Equals(item.CandidateId, envelope.Result.CandidateId, StringComparison.Ordinal))
                ?? envelope.Session.Candidates.FirstOrDefault(item => item.Address == envelope.Result.Address);

            if (candidate is null)
            {
                references.Add(BuildReferenceFromWrite(file, envelope));
                continue;
            }

            references.Add(BuildReferenceFromCandidate(
                "WriteExport",
                file,
                envelope.CreatedAt,
                envelope.Process,
                envelope.Session,
                candidate,
                envelope.Result));
        }
    }

    private void LoadStructures(string root, List<TreasuryValidationReference> references, List<string> warnings)
    {
        foreach (var file in EnumerateFilesSafe(DonjHackPathResolver.ResolveStructuresDirectory(root), "treasury-structure-*.json"))
        {
            if (!TryReadJson<TreasuryPointerAnalysisExportEnvelope>(file, warnings, out var envelope) || envelope is null)
            {
                continue;
            }

            var candidate = envelope.Session.Candidates.FirstOrDefault(item =>
                string.Equals(item.CandidateId, envelope.Result.CandidateId, StringComparison.Ordinal))
                ?? envelope.Session.Candidates.FirstOrDefault(item => item.Address == envelope.Result.CandidateAddress);

            if (candidate is null)
            {
                continue;
            }

            references.Add(BuildReferenceFromCandidate(
                "StructureExport",
                file,
                envelope.CreatedAt,
                envelope.Process,
                envelope.Session,
                candidate,
                null,
                envelope.Result));
        }
    }

    private void LoadValidations(string root, List<TreasuryValidationReference> references, List<string> warnings)
    {
        foreach (var file in EnumerateFilesSafe(DonjHackPathResolver.ResolveValidationsDirectory(root), "treasury-validation-*.json", recursive: true))
        {
            if (!TryReadJson<TreasuryRestartValidationExportEnvelope>(file, warnings, out var envelope) || envelope is null)
            {
                continue;
            }

            references.Add(envelope.Result.CurrentReference with
            {
                SourceKind = "ValidationExport",
                SourcePath = file
            });
        }
    }

    private static TreasuryValidationReference BuildReferenceFromCandidate(
        string sourceKind,
        string sourcePath,
        DateTimeOffset createdAt,
        Rome2ProcessInfo process,
        KnownValueScanSession session,
        Candidate candidate,
        TreasuryWriteResult? writeResult,
        TreasuryPointerAnalysisResult? pointerResult = null)
    {
        var region = candidate.Region;
        var best = pointerResult?.BestStructureBase;
        var evidence = candidate.Evidence
            .Concat(writeResult?.Evidence ?? Array.Empty<string>())
            .Concat(pointerResult?.Evidence ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warnings = candidate.Warnings
            .Concat(writeResult?.Warnings ?? Array.Empty<string>())
            .Concat(pointerResult?.Warnings ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new TreasuryValidationReference(
            CreatedAt: createdAt,
            SourceKind: sourceKind,
            SourcePath: sourcePath,
            FeatureId: session.FeatureId,
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            AddressHex: $"0x{candidate.Address:X}",
            Architecture: process.Architecture.ToString(),
            ProcessId: process.ProcessId,
            ProcessStartTime: process.StartTime,
            ProcessPath: process.Path,
            ValueHistory: session.ValueHistory.ToArray(),
            RegionBaseHex: region is null ? null : $"0x{region.BaseAddress:X}",
            RegionOffsetHex: region is null ? null : $"0x{candidate.Address - region.BaseAddress:X}",
            RegionProtection: region?.Protection,
            RegionType: region?.Type,
            PointerVerdict: pointerResult?.OverallVerdict,
            StructureBaseHex: best?.BaseAddressHex,
            TreasuryOffset: best?.TreasuryOffset,
            TreasuryOffsetHex: best?.TreasuryOffsetHex,
            PointerScore: best?.Score,
            PointerHitCount: pointerResult?.PointerHits.Count ?? 0,
            PointerChainCount: pointerResult?.PointerChains.Count ?? 0,
            WriteSucceeded: writeResult?.Success,
            WrittenValue: writeResult?.DesiredValue,
            Evidence: evidence,
            Warnings: warnings);
    }

    private static TreasuryValidationReference BuildReferenceFromWrite(string sourcePath, TreasuryWriteExportEnvelope envelope)
    {
        var result = envelope.Result;
        return new TreasuryValidationReference(
            CreatedAt: envelope.CreatedAt,
            SourceKind: "WriteExport",
            SourcePath: sourcePath,
            FeatureId: result.FeatureId,
            CandidateId: result.CandidateId,
            Address: result.Address,
            AddressHex: result.AddressHex,
            Architecture: envelope.Process.Architecture.ToString(),
            ProcessId: envelope.Process.ProcessId,
            ProcessStartTime: envelope.Process.StartTime,
            ProcessPath: envelope.Process.Path,
            ValueHistory: envelope.Session.ValueHistory.ToArray(),
            RegionBaseHex: result.RegionBaseHex,
            RegionOffsetHex: result.RegionOffsetHex,
            RegionProtection: result.RegionProtection,
            RegionType: result.RegionType,
            PointerVerdict: null,
            StructureBaseHex: null,
            TreasuryOffset: null,
            TreasuryOffsetHex: null,
            PointerScore: null,
            PointerHitCount: 0,
            PointerChainCount: 0,
            WriteSucceeded: result.Success,
            WrittenValue: result.DesiredValue,
            Evidence: result.Evidence,
            Warnings: result.Warnings);
    }

    private string CreateUniqueBundleDirectory(string exportRoot)
    {
        var timestamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var directoryName = suffix == 0
                ? $"treasury-validation-{timestamp}"
                : $"treasury-validation-{timestamp}-{suffix:000}";
            var directory = Path.Combine(exportRoot, directoryName);
            if (Directory.Exists(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            return directory;
        }

        throw new IOException($"Impossible de creer un bundle validation unique dans {exportRoot}.");
    }

    private static IReadOnlyList<string> EnumerateFilesSafe(string directory, string pattern, bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    private static bool TryReadJson<T>(string path, List<string> warnings, out T? payload)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            payload = JsonSerializer.Deserialize<T>(stream, JsonOptions);
            return payload is not null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add($"Export ignore {path}: {ex.Message}");
            payload = default;
            return false;
        }
    }

    private static void WriteJson<T>(string path, T payload)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, payload, JsonOptions);
    }

    private static string BuildReferenceKey(TreasuryValidationReference reference)
    {
        return string.Join(
            "|",
            reference.SourceKind,
            reference.SourcePath,
            reference.CandidateId,
            reference.AddressHex,
            reference.TreasuryOffsetHex,
            reference.WrittenValue?.ToString(CultureInfo.InvariantCulture));
    }
}
