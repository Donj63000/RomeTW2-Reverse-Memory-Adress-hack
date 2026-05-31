using Rome2Explorer.Domain;

namespace Rome2Explorer.Features;

public static class TreasuryScanGuidance
{
    public const string RawStatus = "Brut";
    public const string PlausibleStatus = "Plausible";
    public const string VeryLikelyStatus = "Tres probable";
    public const string RevalidateStatus = "A revalider";

    public static TreasuryGuidanceViewModel Build(KnownValueScanSession? session)
    {
        if (session is null)
        {
            return new TreasuryGuidanceViewModel(
                CurrentStep: "Aucun scan lance",
                Action: "Attache Rome2, ouvre la campagne, lis l'argent exact affiche, puis Start Scan.",
                Rationale: "Le scanner a besoin d'une premiere valeur connue pour creer une liste de candidats memoire.",
                ExpectedResult: "Une premiere liste d'adresses possibles apparait. Si plusieurs candidats apparaissent, c'est normal.");
        }

        var candidateCount = session.Candidates.Count;
        var scannedValueCount = session.ValueHistory.Count;
        var hasRefine = scannedValueCount > 1;
        var currentValue = session.ValueHistory.LastOrDefault();
        var valuePath = string.Join(" -> ", session.ValueHistory);

        if (candidateCount == 0)
        {
            return new TreasuryGuidanceViewModel(
                CurrentStep: hasRefine ? "Refine sans resultat" : "Scan initial sans resultat",
                Action: hasRefine
                    ? "La valeur entree ne correspond pas, recommence depuis Start Scan avec l'argent actuellement affiche en jeu."
                    : "Verifie l'argent exact affiche en campagne, puis relance Start Scan.",
                Rationale: hasRefine
                    ? "Tous les candidats precedents ont ete elimines : la nouvelle valeur saisie n'a pas ete retrouvee aux memes adresses."
                    : "Aucune adresse Int32 alignee n'a ete trouvee avec cette valeur dans les regions memoire retenues.",
                ExpectedResult: "Un nouveau scan propre doit recreer une liste de candidats coherente.");
        }

        if (!hasRefine && candidateCount > 1)
        {
            return new TreasuryGuidanceViewModel(
                CurrentStep: $"Scan initial termine - {candidateCount} candidats",
                Action: $"Impossible de savoir lequel est bon. Retourne dans Rome2, change l'argent, note la nouvelle valeur exacte, remplace {currentValue} par cette nouvelle valeur, puis clique Refine Scan.",
                Rationale: "Plusieurs zones memoire peuvent contenir la meme valeur : affichage UI, copies temporaires, caches ou vraie donnee logique.",
                ExpectedResult: "Apres Refine, seules les adresses qui suivent la nouvelle valeur doivent rester.");
        }

        if (!hasRefine && candidateCount == 1)
        {
            return new TreasuryGuidanceViewModel(
                CurrentStep: "Candidat brut unique",
                Action: $"Je change quand meme l'argent en jeu, je remplace {currentValue} par la nouvelle valeur affichee, puis je clique Refine Scan.",
                Rationale: "Une adresse unique au premier scan reste une hypothese : elle n'a pas encore prouve qu'elle suit la valeur reelle.",
                ExpectedResult: "Si l'adresse suit la nouvelle valeur, elle deviendra un candidat plausible.");
        }

        if (candidateCount > 1)
        {
            return new TreasuryGuidanceViewModel(
                CurrentStep: $"Refine termine - {candidateCount} candidats synchronises",
                Action: $"Ces adresses sont synchronisees : le scan exact ne suffit plus. Clique Departager candidats, puis utilise Analyser pointeurs sur le meilleur candidat.",
                Rationale: $"Ces {candidateCount} adresses suivent encore la suite {valuePath}. Elles peuvent etre la vraie valeur, une copie UI, un cache ou une structure miroir.",
                ExpectedResult: "Si le departage reste ambigu, l'analyse pointeur doit chercher une base structure + offset. Une adresse brute n'est pas portable apres reload/redemarrage.");
        }

        return new TreasuryGuidanceViewModel(
            CurrentStep: "Candidat tres probable",
                Action: $"Il reste une seule adresse apres la suite {valuePath}. Fais encore une petite validation pour valider definitivement, puis lance Analyser pointeurs pour chercher une base structure et un offset treasury reutilisables.",
            Rationale: "Cette adresse a deja suivi au moins une variation de l'argent apres le scan initial.",
            ExpectedResult: "Si l'ecriture marche mais n'est pas stable, l'analyse pointeurs indique quoi valider ensuite avec reload/redemarrage.");
    }

    public static string GetCandidateStatus(KnownValueScanSession session, Candidate candidate)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidate);

        // Je ne marque jamais une adresse comme fiable sur un scan initial : elle peut etre une copie UI,
        // un cache ou une valeur temporaire. La confiance monte seulement quand l'adresse suit un refine.
        if (candidate.Confidence < 0.15)
        {
            return RevalidateStatus;
        }

        if (candidate.Confidence <= 0.15 || session.ValueHistory.Count <= 1)
        {
            return RawStatus;
        }

        if (session.ValueHistory.Count >= 3 || candidate.Confidence >= 0.65)
        {
            return VeryLikelyStatus;
        }

        return PlausibleStatus;
    }
}
