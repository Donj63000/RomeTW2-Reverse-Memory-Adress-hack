using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;

namespace Rome2Explorer.Trace;

public sealed class TreasuryStructureCaptureImporter
{
    private readonly Func<DateTimeOffset> _clock;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TreasuryStructureCaptureImporter()
        : this(() => DateTimeOffset.Now)
    {
    }

    public TreasuryStructureCaptureImporter(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public LuaMemoryCaptureEnvelope LoadCapture(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin de capture Lua est vide.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Capture Lua introuvable.", path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var capture = JsonSerializer.Deserialize<LuaMemoryCaptureEnvelope>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Capture Lua illisible : {path}.");

        ValidateCapture(capture, path);
        return capture;
    }

    public StructureComparisonBundleResult ImportAndCompareTreasury(
        KnownValueScanSession session,
        IReadOnlyList<string> capturePaths,
        string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(capturePaths);

        if (capturePaths.Count == 0)
        {
            throw new InvalidOperationException("Aucune capture CE/Lua selectionnee.");
        }

        var captures = capturePaths.Select(LoadCapture).ToArray();
        var report = CompareTreasury(session, captures, capturePaths);
        return Export(report, donjHackRoot);
    }

    public StructureComparisonReport CompareTreasury(
        KnownValueScanSession session,
        IReadOnlyList<LuaMemoryCaptureEnvelope> captures,
        IReadOnlyList<string>? inputCapturePaths = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(captures);

        if (!string.Equals(session.FeatureId, KnownValueScanner.TreasuryFeatureId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Session non supportee : {session.FeatureId}.");
        }

        if (captures.Count == 0)
        {
            throw new InvalidOperationException("Aucune capture CE/Lua a comparer.");
        }

        var warnings = new List<string>();
        foreach (var capture in captures)
        {
            ValidateCapture(capture, "capture en memoire");
            if (!string.Equals(capture.FeatureId, session.FeatureId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Capture featureId {capture.FeatureId} incompatible avec la session {session.FeatureId}.");
            }
        }

        var sessionCandidatesByAddress = session.Candidates
            .GroupBy(candidate => candidate.Address)
            .ToDictionary(group => group.Key, group => group.First());
        var observations = BuildObservations(captures, sessionCandidatesByAddress, warnings);
        var initialResults = session.Candidates
            .Select(candidate => BuildCandidateResult(candidate, observations.Where(observation => observation.Address == candidate.Address).ToArray()))
            .ToArray();
        var synchronizedResults = ApplySynchronizationPenalty(initialResults)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Address)
            .ToArray();
        var scenarioCount = captures
            .Select(BuildScenarioKey)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var overall = BuildOverallVerdict(synchronizedResults);

        warnings.AddRange(observations
            .Where(observation => observation.SessionCandidate is null)
            .Select(observation => $"Capture ignoree : {observation.Window.CandidateId ?? observation.Window.AddressHex ?? "unknown"} n'existe pas dans la session active.")
            .Distinct(StringComparer.Ordinal));

        return new StructureComparisonReport(
            CreatedAt: _clock(),
            FeatureId: session.FeatureId,
            OverallStatus: overall.Status,
            OverallVerdict: overall.Verdict,
            CaptureCount: captures.Count,
            ScenarioCount: scenarioCount,
            InputCapturePaths: inputCapturePaths?.ToArray() ?? Array.Empty<string>(),
            Results: synchronizedResults,
            Warnings: warnings.ToArray());
    }

    private StructureComparisonBundleResult Export(StructureComparisonReport report, string? donjHackRoot)
    {
        var exportRoot = DonjHackPathResolver.ResolveStructureComparisonsDirectory(donjHackRoot);
        Directory.CreateDirectory(exportRoot);
        var bundleDirectory = CreateUniqueBundleDirectory(exportRoot);
        var manifestPath = Path.Combine(bundleDirectory, "manifest.json");
        var rankedCandidatesPath = Path.Combine(bundleDirectory, "ranked-candidates.json");
        var validationReportPath = Path.Combine(bundleDirectory, "validation-report.json");
        var manifest = new StructureComparisonManifest(
            CreatedAt: report.CreatedAt,
            FeatureId: report.FeatureId,
            OverallStatus: report.OverallStatus,
            CaptureCount: report.CaptureCount,
            CandidateCount: report.Results.Count,
            InputCapturePaths: report.InputCapturePaths,
            OutputFiles: new[]
            {
                Path.GetFileName(manifestPath),
                Path.GetFileName(rankedCandidatesPath),
                Path.GetFileName(validationReportPath)
            });
        var ranked = new RankedStructureCandidates(
            CreatedAt: report.CreatedAt,
            FeatureId: report.FeatureId,
            Candidates: report.Results);

        WriteJson(manifestPath, manifest);
        WriteJson(rankedCandidatesPath, ranked);
        WriteJson(validationReportPath, report);

        return new StructureComparisonBundleResult(
            BundleDirectory: bundleDirectory,
            ManifestPath: manifestPath,
            RankedCandidatesPath: rankedCandidatesPath,
            ValidationReportPath: validationReportPath,
            Report: report);
    }

    private string CreateUniqueBundleDirectory(string exportRoot)
    {
        var timestamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var directoryName = suffix == 0
                ? $"treasury-structure-comparison-{timestamp}"
                : $"treasury-structure-comparison-{timestamp}-{suffix:000}";
            var directory = Path.Combine(exportRoot, directoryName);
            if (Directory.Exists(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            return directory;
        }

        throw new IOException($"Impossible de creer un bundle structure unique dans {exportRoot}.");
    }

    private static void WriteJson<T>(string path, T payload)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, payload, JsonOptions);
    }

    private static IReadOnlyList<LuaCaptureCandidateObservation> BuildObservations(
        IReadOnlyList<LuaMemoryCaptureEnvelope> captures,
        IReadOnlyDictionary<ulong, Candidate> sessionCandidatesByAddress,
        List<string> warnings)
    {
        var observations = new List<LuaCaptureCandidateObservation>();

        foreach (var capture in captures)
        {
            foreach (var window in capture.Candidates)
            {
                if (!TryResolveAddress(window.Address, window.AddressHex, out var address))
                {
                    warnings.Add($"Capture ignoree : adresse invalide {window.AddressHex ?? "n/a"}.");
                    continue;
                }

                sessionCandidatesByAddress.TryGetValue(address, out var sessionCandidate);
                observations.Add(new LuaCaptureCandidateObservation(capture, window, sessionCandidate, address));
            }
        }

        return observations;
    }

    private static StructureComparisonResult BuildCandidateResult(
        Candidate candidate,
        IReadOnlyList<LuaCaptureCandidateObservation> observations)
    {
        if (observations.Count == 0)
        {
            return new StructureComparisonResult(
                CandidateId: candidate.CandidateId,
                Address: candidate.Address,
                AddressHex: $"0x{candidate.Address:X}",
                Status: "Unknown",
                Score: 0,
                CaptureCount: 0,
                ScenarioCount: 0,
                SuspectedBase: new StructureSuspectedBase(null, null, 0, "Aucune capture CE/Lua pour ce candidat."),
                FieldOffsets: Array.Empty<StructureFieldComparison>(),
                Evidence: Array.Empty<string>(),
                Warnings: new[] { "Aucune fenetre memoire importee pour ce candidat." },
                ComparisonSignature: string.Empty);
        }

        var scenarioCount = observations
            .Select(observation => BuildScenarioKey(observation.Capture))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var fieldComparisons = BuildFieldComparisons(observations);
        var bestField = fieldComparisons.FirstOrDefault();
        var followsKnownValue = fieldComparisons.Any(field => field.Status == "FollowsKnownValues");
        var stableFields = fieldComparisons.Count(field => field.Status == "StableContext");
        var readableContext = observations.Any(observation => observation.Window.Region?.IsReadable == true || candidate.Region?.IsReadable == true);
        var pointerLikeCount = observations.Sum(observation => observation.Window.PointerLikeValues?.Count ?? 0);
        var evidence = new List<string>
        {
            $"{observations.Count} capture(s) CE/Lua importee(s).",
            $"{scenarioCount} scenario/step distinct(s)."
        };
        var warnings = observations
            .SelectMany(observation => observation.Window.Warnings ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var score = 0.0;
        Add(ref score, Math.Min(20, observations.Count * 10), evidence, "fenetres memoire importees");
        if (scenarioCount >= 2)
        {
            Add(ref score, 10, evidence, "captures multi-scenarios");
        }

        if (bestField is not null)
        {
            Add(ref score, Math.Min(40, bestField.Score * 0.5), evidence, $"meilleur offset relatif {bestField.RelativeOffsetHex} : {bestField.Status}");
        }

        if (readableContext)
        {
            Add(ref score, 5, evidence, "region contexte lisible");
        }

        if (pointerLikeCount > 0)
        {
            Add(ref score, Math.Min(8, pointerLikeCount), evidence, $"{pointerLikeCount} valeur(s) pointeur-like dans les fenetres");
        }

        if (stableFields > 0)
        {
            Add(ref score, Math.Min(8, stableFields * 2), evidence, $"{stableFields} offset(s) de contexte stable(s)");
        }

        if (!followsKnownValue)
        {
            warnings.Add("Aucun offset relatif ne suit les valeurs treasury UI sur plusieurs captures.");
        }

        var suspectedBase = BuildSuspectedBase(bestField, followsKnownValue);
        var clampedScore = Math.Round(Math.Clamp(score - Math.Min(12, warnings.Count * 2), 0, 100), 2);
        var status = BuildCandidateStatus(clampedScore, followsKnownValue, isSynchronized: false);
        var comparisonSignature = BuildComparisonSignature(fieldComparisons);

        return new StructureComparisonResult(
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            AddressHex: $"0x{candidate.Address:X}",
            Status: status,
            Score: clampedScore,
            CaptureCount: observations.Count,
            ScenarioCount: scenarioCount,
            SuspectedBase: suspectedBase,
            FieldOffsets: fieldComparisons,
            Evidence: evidence.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings: warnings.ToArray(),
            ComparisonSignature: comparisonSignature);
    }

    private static IReadOnlyList<StructureFieldComparison> BuildFieldComparisons(IReadOnlyList<LuaCaptureCandidateObservation> observations)
    {
        return observations
            .SelectMany(observation => (observation.Window.DecodedInt32Fields ?? Array.Empty<CapturedInt32Field>())
                .Select(field => new FieldObservation(observation.Capture, field)))
            .GroupBy(observation => observation.Field.RelativeOffset)
            .Select(BuildFieldComparison)
            .OrderByDescending(field => field.Score)
            .ThenBy(field => Math.Abs(field.RelativeOffset))
            .ThenBy(field => field.RelativeOffset)
            .ToArray();
    }

    private static StructureFieldComparison BuildFieldComparison(IGrouping<int, FieldObservation> group)
    {
        var observations = group.ToArray();
        var matchCount = observations.Count(observation =>
            observation.Field.MatchesUiValue || observation.Field.Value == observation.Capture.KnownValues?.UiTreasury);
        var distinctValues = observations
            .Select(observation => observation.Field.Value)
            .Distinct()
            .Count();
        var distinctUiValues = observations
            .Select(observation => observation.Capture.KnownValues?.UiTreasury)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .Count();
        var followsKnownValues = observations.Length >= 2
            && matchCount == observations.Length
            && distinctValues >= 2
            && distinctUiValues >= 2;
        var stableContext = observations.Length >= 2
            && distinctValues == 1
            && matchCount == 0;
        var status = followsKnownValues
            ? "FollowsKnownValues"
            : stableContext
                ? "StableContext"
                : "NoisyOrUnproven";
        var evidence = new List<string>();
        var warnings = new List<string>();
        var score = 0.0;
        var matchRatio = observations.Length == 0 ? 0 : (double)matchCount / observations.Length;

        Add(ref score, matchRatio * 40, evidence, $"{matchCount}/{observations.Length} valeur(s) egales a l'UI");
        if (followsKnownValues)
        {
            Add(ref score, 30, evidence, "variation conforme aux valeurs UI sur plusieurs captures");
            if (group.Key != 0)
            {
                Add(ref score, 5, evidence, "offset non contigu conserve par comparaison");
            }
        }
        else if (stableContext)
        {
            Add(ref score, 8, evidence, "champ stable utile pour identifier la structure");
        }
        else if (distinctValues > distinctUiValues && matchCount > 0)
        {
            warnings.Add("Offset partiellement synchronise mais trop bruite.");
            Add(ref score, -8, evidence, "bruit detecte");
        }

        return new StructureFieldComparison(
            RelativeOffset: group.Key,
            RelativeOffsetHex: FormatRelativeOffset(group.Key),
            ObservationCount: observations.Length,
            MatchKnownValueCount: matchCount,
            DistinctValueCount: distinctValues,
            FirstValue: observations.FirstOrDefault()?.Field.Value,
            LastValue: observations.LastOrDefault()?.Field.Value,
            Score: Math.Round(Math.Clamp(score, 0, 100), 2),
            Status: status,
            Evidence: evidence.ToArray(),
            Warnings: warnings.ToArray());
    }

    private static IReadOnlyList<StructureComparisonResult> ApplySynchronizationPenalty(IReadOnlyList<StructureComparisonResult> results)
    {
        var synchronizedSignatures = results
            .Where(result => !string.IsNullOrWhiteSpace(result.ComparisonSignature))
            .GroupBy(result => result.ComparisonSignature, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (synchronizedSignatures.Count == 0)
        {
            return results;
        }

        return results
            .Select(result =>
            {
                if (!synchronizedSignatures.Contains(result.ComparisonSignature))
                {
                    return result;
                }

                var score = Math.Round(Math.Max(0, result.Score - 15), 2);
                var warnings = result.Warnings
                    .Concat(new[] { "Pattern synchronise avec un autre candidat : preuve structurelle ambigue." })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                return result with
                {
                    Score = score,
                    Status = BuildCandidateStatus(score, result.FieldOffsets.Any(field => field.Status == "FollowsKnownValues"), isSynchronized: true),
                    Warnings = warnings
                };
            })
            .ToArray();
    }

    private static StructureSuspectedBase BuildSuspectedBase(StructureFieldComparison? bestField, bool followsKnownValue)
    {
        if (bestField is null || !followsKnownValue)
        {
            return new StructureSuspectedBase(null, null, 0, "Base structure non deduite par les captures.");
        }

        var confidence = bestField.RelativeOffset == 0 ? 0.25 : 0.45;
        var reason = bestField.RelativeOffset == 0
            ? "La valeur treasury est confirmee, mais la base structure reste inconnue."
            : $"Un champ treasury plausible apparait a l'offset relatif {bestField.RelativeOffsetHex}; base a confirmer avec ReClass/CE.";

        return new StructureSuspectedBase(null, bestField.RelativeOffset, confidence, reason);
    }

    private static string BuildCandidateStatus(double score, bool followsKnownValue, bool isSynchronized)
    {
        if (isSynchronized)
        {
            return "Ambiguous";
        }

        if (score >= 70 && followsKnownValue)
        {
            return "Probable";
        }

        if (score >= 40)
        {
            return "Candidate";
        }

        return "Unknown";
    }

    private static (string Status, string Verdict) BuildOverallVerdict(IReadOnlyList<StructureComparisonResult> results)
    {
        if (results.Count == 0)
        {
            return ("Unknown", "Aucun candidat a comparer.");
        }

        var top = results[0];
        var second = results.Count > 1 ? results[1] : null;
        var margin = second is null ? top.Score : top.Score - second.Score;
        if (top.Status == "Probable" && margin >= 10)
        {
            return ("Probable", $"Candidat structurel probable: {top.AddressHex} score {top.Score:0.00}.");
        }

        if (top.Status is "Probable" or "Candidate" && margin < 10)
        {
            return ("Ambiguous", $"Meilleurs candidats trop proches: {top.AddressHex} marge {margin:0.00}.");
        }

        return (top.Status, $"Meilleur candidat: {top.AddressHex} statut {top.Status} score {top.Score:0.00}.");
    }

    private static string BuildComparisonSignature(IReadOnlyList<StructureFieldComparison> fields)
    {
        var parts = fields
            .Where(field => field.Status == "FollowsKnownValues")
            .Select(field => $"{field.RelativeOffsetHex}:{field.MatchKnownValueCount}:{field.DistinctValueCount}")
            .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join("|", parts);
    }

    private static void ValidateCapture(LuaMemoryCaptureEnvelope capture, string source)
    {
        if (string.IsNullOrWhiteSpace(capture.FeatureId))
        {
            throw new InvalidDataException($"Capture Lua incomplete dans {source}: featureId absent.");
        }

        if (capture.KnownValues is null)
        {
            throw new InvalidDataException($"Capture Lua incomplete dans {source}: knownValues absent.");
        }

        if (capture.Candidates is null || capture.Candidates.Count == 0)
        {
            throw new InvalidDataException($"Capture Lua incomplete dans {source}: aucun candidat capture.");
        }

        foreach (var candidate in capture.Candidates)
        {
            if (!TryResolveAddress(candidate.Address, candidate.AddressHex, out _))
            {
                throw new InvalidDataException($"Capture Lua invalide dans {source}: adresse candidat absente ou invalide.");
            }

            if (candidate.ContextByteCount <= 0 || string.IsNullOrWhiteSpace(candidate.ContextBytesHex))
            {
                throw new InvalidDataException($"Capture Lua invalide dans {source}: fenetre memoire vide pour {candidate.CandidateId ?? candidate.AddressHex ?? "unknown"}.");
            }
        }
    }

    private static bool TryResolveAddress(ulong numericAddress, string? hexAddress, out ulong address)
    {
        if (numericAddress != 0)
        {
            address = numericAddress;
            return true;
        }

        if (string.IsNullOrWhiteSpace(hexAddress))
        {
            address = 0;
            return false;
        }

        var normalized = hexAddress.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return ulong.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address)
            && address != 0;
    }

    private static string FormatRelativeOffset(int offset)
    {
        if (offset < 0)
        {
            return $"-0x{Math.Abs((long)offset):X}";
        }

        return $"0x{offset:X}";
    }

    private static string BuildScenarioKey(LuaMemoryCaptureEnvelope capture)
    {
        var scenarioId = string.IsNullOrWhiteSpace(capture.Scenario?.ScenarioId) ? "scenario-unknown" : capture.Scenario!.ScenarioId;
        var stepId = string.IsNullOrWhiteSpace(capture.Scenario?.StepId) ? "step-unknown" : capture.Scenario!.StepId;
        return $"{scenarioId}:{stepId}:{capture.KnownValues?.UiTreasury.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
    }

    private static void Add(ref double score, double delta, List<string> evidence, string reason)
    {
        score += delta;
        evidence.Add(reason);
    }

    private sealed record FieldObservation(LuaMemoryCaptureEnvelope Capture, CapturedInt32Field Field);
}
