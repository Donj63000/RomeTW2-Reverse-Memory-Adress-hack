using System.Buffers.Binary;
using Rome2Explorer.Domain;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Features;

public sealed class TreasuryPointerAnalyzer
{
    public TreasuryPointerAnalysisResult Analyze(
        KnownValueScanSession session,
        Candidate candidate,
        IProcessMemoryReader reader,
        MemoryMapSnapshot snapshot,
        TreasuryPointerAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(snapshot);

        options ??= TreasuryPointerAnalysisOptions.Default;
        ValidateOptions(options);
        ValidateInput(session, candidate);

        var warnings = new List<string>();
        var evidence = new List<string>
        {
            "Analyse pointeurs treasury lancee en lecture seule.",
            "Pointeurs interpretes en UInt32 car Rome2 est attendu en x86."
        };

        if (snapshot.Process.Architecture != ProcessArchitecture.X86)
        {
            warnings.Add($"Architecture snapshot {snapshot.Process.Architecture} : l'analyse continue en UInt32 mais doit etre verifiee.");
        }

        var candidateRegion = FindRegion(snapshot, candidate.Address) ?? candidate.Region;
        var context = ReadCandidateContext(candidate, candidateRegion, reader, options, warnings);
        var baseTargets = BuildStructureBaseTargets(candidate, snapshot, options);
        var targetMap = BuildTargetMap(candidate, baseTargets);

        // Je fais un scan borne et aligne, pas un pointer scan infini façon CE :
        // profondeur 1 d'abord, puis une seconde passe uniquement vers les pointeurs directs trouves.
        var directScan = ScanPointerTargets(reader, snapshot, targetMap, options, cancellationToken);
        warnings.AddRange(directScan.Warnings);

        var chainTargets = directScan.Hits
            .Where(hit => hit.PointerAddress <= uint.MaxValue)
            .GroupBy(hit => (uint)hit.PointerAddress)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(hit => new PointerTarget(hit.PointerAddress, $"level1:{hit.PointerId}", hit.TargetKind))
                    .ToArray());
        var chainScan = chainTargets.Count == 0 || directScan.HitLimitReached
            ? PointerScanResult.Empty
            : ScanPointerTargets(reader, snapshot, chainTargets, options with { MaxPointerHits = options.MaxPointerChains }, cancellationToken);
        warnings.AddRange(chainScan.Warnings);

        var chains = BuildChains(directScan.Hits, chainScan.Hits, options);
        if (chains.Count >= options.MaxPointerChains)
        {
            warnings.Add($"Limite de {options.MaxPointerChains} chaines pointeurs atteinte.");
        }

        var structureBases = ScoreStructureBases(
                candidate,
                baseTargets,
                directScan.Hits,
                chains,
                context,
                options)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.TreasuryOffset)
            .ThenBy(result => result.BaseAddress)
            .Take(options.MaxBaseCandidates)
            .ToArray();

        var overallVerdict = BuildOverallVerdict(structureBases, directScan.Hits);
        var best = structureBases.FirstOrDefault();
        if (best is not null)
        {
            evidence.Add($"Meilleure base candidate {best.BaseAddressHex}+{best.TreasuryOffsetHex}, score {best.Score:0.00}.");
        }

        var allHits = directScan.Hits.Concat(chainScan.Hits).ToArray();
        return new TreasuryPointerAnalysisResult(
            CreatedAt: DateTimeOffset.Now,
            FeatureId: session.FeatureId,
            CandidateId: candidate.CandidateId,
            CandidateAddress: candidate.Address,
            CandidateAddressHex: $"0x{candidate.Address:X}",
            ValueType: candidate.Type,
            CandidateCount: session.Candidates.Count,
            ValueHistory: session.ValueHistory.ToArray(),
            ContextByteCount: context.Bytes.Length,
            ContextNonZeroByteCount: context.NonZeroByteCount,
            ContextStartAddressHex: $"0x{context.StartAddress:X}",
            ContextBytesHexPreview: ToHexPreview(context.Bytes, 128),
            TestedBaseCount: baseTargets.Count,
            ScannedRegionCount: directScan.ScannedRegionCount,
            ScannedBytes: directScan.ScannedBytes,
            HitLimitReached: directScan.HitLimitReached || chainScan.HitLimitReached,
            OverallVerdict: overallVerdict,
            BestStructureBase: best,
            StructureBases: structureBases,
            PointerHits: allHits,
            PointerChains: chains,
            Evidence: evidence.ToArray(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateInput(KnownValueScanSession session, Candidate candidate)
    {
        if (!string.Equals(session.FeatureId, KnownValueScanner.TreasuryFeatureId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Session non supportee : {session.FeatureId}.");
        }

        if (!session.Candidates.Any(item => string.Equals(item.CandidateId, candidate.CandidateId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Le candidat selectionne n'appartient pas a la session treasury courante.");
        }

        if (candidate.Address > uint.MaxValue)
        {
            throw new InvalidOperationException($"Adresse 0x{candidate.Address:X} hors plage pointeur x86.");
        }
    }

    private static CandidateContext ReadCandidateContext(
        Candidate candidate,
        MemoryRegionInfo? region,
        IProcessMemoryReader reader,
        TreasuryPointerAnalysisOptions options,
        List<string> warnings)
    {
        if (region is null || !region.IsReadable)
        {
            warnings.Add("Contexte candidat non lu : region absente ou non lisible.");
            return new CandidateContext(candidate.Address, Array.Empty<byte>());
        }

        var regionEnd = SafeAdd(region.BaseAddress, region.Size);
        var start = candidate.Address > region.BaseAddress
            ? candidate.Address - Math.Min((ulong)options.ContextBeforeBytes, candidate.Address - region.BaseAddress)
            : region.BaseAddress;
        var requestedEnd = SafeAdd(candidate.Address, (ulong)sizeof(int) + (ulong)options.ContextAfterBytes);
        var end = Math.Min(regionEnd, requestedEnd);
        if (end <= start)
        {
            warnings.Add("Contexte candidat vide apres bornage region.");
            return new CandidateContext(start, Array.Empty<byte>());
        }

        var count = checked((int)Math.Min(end - start, (ulong)int.MaxValue));
        if (!reader.TryReadBytes(start, count, out var bytes, out var warning))
        {
            warnings.Add(warning ?? $"Lecture contexte impossible autour de 0x{candidate.Address:X}.");
            return new CandidateContext(start, Array.Empty<byte>());
        }

        return new CandidateContext(start, bytes);
    }

    private static IReadOnlyList<StructureBaseTarget> BuildStructureBaseTargets(
        Candidate candidate,
        MemoryMapSnapshot snapshot,
        TreasuryPointerAnalysisOptions options)
    {
        var targets = new List<StructureBaseTarget>();
        var region = FindRegion(snapshot, candidate.Address) ?? candidate.Region;
        var minBase = region?.BaseAddress ?? 0UL;

        for (var offset = 0; offset <= options.MaxStructureOffset; offset += options.Alignment)
        {
            if ((ulong)offset > candidate.Address)
            {
                break;
            }

            var baseAddress = candidate.Address - (ulong)offset;
            if (region is not null && baseAddress < minBase)
            {
                continue;
            }

            targets.Add(new StructureBaseTarget(baseAddress, offset, region));
        }

        return targets;
    }

    private static Dictionary<uint, PointerTarget[]> BuildTargetMap(Candidate candidate, IReadOnlyList<StructureBaseTarget> baseTargets)
    {
        var map = new Dictionary<uint, List<PointerTarget>>
        {
            [(uint)candidate.Address] = new()
            {
                new PointerTarget(candidate.Address, "treasuryAddress", "TreasuryValue")
            }
        };

        foreach (var target in baseTargets)
        {
            if (target.TreasuryOffset == 0)
            {
                continue;
            }

            if (target.BaseAddress > uint.MaxValue)
            {
                continue;
            }

            var key = (uint)target.BaseAddress;
            if (!map.TryGetValue(key, out var targets))
            {
                targets = new List<PointerTarget>();
                map[key] = targets;
            }

            targets.Add(new PointerTarget(target.BaseAddress, $"structureBase:+0x{target.TreasuryOffset:X}", "StructureBase"));
        }

        return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static PointerScanResult ScanPointerTargets(
        IProcessMemoryReader reader,
        MemoryMapSnapshot snapshot,
        IReadOnlyDictionary<uint, PointerTarget[]> targetMap,
        TreasuryPointerAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        if (targetMap.Count == 0)
        {
            return PointerScanResult.Empty;
        }

        var hits = new List<PointerHit>();
        var warnings = new List<string>();
        var scannedRegionCount = 0;
        ulong scannedBytes = 0;
        var hitLimitReached = false;

        foreach (var region in snapshot.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedPointerRegion(region))
            {
                continue;
            }

            scannedRegionCount++;
            var regionOffset = 0UL;
            while (regionOffset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = region.Size - regionOffset;
                var readSize = (int)Math.Min((ulong)options.ChunkSizeBytes, remaining);
                readSize -= readSize % options.Alignment;
                if (readSize < sizeof(uint))
                {
                    break;
                }

                var chunkAddress = region.BaseAddress + regionOffset;
                if (!reader.TryReadBytes(chunkAddress, readSize, out var bytes, out var warning))
                {
                    warnings.Add(warning ?? $"Lecture pointeurs ignoree a 0x{chunkAddress:X}.");
                    regionOffset += (ulong)readSize;
                    continue;
                }

                scannedBytes += (ulong)bytes.Length;
                foreach (var hit in FindPointerHits(bytes, chunkAddress, region, snapshot, targetMap))
                {
                    hits.Add(hit);
                    if (hits.Count >= options.MaxPointerHits)
                    {
                        hitLimitReached = true;
                        warnings.Add($"Limite de {options.MaxPointerHits} pointeurs atteinte : analyse bornee pour eviter l'explosion.");
                        break;
                    }
                }

                if (hitLimitReached)
                {
                    break;
                }

                regionOffset += (ulong)readSize;
            }

            if (hitLimitReached)
            {
                break;
            }
        }

        return new PointerScanResult(hits, warnings, scannedRegionCount, scannedBytes, hitLimitReached);
    }

    private static IEnumerable<PointerHit> FindPointerHits(
        byte[] bytes,
        ulong chunkAddress,
        MemoryRegionInfo region,
        MemoryMapSnapshot snapshot,
        IReadOnlyDictionary<uint, PointerTarget[]> targetMap)
    {
        var firstAlignedOffset = (int)((4 - (chunkAddress % 4)) % 4);
        for (var offset = firstAlignedOffset; offset <= bytes.Length - sizeof(uint); offset += sizeof(uint))
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
            if (!targetMap.TryGetValue(value, out var targets))
            {
                continue;
            }

            var pointerAddress = chunkAddress + (ulong)offset;
            var module = FindModule(snapshot, pointerAddress);
            foreach (var target in targets)
            {
                yield return new PointerHit(
                    PointerId: $"ptr-0x{pointerAddress:X}-to-0x{target.Address:X}",
                    Level: target.Kind.StartsWith("level1:", StringComparison.Ordinal) ? 2 : 1,
                    PointerAddress: pointerAddress,
                    PointerAddressHex: $"0x{pointerAddress:X}",
                    TargetAddress: target.Address,
                    TargetAddressHex: $"0x{target.Address:X}",
                    TargetKind: target.TargetKind,
                    SourceRegionBase: region.BaseAddress,
                    SourceRegionBaseHex: $"0x{region.BaseAddress:X}",
                    SourceRegionProtection: region.Protection,
                    SourceRegionType: region.Type,
                    SourceModuleName: module?.Name,
                    SourceModuleOffsetHex: module is null ? null : $"0x{pointerAddress - module.BaseAddress:X}",
                    Evidence: new[] { $"0x{pointerAddress:X} contient 0x{target.Address:X} ({target.Kind})." });
            }
        }
    }

    private static IReadOnlyList<PointerChainCandidate> BuildChains(
        IReadOnlyList<PointerHit> directHits,
        IReadOnlyList<PointerHit> secondLevelHits,
        TreasuryPointerAnalysisOptions options)
    {
        var directByPointerAddress = directHits
            .GroupBy(hit => hit.PointerAddress)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var chains = new List<PointerChainCandidate>();

        foreach (var secondLevelHit in secondLevelHits)
        {
            if (!directByPointerAddress.TryGetValue(secondLevelHit.TargetAddress, out var directTargets))
            {
                continue;
            }

            foreach (var direct in directTargets)
            {
                var score = direct.TargetKind == "StructureBase" ? 75 : 45;
                chains.Add(new PointerChainCandidate(
                    ChainId: $"chain-0x{secondLevelHit.PointerAddress:X}-0x{direct.PointerAddress:X}-0x{direct.TargetAddress:X}",
                    Depth: 2,
                    RootPointerAddress: secondLevelHit.PointerAddress,
                    RootPointerAddressHex: secondLevelHit.PointerAddressHex,
                    IntermediatePointerAddress: direct.PointerAddress,
                    IntermediatePointerAddressHex: direct.PointerAddressHex,
                    FinalTargetAddress: direct.TargetAddress,
                    FinalTargetAddressHex: direct.TargetAddressHex,
                    FinalTargetKind: direct.TargetKind,
                    Score: score,
                    Evidence: new[]
                    {
                        $"0x{secondLevelHit.PointerAddress:X} -> 0x{direct.PointerAddress:X}",
                        $"0x{direct.PointerAddress:X} -> 0x{direct.TargetAddress:X} ({direct.TargetKind})"
                    }));

                if (chains.Count >= options.MaxPointerChains)
                {
                    return chains;
                }
            }
        }

        return chains;
    }

    private static IEnumerable<StructureBaseCandidate> ScoreStructureBases(
        Candidate candidate,
        IReadOnlyList<StructureBaseTarget> targets,
        IReadOnlyList<PointerHit> pointerHits,
        IReadOnlyList<PointerChainCandidate> chains,
        CandidateContext context,
        TreasuryPointerAnalysisOptions options)
    {
        var candidatePointerHitCount = pointerHits.Count(hit => hit.TargetAddress == candidate.Address && hit.TargetKind == "TreasuryValue");
        foreach (var target in targets)
        {
            var evidence = new List<string>();
            var warnings = new List<string>();
            var score = 0.0;
            var directBaseHits = pointerHits.Where(hit => hit.TargetAddress == target.BaseAddress && hit.TargetKind == "StructureBase").ToArray();
            var chainHits = chains.Where(chain => chain.FinalTargetAddress == target.BaseAddress).ToArray();

            Add(ref score, Math.Min(35, directBaseHits.Length * 8), evidence, $"{directBaseHits.Length} pointeur(s) entrant(s) vers la base");
            Add(ref score, Math.Min(15, chainHits.Length * 5), evidence, $"{chainHits.Length} chaine(s) profondeur 2 vers la base");
            Add(ref score, Math.Min(10, candidatePointerHitCount * 2), evidence, $"{candidatePointerHitCount} pointeur(s) direct(s) vers la valeur treasury");
            AddOffsetScore(ref score, evidence, target.TreasuryOffset);
            Add(ref score, candidate.Confidence * 16, evidence, $"confiance scan {candidate.Confidence:0.00}");

            if (target.Region is not null)
            {
                if (target.Region.State == "MEM_COMMIT")
                {
                    Add(ref score, 4, evidence, "base dans region commit");
                }

                if (target.Region.IsReadable && target.Region.IsWritable && !target.Region.IsExecutable)
                {
                    Add(ref score, 8, evidence, "base dans region data read/write non executable");
                }

                if (target.Region.Type == "MEM_PRIVATE")
                {
                    Add(ref score, 6, evidence, "region private probable gameplay");
                }
            }
            else
            {
                warnings.Add("Base hors region connue.");
            }

            if (context.Bytes.Length > 0)
            {
                var nonZeroRatio = (double)context.NonZeroByteCount / context.Bytes.Length;
                Add(ref score, Math.Min(8, nonZeroRatio * 10), evidence, $"contexte non nul {nonZeroRatio:P0}");
            }

            if (directBaseHits.Any(hit => hit.SourceModuleName is not null))
            {
                Add(ref score, 6, evidence, "pointeur source dans module connu");
            }

            if (directBaseHits.Length == 0 && chainHits.Length == 0)
            {
                warnings.Add("Aucun pointeur entrant direct vers cette base.");
            }

            var clamped = Math.Round(Math.Clamp(score - Math.Min(15, warnings.Count * 3), 0, 100), 2);
            yield return new StructureBaseCandidate(
                BaseAddress: target.BaseAddress,
                BaseAddressHex: $"0x{target.BaseAddress:X}",
                TreasuryOffset: target.TreasuryOffset,
                TreasuryOffsetHex: $"0x{target.TreasuryOffset:X}",
                RegionBaseHex: target.Region is null ? "unknown" : $"0x{target.Region.BaseAddress:X}",
                RegionProtection: target.Region?.Protection ?? "unknown",
                RegionType: target.Region?.Type ?? "unknown",
                DirectPointerHitCount: directBaseHits.Length,
                CandidatePointerHitCount: candidatePointerHitCount,
                PointerChainCount: chainHits.Length,
                Score: clamped,
                Status: BuildBaseStatus(clamped, directBaseHits.Length, chainHits.Length),
                Evidence: evidence.ToArray(),
                Warnings: warnings.ToArray());
        }
    }

    private static string BuildOverallVerdict(IReadOnlyList<StructureBaseCandidate> bases, IReadOnlyList<PointerHit> directHits)
    {
        if (directHits.Count == 0)
        {
            return TreasuryPointerAnalysisVerdicts.NoPointerFound;
        }

        if (bases.Count == 0 || bases.All(candidate => candidate.DirectPointerHitCount == 0 && candidate.PointerChainCount == 0))
        {
            return TreasuryPointerAnalysisVerdicts.RawCandidateOnly;
        }

        var best = bases[0];
        var second = bases.Count > 1 ? bases[1] : null;
        var margin = second is null ? best.Score : best.Score - second.Score;
        if (best.Score >= 50 && margin >= 8 && best.Status == "Probable")
        {
            return TreasuryPointerAnalysisVerdicts.ProbableStructure;
        }

        if (best.Score >= 45 && second is not null && margin < 8)
        {
            return TreasuryPointerAnalysisVerdicts.AmbiguousStructure;
        }

        return TreasuryPointerAnalysisVerdicts.NeedsRestartValidation;
    }

    private static string BuildBaseStatus(double score, int directHits, int chains)
    {
        if (directHits == 0 && chains == 0)
        {
            return "RawCandidateOnly";
        }

        if (score >= 50)
        {
            return "Probable";
        }

        return "Candidate";
    }

    private static void AddOffsetScore(ref double score, List<string> evidence, int offset)
    {
        if (offset == 0)
        {
            Add(ref score, 2, evidence, "offset 0 : valeur brute, structure non demontree");
            return;
        }

        if (offset <= 0x200)
        {
            Add(ref score, 12, evidence, $"offset treasury plausible +0x{offset:X}");
            return;
        }

        Add(ref score, 6, evidence, $"offset treasury large +0x{offset:X}");
    }

    private static void ValidateOptions(TreasuryPointerAnalysisOptions options)
    {
        if (options.Alignment <= 0 || options.Alignment % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "L'alignement pointeur doit etre un multiple positif de 4.");
        }

        if (options.ChunkSizeBytes < sizeof(uint) || options.ChunkSizeBytes % options.Alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Le chunk pointeur doit etre aligne et faire au moins 4 octets.");
        }

        if (options.MaxStructureOffset < 0 || options.MaxStructureOffset % options.Alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "L'offset structure max doit etre positif et aligne.");
        }

        if (options.MaxPointerHits <= 0 || options.MaxPointerChains <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Les limites de pointeurs doivent etre positives.");
        }
    }

    private static bool IsSupportedPointerRegion(MemoryRegionInfo region)
    {
        return region.State == "MEM_COMMIT"
            && region.IsReadable
            && !region.IsExecutable
            && region.Size >= sizeof(uint);
    }

    private static MemoryRegionInfo? FindRegion(MemoryMapSnapshot snapshot, ulong address)
    {
        return snapshot.Regions.FirstOrDefault(region =>
            address >= region.BaseAddress
            && address < SafeAdd(region.BaseAddress, region.Size));
    }

    private static ModuleInfo? FindModule(MemoryMapSnapshot snapshot, ulong address)
    {
        return snapshot.Modules.FirstOrDefault(module =>
            module.Size > 0
            && address >= module.BaseAddress
            && address - module.BaseAddress < (ulong)module.Size);
    }

    private static ulong SafeAdd(ulong left, ulong right)
    {
        var result = left + right;
        return result < left ? ulong.MaxValue : result;
    }

    private static void Add(ref double score, double delta, List<string> evidence, string reason)
    {
        score += delta;
        evidence.Add(reason);
    }

    private static string ToHexPreview(byte[] bytes, int maxBytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        return Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, maxBytes)));
    }

    private sealed record PointerTarget(ulong Address, string Kind, string TargetKind);

    private sealed record StructureBaseTarget(ulong BaseAddress, int TreasuryOffset, MemoryRegionInfo? Region);

    private sealed record CandidateContext(ulong StartAddress, byte[] Bytes)
    {
        public int NonZeroByteCount => Bytes.Count(value => value != 0);
    }

    private sealed record PointerScanResult(
        IReadOnlyList<PointerHit> Hits,
        IReadOnlyList<string> Warnings,
        int ScannedRegionCount,
        ulong ScannedBytes,
        bool HitLimitReached)
    {
        public static PointerScanResult Empty { get; } = new(
            Array.Empty<PointerHit>(),
            Array.Empty<string>(),
            0,
            0,
            false);
    }
}
