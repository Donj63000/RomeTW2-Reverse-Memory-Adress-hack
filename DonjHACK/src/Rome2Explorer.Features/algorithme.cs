using Rome2Explorer.Domain;

namespace Rome2Explorer.Features;

public sealed class Algorithme
{
    private const double BestScoreEpsilon = 0.01;
    private const double FavoriteMarginThreshold = 5.0;
    private const double MinimumFavoriteScore = 55.0;

    public IReadOnlyList<AlgorithmeCandidateScore> ScoreTreasuryCandidates(
        KnownValueScanSession session,
        IReadOnlyList<Candidate> candidates,
        MemoryMapSnapshot? snapshot,
        IReadOnlyList<SavedMemoryFinding> savedFindings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(savedFindings);

        if (candidates.Count == 0)
        {
            return Array.Empty<AlgorithmeCandidateScore>();
        }

        var regionDensity = candidates
            .Where(candidate => candidate.Region is not null)
            .GroupBy(candidate => candidate.Region!.BaseAddress)
            .ToDictionary(group => group.Key, group => group.Count());

        var rawScores = candidates
            .Select(candidate => ScoreCandidate(session, candidate, snapshot, savedFindings, candidates.Count, regionDensity))
            .OrderByDescending(score => score.Score)
            .ThenBy(score => score.Address)
            .ThenBy(score => score.CandidateId, StringComparer.Ordinal)
            .ToArray();

        var bestScore = rawScores[0].Score;
        var distinctScores = rawScores
            .Select(score => score.Score)
            .Distinct()
            .OrderByDescending(score => score)
            .ToArray();
        var secondDistinctScore = distinctScores.Length > 1 ? distinctScores[1] : (double?)null;
        var margin = secondDistinctScore is null ? bestScore : bestScore - secondDistinctScore.Value;
        var bestGroupCount = rawScores.Count(result => Math.Abs(result.Score - bestScore) < BestScoreEpsilon);
        var hasUsefulRefine = session.ValueHistory.Count > 1;

        return rawScores
            .Select((score, index) =>
            {
                var isBestGroup = Math.Abs(score.Score - bestScore) < BestScoreEpsilon;
                var isUniqueValidatedFavorite =
                    isBestGroup
                    && hasUsefulRefine
                    && bestGroupCount == 1
                    && margin >= FavoriteMarginThreshold
                    && score.Score >= MinimumFavoriteScore;
                var isAmbiguousTop = isBestGroup && !isUniqueValidatedFavorite;
                var verdict = BuildVerdict(score.Score, isBestGroup, hasUsefulRefine, margin, bestGroupCount);
                var reasons = BuildFinalReasons(score.Reasons, isBestGroup, isUniqueValidatedFavorite, hasUsefulRefine, margin, bestGroupCount, score.Score);

                return score with
                {
                    Rank = index + 1,
                    IsAlgorithmFavorite = isUniqueValidatedFavorite,
                    IsAlgorithmAmbiguousTop = isAmbiguousTop,
                    MarginToNextBest = Math.Round(margin, 2),
                    Verdict = verdict,
                    Reasons = reasons
                };
            })
            .OrderBy(score => score.Address)
            .ThenBy(score => score.CandidateId, StringComparer.Ordinal)
            .ToArray();
    }

    private static AlgorithmeCandidateScore ScoreCandidate(
        KnownValueScanSession session,
        Candidate candidate,
        MemoryMapSnapshot? snapshot,
        IReadOnlyList<SavedMemoryFinding> savedFindings,
        int candidateCount,
        IReadOnlyDictionary<ulong, int> regionDensity)
    {
        var reasons = new List<string>();
        var score = 0.0;

        Add(ref score, candidate.Confidence * 45, reasons, $"confiance scan {candidate.Confidence:0.00}");

        var refineCount = Math.Max(0, session.ValueHistory.Count - 1);
        Add(ref score, Math.Min(24, refineCount * 12), reasons, $"{refineCount} refine(s)");

        var refineEvidenceCount = candidate.Evidence.Count(evidence => evidence.Contains("Refine OK", StringComparison.OrdinalIgnoreCase));
        Add(ref score, Math.Min(18, refineEvidenceCount * 9), reasons, $"{refineEvidenceCount} preuve(s) refine");

        Add(ref score, CandidateCountBonus(candidateCount), reasons, $"{candidateCount} candidat(s) restant(s)");

        if (candidate.Address % 4 == 0)
        {
            Add(ref score, 7, reasons, "adresse alignee Int32");
        }
        else
        {
            Add(ref score, -18, reasons, "adresse non alignee");
        }

        if (candidate.Type.Equals("Int32", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 5, reasons, "type Int32 attendu");
        }

        ScoreRegion(candidate, regionDensity, ref score, reasons);
        ScoreModule(snapshot, candidate, ref score, reasons);
        ScoreSavedFinding(savedFindings, session, candidate, snapshot, ref score, reasons);

        if (candidate.Warnings.Count == 0)
        {
            Add(ref score, 4, reasons, "aucun warning candidat");
        }
        else
        {
            Add(ref score, -Math.Min(12, candidate.Warnings.Count * 4), reasons, "warnings candidat");
        }

        var clamped = Math.Clamp(score, 0, 100);
        return new AlgorithmeCandidateScore(
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            Score: Math.Round(clamped, 2),
            Rank: 0,
            IsAlgorithmFavorite: false,
            IsAlgorithmAmbiguousTop: false,
            MarginToNextBest: 0,
            Verdict: string.Empty,
            Reasons: reasons.ToArray());
    }

    private static void ScoreRegion(
        Candidate candidate,
        IReadOnlyDictionary<ulong, int> regionDensity,
        ref double score,
        List<string> reasons)
    {
        var region = candidate.Region;
        if (region is null)
        {
            Add(ref score, -8, reasons, "region inconnue");
            return;
        }

        if (region.State.Equals("MEM_COMMIT", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 5, reasons, "region commit");
        }

        if (region.IsReadable && region.IsWritable && !region.IsExecutable)
        {
            Add(ref score, 12, reasons, "region data read/write non executable");
        }
        else
        {
            Add(ref score, -20, reasons, "protection region moins fiable");
        }

        if (region.Protection.Contains("GUARD", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, -15, reasons, "page guard");
        }

        if (region.Type.Equals("MEM_PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 5, reasons, "memoire privee gameplay probable");
        }
        else if (region.Type.Equals("MEM_IMAGE", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, -4, reasons, "region image moins probable pour valeur dynamique");
        }

        if (regionDensity.TryGetValue(region.BaseAddress, out var countInRegion))
        {
            if (countInRegion == 1)
            {
                Add(ref score, 6, reasons, "candidat isole dans sa region");
            }
            else if (countInRegion >= 4)
            {
                Add(ref score, -5, reasons, "plusieurs candidats dans la meme region");
            }
        }
    }

    private static void ScoreModule(
        MemoryMapSnapshot? snapshot,
        Candidate candidate,
        ref double score,
        List<string> reasons)
    {
        var module = FindContainingModule(snapshot, candidate.Address);

        if (module is null)
        {
            Add(ref score, 0, reasons, "pas de module fixe connu");
            return;
        }

        Add(ref score, 8, reasons, $"offset module {module.Name}+0x{candidate.Address - module.BaseAddress:X}");
    }

    private static void ScoreSavedFinding(
        IReadOnlyList<SavedMemoryFinding> savedFindings,
        KnownValueScanSession session,
        Candidate candidate,
        MemoryMapSnapshot? snapshot,
        ref double score,
        List<string> reasons)
    {
        var matchingSavedFindings = savedFindings
            .Where(finding =>
                finding.Address == candidate.Address
                && string.Equals(finding.FeatureId, session.FeatureId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingSavedFindings.Length == 0)
        {
            return;
        }

        var currentArchitecture = snapshot?.Process.Architecture.ToString();
        var hasCompatibleArchitecture = matchingSavedFindings.Any(finding =>
            string.IsNullOrWhiteSpace(finding.Architecture)
            || string.IsNullOrWhiteSpace(currentArchitecture)
            || string.Equals(finding.Architecture, currentArchitecture, StringComparison.OrdinalIgnoreCase));

        if (!hasCompatibleArchitecture)
        {
            Add(ref score, -6, reasons, "known finding meme adresse mais architecture differente");
            return;
        }

        var currentModule = FindContainingModule(snapshot, candidate.Address);
        var hasCompatibleModule = matchingSavedFindings.Any(finding =>
            string.IsNullOrWhiteSpace(finding.ModuleName)
            || (currentModule is not null && string.Equals(finding.ModuleName, currentModule.Name, StringComparison.OrdinalIgnoreCase)));

        if (!hasCompatibleModule)
        {
            Add(ref score, -4, reasons, "known finding meme adresse mais module different");
            return;
        }

        var currentProcessPath = snapshot?.Process.Path;
        var hasSameProcessPath = matchingSavedFindings.Any(finding =>
            !string.IsNullOrWhiteSpace(finding.ProcessPath)
            && !string.IsNullOrWhiteSpace(currentProcessPath)
            && string.Equals(finding.ProcessPath, currentProcessPath, StringComparison.OrdinalIgnoreCase));

        // Je garde un bonus plus fort seulement quand l'ancien finding vient du meme executable.
        // Une adresse absolue peut changer apres reload, donc sans chemin identique je la traite comme indice a revalider.
        Add(
            ref score,
            hasSameProcessPath ? 15 : 8,
            reasons,
            hasSameProcessPath
                ? "known finding compatible meme executable"
                : "known finding compatible a revalider");
    }

    private static ModuleInfo? FindContainingModule(MemoryMapSnapshot? snapshot, ulong address)
    {
        return snapshot?.Modules.FirstOrDefault(module => IsAddressInsideModule(address, module));
    }

    private static bool IsAddressInsideModule(ulong address, ModuleInfo module)
    {
        if (module.Size <= 0 || address < module.BaseAddress)
        {
            return false;
        }

        // Je compare avec une soustraction pour eviter un overflow sur base + size.
        return address - module.BaseAddress < (ulong)module.Size;
    }

    private static double CandidateCountBonus(int candidateCount)
    {
        return candidateCount switch
        {
            <= 1 => 18,
            <= 3 => 12,
            <= 10 => 6,
            <= 100 => 2,
            _ => 0
        };
    }

    private static string BuildVerdict(double score, bool isBestGroup, bool hasUsefulRefine, double margin, int bestGroupCount)
    {
        if (!isBestGroup)
        {
            return "Possible";
        }

        if (!hasUsefulRefine)
        {
            return "Scan initial - refine obligatoire";
        }

        if (bestGroupCount > 1)
        {
            return "Ex aequo - refine encore";
        }

        if (margin < FavoriteMarginThreshold || score < MinimumFavoriteScore)
        {
            return "Favori fragile - refine encore";
        }

        if (score >= 70)
        {
            return "Favori fort";
        }

        return "Favori a confirmer";
    }

    private static IReadOnlyList<string> BuildFinalReasons(
        IReadOnlyList<string> baseReasons,
        bool isBestGroup,
        bool isUniqueValidatedFavorite,
        bool hasUsefulRefine,
        double margin,
        int bestGroupCount,
        double score)
    {
        var reasons = baseReasons.ToList();

        if (!isBestGroup)
        {
            return reasons.ToArray();
        }

        if (isUniqueValidatedFavorite)
        {
            reasons.Add($"+0 favori unique apres refine, marge {margin:0.##}");
            return reasons.ToArray();
        }

        if (!hasUsefulRefine)
        {
            reasons.Add("0 favori bloque tant qu'aucun refine n'a valide l'adresse");
        }
        else if (bestGroupCount > 1)
        {
            reasons.Add($"0 meilleur score partage par {bestGroupCount} candidats");
        }
        else if (margin < FavoriteMarginThreshold)
        {
            reasons.Add($"0 marge insuffisante {margin:0.##}");
        }
        else if (score < MinimumFavoriteScore)
        {
            reasons.Add($"0 score trop faible {score:0.##}");
        }

        return reasons.ToArray();
    }

    private static void Add(ref double score, double delta, List<string> reasons, string reason)
    {
        score += delta;
        reasons.Add($"{delta:+0.##;-0.##;0} {reason}");
    }
}

public sealed record AlgorithmeCandidateScore(
    string CandidateId,
    ulong Address,
    double Score,
    int Rank,
    bool IsAlgorithmFavorite,
    bool IsAlgorithmAmbiguousTop,
    double MarginToNextBest,
    string Verdict,
    IReadOnlyList<string> Reasons);
