using System.Globalization;
using Rome2Explorer.Domain;

namespace Rome2Explorer.Features;

public sealed class TreasuryRestartValidationService
{
    private const double AmbiguousScoreGap = 5.0;
    private const double UsefulMatchScore = 35.0;
    private readonly Func<DateTimeOffset> _clock;

    public TreasuryRestartValidationService()
        : this(() => DateTimeOffset.Now)
    {
    }

    public TreasuryRestartValidationService(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public TreasuryRestartValidationResult Validate(
        KnownValueScanSession session,
        Candidate selectedCandidate,
        MemoryMapSnapshot snapshot,
        IReadOnlyList<TreasuryValidationReference> previousReferences,
        TreasuryPointerAnalysisResult? currentPointerAnalysis = null,
        string scenario = "manual-reload-validation")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selectedCandidate);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previousReferences);

        if (!string.Equals(session.FeatureId, KnownValueScanner.TreasuryFeatureId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Validation treasury non supportee pour feature {session.FeatureId}.");
        }

        if (!session.Candidates.Any(candidate => string.Equals(candidate.CandidateId, selectedCandidate.CandidateId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Le candidat selectionne n'appartient pas a la session treasury active.");
        }

        var usableReferences = previousReferences
            .Where(reference => string.Equals(reference.FeatureId, session.FeatureId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var currentReference = BuildReferenceFromCurrentSession(session, selectedCandidate, snapshot, currentPointerAnalysis);
        var rankedCandidates = session.Candidates
            .Select(candidate => ScoreCandidate(session, candidate, selectedCandidate, snapshot, usableReferences, currentPointerAnalysis))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Address)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
        var overall = BuildOverallStatus(rankedCandidates, usableReferences);
        var evidence = new List<string>
        {
            $"References chargees: {usableReferences.Length}.",
            $"Candidat selectionne: 0x{selectedCandidate.Address:X}.",
            "Validation en lecture seule : aucune ecriture memoire n'est effectuee."
        };
        var warnings = new List<string>();

        if (usableReferences.Length == 0)
        {
            warnings.Add("Aucune preuve precedente chargee : cet export sert de point de depart session A.");
        }

        if (currentPointerAnalysis is null || !string.Equals(currentPointerAnalysis.CandidateId, selectedCandidate.CandidateId, StringComparison.Ordinal))
        {
            warnings.Add("Aucune analyse pointeur active pour le candidat selectionne : la validation reste limitee aux adresses/regions.");
        }

        if (overall.Status == TreasuryRestartValidationStatuses.Ambiguous)
        {
            warnings.Add("Plusieurs candidats restent equivalents : refaire un refine, une analyse pointeur ou une capture CE/Lua.");
        }

        return new TreasuryRestartValidationResult(
            CreatedAt: _clock(),
            FeatureId: session.FeatureId,
            Scenario: scenario,
            OverallStatus: overall.Status,
            OverallVerdict: overall.Verdict,
            Process: snapshot.Process,
            MemorySummary: snapshot.Summary,
            CurrentReference: currentReference,
            ReferenceCount: usableReferences.Length,
            ReferencesUsed: usableReferences,
            RankedCandidates: rankedCandidates,
            Evidence: evidence.ToArray(),
            Warnings: warnings.ToArray());
    }

    public TreasuryValidationReference BuildReferenceFromCurrentSession(
        KnownValueScanSession session,
        Candidate selectedCandidate,
        MemoryMapSnapshot snapshot,
        TreasuryPointerAnalysisResult? currentPointerAnalysis)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selectedCandidate);
        ArgumentNullException.ThrowIfNull(snapshot);

        var pointer = IsPointerForCandidate(currentPointerAnalysis, selectedCandidate)
            ? currentPointerAnalysis
            : null;
        var best = pointer?.BestStructureBase;
        var region = selectedCandidate.Region;
        var evidence = selectedCandidate.Evidence
            .Concat(pointer?.Evidence ?? Array.Empty<string>())
            .Concat(new[] { "Reference creee depuis la session DonjHACK courante." })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warnings = selectedCandidate.Warnings
            .Concat(pointer?.Warnings ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new TreasuryValidationReference(
            CreatedAt: _clock(),
            SourceKind: "CurrentSession",
            SourcePath: "runtime",
            FeatureId: session.FeatureId,
            CandidateId: selectedCandidate.CandidateId,
            Address: selectedCandidate.Address,
            AddressHex: $"0x{selectedCandidate.Address:X}",
            Architecture: snapshot.Process.Architecture.ToString(),
            ProcessId: snapshot.Process.ProcessId,
            ProcessStartTime: snapshot.Process.StartTime,
            ProcessPath: snapshot.Process.Path,
            ValueHistory: session.ValueHistory.ToArray(),
            RegionBaseHex: region is null ? null : $"0x{region.BaseAddress:X}",
            RegionOffsetHex: region is null ? null : $"0x{selectedCandidate.Address - region.BaseAddress:X}",
            RegionProtection: region?.Protection,
            RegionType: region?.Type,
            PointerVerdict: pointer?.OverallVerdict,
            StructureBaseHex: best?.BaseAddressHex,
            TreasuryOffset: best?.TreasuryOffset,
            TreasuryOffsetHex: best?.TreasuryOffsetHex,
            PointerScore: best?.Score,
            PointerHitCount: pointer?.PointerHits.Count ?? 0,
            PointerChainCount: pointer?.PointerChains.Count ?? 0,
            WriteSucceeded: null,
            WrittenValue: null,
            Evidence: evidence,
            Warnings: warnings);
    }

    private TreasuryValidationCandidateResult ScoreCandidate(
        KnownValueScanSession session,
        Candidate candidate,
        Candidate selectedCandidate,
        MemoryMapSnapshot snapshot,
        IReadOnlyList<TreasuryValidationReference> references,
        TreasuryPointerAnalysisResult? currentPointerAnalysis)
    {
        var candidatePointer = IsPointerForCandidate(currentPointerAnalysis, candidate)
            ? currentPointerAnalysis
            : null;
        var candidateReference = BuildReferenceFromCurrentSession(session, candidate, snapshot, candidatePointer);
        var matches = references
            .Select(reference => ScoreReference(candidateReference, reference))
            .OrderByDescending(match => match.Score)
            .ToArray();
        var strongest = matches.FirstOrDefault();
        var evidence = new List<string>();
        var warnings = new List<string>();
        var score = 0.0;

        Add(ref score, Math.Min(12, candidate.Confidence * 12), evidence, $"confiance scan {candidate.Confidence:0.00}");

        if (candidatePointer is not null)
        {
            Add(ref score, 10, evidence, $"analyse pointeur courante {candidatePointer.OverallVerdict}");
            if (candidatePointer.BestStructureBase is not null)
            {
                Add(ref score, Math.Min(15, candidatePointer.BestStructureBase.Score * 0.15), evidence, $"base structure courante {candidatePointer.BestStructureBase.BaseAddressHex}+{candidatePointer.BestStructureBase.TreasuryOffsetHex}");
            }
        }
        else
        {
            warnings.Add("Pas d'analyse pointeur courante pour ce candidat.");
        }

        if (strongest is not null)
        {
            Add(ref score, strongest.Score, evidence, $"meilleure preuve precedente {strongest.Reference.SourceKind} score {strongest.Score:0.00}");
            evidence.AddRange(strongest.Evidence);
            warnings.AddRange(strongest.Warnings);
        }
        else if (references.Count > 0)
        {
            warnings.Add("Aucune preuve precedente ne correspond a ce candidat.");
        }

        var clamped = Math.Round(Math.Clamp(score - Math.Min(18, warnings.Count * 2), 0, 100), 2);
        var status = BuildCandidateStatus(candidateReference, strongest, references.Count, clamped);
        var region = candidate.Region;
        var best = candidatePointer?.BestStructureBase;

        return new TreasuryValidationCandidateResult(
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            AddressHex: $"0x{candidate.Address:X}",
            IsSelected: string.Equals(candidate.CandidateId, selectedCandidate.CandidateId, StringComparison.Ordinal),
            Status: status,
            Score: clamped,
            RegionBaseHex: region is null ? "unknown" : $"0x{region.BaseAddress:X}",
            RegionOffsetHex: region is null ? "unknown" : $"0x{candidate.Address - region.BaseAddress:X}",
            RegionProtection: region?.Protection ?? "unknown",
            RegionType: region?.Type ?? "unknown",
            PointerStatus: candidatePointer?.OverallVerdict ?? "Non analyse",
            StructureBaseHex: best?.BaseAddressHex,
            TreasuryOffsetHex: best?.TreasuryOffsetHex,
            MatchedReferenceCount: matches.Count(match => match.Score >= UsefulMatchScore),
            StrongestReferenceKind: strongest?.Reference.SourceKind,
            StrongestReferencePath: strongest?.Reference.SourcePath,
            Evidence: evidence.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static ReferenceMatchScore ScoreReference(TreasuryValidationReference current, TreasuryValidationReference reference)
    {
        var evidence = new List<string>();
        var warnings = new List<string>();
        var score = 0.0;

        if (string.Equals(current.Architecture, reference.Architecture, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 6, evidence, "architecture identique");
        }

        if (!string.IsNullOrWhiteSpace(current.ProcessPath)
            && string.Equals(current.ProcessPath, reference.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 6, evidence, "meme executable Rome2");
        }

        if (current.Address.HasValue && reference.Address.HasValue && current.Address.Value == reference.Address.Value)
        {
            Add(ref score, IsDifferentProcessRun(current, reference) ? 8 : 18, evidence, "adresse absolue identique");
            if (!IsDifferentProcessRun(current, reference))
            {
                warnings.Add("Adresse identique dans le meme run : preuve utile mais pas portable.");
            }
        }
        else if (reference.Address.HasValue)
        {
            evidence.Add($"adresse differente de la reference {reference.AddressHex ?? reference.Address.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(current.RegionOffsetHex)
            && string.Equals(current.RegionOffsetHex, reference.RegionOffsetHex, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 18, evidence, $"offset region conserve {current.RegionOffsetHex}");
        }

        if (!string.IsNullOrWhiteSpace(current.RegionProtection)
            && string.Equals(current.RegionProtection, reference.RegionProtection, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 5, evidence, "protection region identique");
        }

        if (!string.IsNullOrWhiteSpace(current.RegionType)
            && string.Equals(current.RegionType, reference.RegionType, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 5, evidence, "type region identique");
        }

        if (current.TreasuryOffset.HasValue
            && reference.TreasuryOffset.HasValue
            && current.TreasuryOffset.Value == reference.TreasuryOffset.Value)
        {
            Add(ref score, 35, evidence, $"offset treasury conserve {current.TreasuryOffsetHex}");
        }

        if (!string.IsNullOrWhiteSpace(current.PointerVerdict)
            && string.Equals(current.PointerVerdict, reference.PointerVerdict, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 8, evidence, $"verdict pointeur conserve {current.PointerVerdict}");
        }

        if (reference.WriteSucceeded == true)
        {
            Add(ref score, 5, evidence, "reference issue d'une ecriture reussie");
        }

        if (current.ValueHistory.Count > 0
            && reference.WrittenValue.HasValue
            && current.ValueHistory[^1] == reference.WrittenValue.Value)
        {
            Add(ref score, 5, evidence, "valeur courante egale a la valeur ecrite precedente");
        }

        return new ReferenceMatchScore(reference, Math.Round(Math.Clamp(score, 0, 100), 2), evidence, warnings);
    }

    private static string BuildCandidateStatus(
        TreasuryValidationReference current,
        ReferenceMatchScore? strongest,
        int referenceCount,
        double score)
    {
        if (referenceCount == 0)
        {
            return TreasuryRestartValidationStatuses.RawOnly;
        }

        if (strongest is null || score < 20)
        {
            return TreasuryRestartValidationStatuses.Broken;
        }

        var hasStructuralMatch = strongest.Evidence.Any(item =>
            item.Contains("offset treasury conserve", StringComparison.OrdinalIgnoreCase)
            || item.Contains("offset region conserve", StringComparison.OrdinalIgnoreCase));
        var hasAddressMatch = strongest.Evidence.Any(item =>
            item.Contains("adresse absolue identique", StringComparison.OrdinalIgnoreCase));
        var differentRun = IsDifferentProcessRun(current, strongest.Reference);

        if (differentRun && hasStructuralMatch && score >= 70)
        {
            return TreasuryRestartValidationStatuses.ValidatedAfterRestart;
        }

        if (hasStructuralMatch && score >= 45)
        {
            return TreasuryRestartValidationStatuses.ProbableAfterReload;
        }

        if (hasAddressMatch)
        {
            return TreasuryRestartValidationStatuses.SameSessionOnly;
        }

        if (!hasStructuralMatch)
        {
            return TreasuryRestartValidationStatuses.Broken;
        }

        return score >= 35
            ? TreasuryRestartValidationStatuses.RawOnly
            : TreasuryRestartValidationStatuses.Broken;
    }

    private static (string Status, string Verdict) BuildOverallStatus(
        IReadOnlyList<TreasuryValidationCandidateResult> candidates,
        IReadOnlyList<TreasuryValidationReference> references)
    {
        if (candidates.Count == 0)
        {
            return (TreasuryRestartValidationStatuses.Broken, "Aucun candidat treasury actif a valider.");
        }

        if (references.Count == 0)
        {
            return (TreasuryRestartValidationStatuses.RawOnly, "Session A exportee. Reload/redemarre Rome2, refais le scan, puis compare.");
        }

        var top = candidates[0];
        var second = candidates.Count > 1 ? candidates[1] : null;
        if (second is not null
            && top.Score >= UsefulMatchScore
            && Math.Abs(top.Score - second.Score) < AmbiguousScoreGap)
        {
            return (TreasuryRestartValidationStatuses.Ambiguous, $"Deux candidats restent equivalents apres comparaison: {top.AddressHex} et {second.AddressHex}.");
        }

        return top.Status switch
        {
            TreasuryRestartValidationStatuses.ValidatedAfterRestart => (top.Status, $"Validation restart forte: {top.AddressHex} retrouve une structure/offset compatible."),
            TreasuryRestartValidationStatuses.ProbableAfterReload => (top.Status, $"Validation reload probable: {top.AddressHex} conserve des indices structurels, restart complet encore conseille."),
            TreasuryRestartValidationStatuses.SameSessionOnly => (top.Status, $"Adresse {top.AddressHex} encore coherente dans la session, mais pas portable sans reload/restart."),
            TreasuryRestartValidationStatuses.Broken => (top.Status, "Les preuves precedentes ne retrouvent pas la cible courante. Il faut refaire pointer analysis ou CE/Lua."),
            _ => (top.Status, $"Meilleur candidat {top.AddressHex} statut {top.Status} score {top.Score:0.00}.")
        };
    }

    private static bool IsPointerForCandidate(TreasuryPointerAnalysisResult? result, Candidate candidate)
    {
        return result is not null
            && string.Equals(result.CandidateId, candidate.CandidateId, StringComparison.Ordinal);
    }

    private static bool IsDifferentProcessRun(TreasuryValidationReference current, TreasuryValidationReference reference)
    {
        if (current.ProcessId.HasValue && reference.ProcessId.HasValue && current.ProcessId.Value != reference.ProcessId.Value)
        {
            return true;
        }

        if (current.ProcessStartTime.HasValue
            && reference.ProcessStartTime.HasValue
            && current.ProcessStartTime.Value != reference.ProcessStartTime.Value)
        {
            return true;
        }

        return false;
    }

    private static void Add(ref double score, double delta, List<string> evidence, string reason)
    {
        score += delta;
        evidence.Add($"{delta:+0.##;-0.##;0} {reason}");
    }

    private sealed record ReferenceMatchScore(
        TreasuryValidationReference Reference,
        double Score,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> Warnings);
}
