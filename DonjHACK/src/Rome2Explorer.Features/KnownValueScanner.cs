using System.Buffers.Binary;
using System.Globalization;
using Rome2Explorer.Domain;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Features;

public sealed class KnownValueScanner
{
    public const string TreasuryFeatureId = "campaign.player_faction.treasury";
    private const string Int32TypeName = "Int32";

    public KnownValueScanSession StartExactInt32Scan(
        MemoryMapSnapshot snapshot,
        IProcessMemoryReader reader,
        int expectedValue,
        KnownValueScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reader);

        options ??= KnownValueScanOptions.Default;
        ValidateOptions(options);

        var candidates = new List<Candidate>();
        var warnings = new List<string>();
        var regionsConsidered = 0;
        var regionsScanned = 0;
        var regionsSkipped = 0;
        var readFailures = 0;
        ulong bytesScanned = 0;
        var reachedCandidateLimit = false;

        foreach (var region in snapshot.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            regionsConsidered++;

            if (!IsSupportedScanRegion(region))
            {
                regionsSkipped++;
                continue;
            }

            regionsScanned++;
            var regionOffset = 0UL;

            while (regionOffset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = region.Size - regionOffset;
                var readSize = (int)Math.Min((ulong)options.ChunkSizeBytes, remaining);
                if (readSize < sizeof(int))
                {
                    break;
                }

                var chunkAddress = region.BaseAddress + regionOffset;
                if (!reader.TryReadBytes(chunkAddress, readSize, out var bytes, out var warning))
                {
                    readFailures++;
                    warnings.Add(warning ?? $"Lecture ignoree a 0x{chunkAddress:X}.");
                    regionOffset += (ulong)readSize;
                    continue;
                }

                bytesScanned += (ulong)bytes.Length;
                // Je scanne uniquement les adresses alignees sur 4 octets : c'est le format naturel d'un Int32
                // et ca reduit fortement les faux positifs et le cout CPU sur Rome2 x86.
                foreach (var candidate in FindInt32CandidatesInChunk(bytes, chunkAddress, expectedValue, region))
                {
                    candidates.Add(candidate);
                    if (candidates.Count >= options.MaxCandidates)
                    {
                        reachedCandidateLimit = true;
                        warnings.Add($"Limite de {options.MaxCandidates} candidats atteinte : change la valeur en jeu puis affine le scan.");
                        break;
                    }
                }

                if (reachedCandidateLimit)
                {
                    break;
                }

                regionOffset += (ulong)readSize;
            }

            if (reachedCandidateLimit)
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            warnings.Add($"Aucun candidat Int32 trouve pour la valeur {expectedValue}.");
        }

        var now = DateTimeOffset.Now;
        return new KnownValueScanSession(
            FeatureId: TreasuryFeatureId,
            ValueType: Int32TypeName,
            CreatedAt: now,
            UpdatedAt: now,
            ValueHistory: new[] { expectedValue },
            Candidates: candidates.ToArray(),
            Counters: new KnownValueScanCounters(
                RegionsConsidered: regionsConsidered,
                RegionsScanned: regionsScanned,
                RegionsSkipped: regionsSkipped,
                BytesScanned: bytesScanned,
                ReadFailures: readFailures,
                CandidatesBefore: 0,
                CandidatesAfter: candidates.Count),
            Warnings: warnings.ToArray());
    }

    public KnownValueScanSession RefineExactInt32Scan(
        KnownValueScanSession session,
        IProcessMemoryReader reader,
        int expectedValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reader);

        var candidates = new List<Candidate>();
        var warnings = new List<string>(session.Warnings);
        var readFailures = 0;
        ulong bytesScanned = 0;

        foreach (var candidate in session.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bytesScanned += sizeof(int);

            // Ici on ne rescane pas toute la memoire : je relis seulement les adresses candidates.
            // C'est l'etape qui transforme une grosse liste de faux positifs en quelques adresses plausibles.
            if (!reader.TryReadInt32(candidate.Address, out var observedValue, out var warning))
            {
                readFailures++;
                warnings.Add(warning ?? $"Candidat ignore : lecture impossible a 0x{candidate.Address:X}.");
                continue;
            }

            if (observedValue != expectedValue)
            {
                continue;
            }

            candidates.Add(candidate with
            {
                ObservedValue = observedValue.ToString(CultureInfo.InvariantCulture),
                ExpectedValue = expectedValue.ToString(CultureInfo.InvariantCulture),
                Evidence = AppendEvidence(candidate.Evidence, $"Refine OK : 0x{candidate.Address:X} vaut maintenant {expectedValue}."),
                Confidence = Math.Min(0.95, candidate.Confidence + 0.25)
            });
        }

        if (candidates.Count == 0)
        {
            warnings.Add($"Refine termine : aucun candidat restant pour la valeur {expectedValue}.");
        }

        return session with
        {
            UpdatedAt = DateTimeOffset.Now,
            ValueHistory = session.ValueHistory.Concat(new[] { expectedValue }).ToArray(),
            Candidates = candidates.ToArray(),
            Counters = new KnownValueScanCounters(
                RegionsConsidered: 0,
                RegionsScanned: 0,
                RegionsSkipped: 0,
                BytesScanned: bytesScanned,
                ReadFailures: readFailures,
                CandidatesBefore: session.Candidates.Count,
                CandidatesAfter: candidates.Count),
            Warnings = warnings.ToArray()
        };
    }

    public static bool IsSupportedScanRegion(MemoryRegionInfo region)
    {
        return region.State == "MEM_COMMIT"
            && region.IsReadable
            && region.IsWritable
            && !region.IsExecutable
            && region.Size >= sizeof(int);
    }

    private static IEnumerable<Candidate> FindInt32CandidatesInChunk(
        byte[] bytes,
        ulong chunkAddress,
        int expectedValue,
        MemoryRegionInfo region)
    {
        var firstAlignedOffset = (int)((4 - (chunkAddress % 4)) % 4);

        for (var offset = firstAlignedOffset; offset <= bytes.Length - sizeof(int); offset += sizeof(int))
        {
            var observedValue = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
            if (observedValue != expectedValue)
            {
                continue;
            }

            var address = chunkAddress + (ulong)offset;
            yield return new Candidate(
                FeatureId: TreasuryFeatureId,
                CandidateId: $"treasury-int32-0x{address:X8}",
                Address: address,
                Type: Int32TypeName,
                ObservedValue: observedValue.ToString(CultureInfo.InvariantCulture),
                ExpectedValue: expectedValue.ToString(CultureInfo.InvariantCulture),
                Region: region,
                SuspectedStructure: null,
                Owner: OwnerKind.Unknown,
                OwnerConfidence: 0,
                Evidence: new[] { $"Scan initial : valeur Int32 {expectedValue} trouvee a 0x{address:X}." },
                Warnings: Array.Empty<string>(),
                Confidence: 0.15);
        }
    }

    private static IReadOnlyList<string> AppendEvidence(IReadOnlyList<string> evidence, string newEvidence)
    {
        return evidence.Concat(new[] { newEvidence }).ToArray();
    }

    private static void ValidateOptions(KnownValueScanOptions options)
    {
        if (options.ChunkSizeBytes < sizeof(int))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Le chunk de scan doit faire au moins 4 octets.");
        }

        if (options.ChunkSizeBytes % sizeof(int) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Le chunk de scan Int32 doit etre aligne sur 4 octets.");
        }

        if (options.MaxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "La limite de candidats doit etre positive.");
        }
    }
}
