using Rome2Explorer.Domain;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Features;

public sealed class TreasuryMoneyWriter
{
    public static readonly TimeSpan DefaultStabilityDelay = TimeSpan.FromMilliseconds(350);

    public async Task<TreasuryWriteResult> WriteSelectedCandidateAsync(
        KnownValueScanSession? session,
        Candidate? candidate,
        IProcessMemoryReader reader,
        IProcessMemoryWriter writer,
        int desiredValue,
        TimeSpan? stabilityDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        var createdAt = DateTimeOffset.Now;
        var delay = stabilityDelay ?? DefaultStabilityDelay;
        var evidence = new List<string>();
        var warnings = new List<string>();

        if (session is null)
        {
            return Refused(createdAt, desiredValue, "Aucune session treasury active. Je dois d'abord scanner et selectionner un candidat.", evidence, warnings);
        }

        if (candidate is null)
        {
            return Refused(createdAt, desiredValue, "Aucun candidat selectionne. Je refuse d'ecrire sans adresse choisie explicitement.", evidence, warnings);
        }

        if (!string.Equals(session.FeatureId, KnownValueScanner.TreasuryFeatureId, StringComparison.Ordinal))
        {
            return Refused(createdAt, desiredValue, "La session active ne correspond pas a l'argent treasury.", evidence, warnings, session, candidate);
        }

        if (!string.Equals(candidate.Type, "Int32", StringComparison.OrdinalIgnoreCase))
        {
            return Refused(createdAt, desiredValue, $"Le candidat est de type {candidate.Type}, pas Int32.", evidence, warnings, session, candidate);
        }

        if (!session.Candidates.Any(item => string.Equals(item.CandidateId, candidate.CandidateId, StringComparison.Ordinal)))
        {
            return Refused(createdAt, desiredValue, "Le candidat selectionne n'appartient pas a la session treasury courante.", evidence, warnings, session, candidate);
        }

        if (session.CurrentValue is not int expectedCurrentValue)
        {
            return Refused(createdAt, desiredValue, "La session ne contient pas de valeur actuelle fiable.", evidence, warnings, session, candidate);
        }

        if (desiredValue == expectedCurrentValue)
        {
            return Refused(createdAt, desiredValue, "La nouvelle valeur est identique a la valeur actuelle : aucune modification utile a faire.", evidence, warnings, session, candidate);
        }

        if (!IsWritableCandidateRegion(candidate, out var regionWarning))
        {
            warnings.Add(regionWarning);
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Refused,
                message: regionWarning,
                valueBefore: null,
                valueAfterWrite: null,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        if (session.Candidates.Count > 1)
        {
            warnings.Add($"Plusieurs candidats restent ({session.Candidates.Count}). J'ecris uniquement l'adresse selectionnee, jamais toute la liste.");
        }

        // Je relis l'adresse juste avant d'ecrire : si le jeu a deja change la valeur,
        // le candidat ou l'etat UI n'est plus synchronise, donc l'ecriture serait dangereuse.
        if (!reader.TryReadInt32(candidate.Address, out var valueBefore, out var readBeforeWarning))
        {
            warnings.Add(readBeforeWarning ?? $"Lecture avant ecriture impossible a 0x{candidate.Address:X}.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Refused,
                message: "Lecture avant ecriture impossible. Je n'ecris pas sur une adresse que je ne peux pas verifier.",
                valueBefore: null,
                valueAfterWrite: null,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        evidence.Add($"Avant ecriture : 0x{candidate.Address:X} vaut {valueBefore}.");
        if (valueBefore != expectedCurrentValue)
        {
            warnings.Add($"Valeur actuelle inattendue : lu {valueBefore}, attendu {expectedCurrentValue}.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Refused,
                message: "La valeur memoire ne correspond plus a la derniere valeur scannee. Refais un refine avant d'ecrire.",
                valueBefore,
                valueAfterWrite: null,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        if (!writer.TryEnableWriteAccess(out var accessWarning))
        {
            warnings.Add(accessWarning ?? "Droits d'ecriture indisponibles.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Error,
                message: "Impossible d'obtenir les droits d'ecriture sur Rome2.exe.",
                valueBefore,
                valueAfterWrite: null,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        // Ici l'ecriture reelle est volontairement minuscule : 4 octets Int32 little-endian.
        // Aucun hook, aucune injection, aucun patch code ne sont utilises.
        if (!writer.TryWriteInt32(candidate.Address, desiredValue, out var writeWarning))
        {
            warnings.Add(writeWarning ?? $"Ecriture impossible a 0x{candidate.Address:X}.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Error,
                message: "WriteProcessMemory a echoue.",
                valueBefore,
                valueAfterWrite: null,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        evidence.Add($"WriteProcessMemory OK : 0x{candidate.Address:X} <- {desiredValue}.");

        if (!reader.TryReadInt32(candidate.Address, out var valueAfterWrite, out var readAfterWarning))
        {
            warnings.Add(readAfterWarning ?? $"Relecture immediate impossible a 0x{candidate.Address:X}.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Error,
                message: "Ecriture faite, mais verification immediate impossible.",
                valueBefore,
                valueAfterWrite: null,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        evidence.Add($"Relecture immediate : 0x{candidate.Address:X} vaut {valueAfterWrite}.");
        if (valueAfterWrite != desiredValue)
        {
            warnings.Add($"La relecture immediate vaut {valueAfterWrite}, pas {desiredValue}.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Overwritten,
                message: "L'adresse n'a pas garde la valeur ecrite. C'est probablement une copie ou le jeu a refuse la valeur.",
                valueBefore,
                valueAfterWrite,
                valueAfterStabilityDelay: valueAfterWrite,
                evidence,
                warnings);
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        if (!reader.TryReadInt32(candidate.Address, out var valueAfterStabilityDelay, out var stabilityWarning))
        {
            warnings.Add(stabilityWarning ?? $"Relecture de stabilite impossible a 0x{candidate.Address:X}.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Error,
                message: "Ecriture faite, mais verification de stabilite impossible.",
                valueBefore,
                valueAfterWrite,
                valueAfterStabilityDelay: null,
                evidence,
                warnings);
        }

        evidence.Add($"Relecture stabilite : 0x{candidate.Address:X} vaut {valueAfterStabilityDelay}.");
        if (valueAfterStabilityDelay != desiredValue)
        {
            warnings.Add($"La valeur a ete remplacee par {valueAfterStabilityDelay} apres {delay.TotalMilliseconds:0} ms.");
            return BuildResult(
                createdAt,
                session,
                candidate,
                desiredValue,
                success: false,
                status: TreasuryWriteStatuses.Overwritten,
                message: "Le jeu a ecrase la modification. Il faut tester l'autre candidat ou passer au pointer scan.",
                valueBefore,
                valueAfterWrite,
                valueAfterStabilityDelay,
                evidence,
                warnings);
        }

        return BuildResult(
            createdAt,
            session,
            candidate,
            desiredValue,
            success: true,
            status: TreasuryWriteStatuses.Success,
            message: "Ecriture verifiee : la valeur demandee est encore presente apres controle.",
            valueBefore,
            valueAfterWrite,
            valueAfterStabilityDelay,
            evidence,
            warnings);
    }

    private static bool IsWritableCandidateRegion(Candidate candidate, out string warning)
    {
        warning = string.Empty;
        var region = candidate.Region;
        if (region is null)
        {
            warning = "Region inconnue : je refuse d'ecrire sans contexte memoire.";
            return false;
        }

        if (region.State != "MEM_COMMIT" || !region.IsReadable || !region.IsWritable || region.IsExecutable)
        {
            warning = $"Region non autorisee pour ecriture : state {region.State}, protection {region.Protection}, read {region.IsReadable}, write {region.IsWritable}, exec {region.IsExecutable}.";
            return false;
        }

        return true;
    }

    private static TreasuryWriteResult Refused(
        DateTimeOffset createdAt,
        int desiredValue,
        string message,
        List<string> evidence,
        List<string> warnings,
        KnownValueScanSession? session = null,
        Candidate? candidate = null)
    {
        return BuildResult(
            createdAt,
            session,
            candidate,
            desiredValue,
            success: false,
            status: TreasuryWriteStatuses.Refused,
            message,
            valueBefore: null,
            valueAfterWrite: null,
            valueAfterStabilityDelay: null,
            evidence,
            warnings);
    }

    private static TreasuryWriteResult BuildResult(
        DateTimeOffset createdAt,
        KnownValueScanSession? session,
        Candidate? candidate,
        int desiredValue,
        bool success,
        string status,
        string message,
        int? valueBefore,
        int? valueAfterWrite,
        int? valueAfterStabilityDelay,
        List<string> evidence,
        List<string> warnings)
    {
        var region = candidate?.Region;
        var address = candidate?.Address;
        var regionOffset = candidate is null || region is null
            ? null
            : $"0x{candidate.Address - region.BaseAddress:X}";

        return new TreasuryWriteResult(
            CreatedAt: createdAt,
            FeatureId: session?.FeatureId ?? KnownValueScanner.TreasuryFeatureId,
            Success: success,
            Status: status,
            Message: message,
            CandidateId: candidate?.CandidateId,
            Address: address,
            AddressHex: address is null ? null : $"0x{address.Value:X}",
            Type: candidate?.Type ?? "Int32",
            CandidateCount: session?.Candidates.Count ?? 0,
            IsAmbiguousSelection: (session?.Candidates.Count ?? 0) > 1,
            ExpectedCurrentValue: session?.CurrentValue,
            DesiredValue: desiredValue,
            ValueBefore: valueBefore,
            ValueAfterWrite: valueAfterWrite,
            ValueAfterStabilityDelay: valueAfterStabilityDelay,
            RegionBaseHex: region is null ? null : $"0x{region.BaseAddress:X}",
            RegionOffsetHex: regionOffset,
            RegionState: region?.State,
            RegionProtection: region?.Protection,
            RegionType: region?.Type,
            CandidateEvidence: candidate?.Evidence.ToArray() ?? Array.Empty<string>(),
            Evidence: evidence.ToArray(),
            Warnings: warnings.Concat(candidate?.Warnings ?? Array.Empty<string>()).Distinct().ToArray());
    }
}
