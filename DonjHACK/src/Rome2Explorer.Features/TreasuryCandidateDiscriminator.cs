using Rome2Explorer.Domain;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Features;

public sealed class TreasuryCandidateDiscriminator
{
    private const double FavoriteMarginThreshold = 8.0;
    private readonly Func<DateTimeOffset> _clock;

    public TreasuryCandidateDiscriminator()
        : this(() => DateTimeOffset.Now)
    {
    }

    public TreasuryCandidateDiscriminator(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public static bool CanRun(KnownValueScanSession? session, int maxCandidates = 10)
    {
        return session is not null
            && session.ValueHistory.Count > 1
            && session.Candidates.Count >= 2
            && session.Candidates.Count <= maxCandidates;
    }

    public async Task<TreasuryDiscriminatorResult> RunAsync(
        KnownValueScanSession session,
        IReadOnlyList<Candidate> candidates,
        IProcessMemoryReader reader,
        MemoryMapSnapshot snapshot,
        TreasuryDiscriminatorOptions? options = null,
        IProgress<TreasuryDiscriminatorLogEntry>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(snapshot);

        options ??= TreasuryDiscriminatorOptions.Default;
        ValidateOptions(options);
        ValidateInput(session, candidates, options);

        var logs = new List<TreasuryDiscriminatorLogEntry>();
        var samples = new List<TreasuryDiscriminatorSample>();
        var candidateStates = candidates
            .Select(candidate => new CandidateObservationState(
                candidate: candidate,
                region: candidate.Region ?? FindRegion(snapshot, candidate.Address)))
            .ToArray();

        void Log(string eventType, Candidate? candidate, string message)
        {
            var entry = new TreasuryDiscriminatorLogEntry(
                Timestamp: _clock(),
                EventType: eventType,
                CandidateId: candidate?.CandidateId,
                AddressHex: candidate is null ? null : $"0x{candidate.Address:X}",
                Message: message);

            logs.Add(entry);
            progress?.Report(entry);
        }

        Log("Start", null, $"Departage read-only lance sur {candidateStates.Length} candidat(s), fenetre {options.ObservationDuration.TotalSeconds:0.#}s.");

        foreach (var state in candidateStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCandidateContext(state, reader, snapshot, options, Log);
        }

        var sampleCount = ComputeSampleCount(options);
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var state in candidateStates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var timestamp = _clock();
                var addressHex = $"0x{state.Candidate.Address:X}";

                // Je lis uniquement les 4 octets Int32 de l'adresse candidate.
                // Aucune ecriture, aucun hook, aucune injection : c'est une observation temporelle.
                if (!reader.TryReadInt32(state.Candidate.Address, out var observedValue, out var warning))
                {
                    state.FailedReads++;
                    state.Warnings.Add(warning ?? $"Lecture impossible a {addressHex}.");
                    samples.Add(new TreasuryDiscriminatorSample(timestamp, sampleIndex, state.Candidate.CandidateId, state.Candidate.Address, addressHex, false, null, warning));
                    Log("ReadError", state.Candidate, warning ?? $"Lecture impossible a {addressHex}.");
                    continue;
                }

                var previousValue = state.Values.Count == 0 ? (int?)null : state.Values[^1];
                state.Values.Add(observedValue);
                state.SuccessfulReads++;
                samples.Add(new TreasuryDiscriminatorSample(timestamp, sampleIndex, state.Candidate.CandidateId, state.Candidate.Address, addressHex, true, observedValue, null));
                Log("Read", state.Candidate, $"Sample {sampleIndex}: {addressHex} = {observedValue}.");

                if (previousValue.HasValue && previousValue.Value != observedValue)
                {
                    state.ChangeCount++;
                    state.ChangeSamples.Add(sampleIndex);
                    if (state.FirstChangeSampleIndex is null)
                    {
                        state.FirstChangeSampleIndex = sampleIndex;
                        state.FirstChangeAt = timestamp;
                    }

                    Log("Change", state.Candidate, $"{addressHex} change de {previousValue.Value} vers {observedValue} au sample {sampleIndex}.");
                }
            }

            if (sampleIndex < sampleCount - 1)
            {
                await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        var candidateResults = BuildCandidateResults(session, candidateStates);
        var finalResults = ApplyFinalVerdict(candidateResults);
        var favorite = finalResults.SingleOrDefault(candidate => candidate.IsFavorite);
        var isAmbiguous = favorite is null;
        var overallVerdict = isAmbiguous
            ? "Toujours ambigu - analyse pointeur necessaire"
            : $"Favori de departage: {favorite!.AddressHex} ({favorite.Verdict})";

        Log("Conclusion", null, overallVerdict);

        return new TreasuryDiscriminatorResult(
            CreatedAt: _clock(),
            FeatureId: session.FeatureId,
            ValueHistory: session.ValueHistory.ToArray(),
            ObservationDuration: options.ObservationDuration,
            PollInterval: options.PollInterval,
            CandidateCount: candidateStates.Length,
            OverallVerdict: overallVerdict,
            FavoriteCandidateId: favorite?.CandidateId,
            FavoriteAddressHex: favorite?.AddressHex,
            IsAmbiguous: isAmbiguous,
            Candidates: finalResults,
            Samples: samples.ToArray(),
            Log: logs.ToArray());
    }

    private IReadOnlyList<TreasuryDiscriminatorCandidateResult> BuildCandidateResults(
        KnownValueScanSession session,
        IReadOnlyList<CandidateObservationState> states)
    {
        var earliestChange = states
            .Where(state => state.FirstChangeSampleIndex.HasValue)
            .Select(state => state.FirstChangeSampleIndex!.Value)
            .DefaultIfEmpty()
            .Min();
        var hasAnyChange = states.Any(state => state.FirstChangeSampleIndex.HasValue);
        var uniqueEarliestCandidateId = hasAnyChange
            ? states
                .Where(state => state.FirstChangeSampleIndex == earliestChange)
                .Select(state => state.Candidate.CandidateId)
                .ToArray()
            : Array.Empty<string>();
        var regionDensity = states
            .Where(state => state.Region is not null)
            .GroupBy(state => state.Region!.BaseAddress)
            .ToDictionary(group => group.Key, group => group.Count());

        return states
            .Select(state => BuildCandidateResult(session, state, regionDensity, uniqueEarliestCandidateId))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Address)
            .ToArray();
    }

    private TreasuryDiscriminatorCandidateResult BuildCandidateResult(
        KnownValueScanSession session,
        CandidateObservationState state,
        IReadOnlyDictionary<ulong, int> regionDensity,
        IReadOnlyList<string> uniqueEarliestCandidateIds)
    {
        var candidate = state.Candidate;
        var region = state.Region;
        var reasons = new List<string>();
        var score = 0.0;
        var readCount = state.SuccessfulReads + state.FailedReads;
        var successRatio = readCount == 0 ? 0 : (double)state.SuccessfulReads / readCount;
        var stableAfterFirstChange = IsStableAfterFirstChange(state);
        var isUniqueEarliest = uniqueEarliestCandidateIds.Count == 1
            && string.Equals(uniqueEarliestCandidateIds[0], candidate.CandidateId, StringComparison.Ordinal);
        var isSynchronized = state.FirstChangeSampleIndex.HasValue
            && uniqueEarliestCandidateIds.Count > 1
            && uniqueEarliestCandidateIds.Contains(candidate.CandidateId, StringComparer.Ordinal);

        Add(ref score, successRatio * 20, reasons, $"lectures reussies {state.SuccessfulReads}/{readCount}");

        if (state.FailedReads == 0)
        {
            Add(ref score, 10, reasons, "aucune erreur de lecture");
        }
        else
        {
            Add(ref score, -Math.Min(20, state.FailedReads * 5), reasons, "erreurs de lecture");
        }

        if (isUniqueEarliest)
        {
            Add(ref score, 25, reasons, $"change en premier au sample {state.FirstChangeSampleIndex}");
        }
        else if (isSynchronized)
        {
            Add(ref score, 8, reasons, $"change synchronise au sample {state.FirstChangeSampleIndex}");
        }
        else if (state.FirstChangeSampleIndex.HasValue)
        {
            Add(ref score, 4, reasons, $"change au sample {state.FirstChangeSampleIndex}");
        }
        else
        {
            Add(ref score, 0, reasons, "aucun changement observe pendant la fenetre");
        }

        if (stableAfterFirstChange)
        {
            Add(ref score, 15, reasons, "stable apres premier changement");
        }
        else if (state.ChangeCount > 1)
        {
            Add(ref score, -8, reasons, "plusieurs changements pendant observation");
        }

        ScoreRegion(region, regionDensity, ref score, reasons);
        ScoreContext(state, ref score, reasons);

        var clamped = Math.Round(Math.Clamp(score, 0, 100), 2);
        var verdict = BuildPreliminaryVerdict(state, isUniqueEarliest, isSynchronized, stableAfterFirstChange);
        var regionBase = region is null ? "unknown" : $"0x{region.BaseAddress:X}";
        var regionOffset = region is null ? "unknown" : $"0x{candidate.Address - region.BaseAddress:X}";

        return new TreasuryDiscriminatorCandidateResult(
            CandidateId: candidate.CandidateId,
            Address: candidate.Address,
            AddressHex: $"0x{candidate.Address:X}",
            Type: session.ValueType,
            RegionBaseHex: regionBase,
            RegionOffsetHex: regionOffset,
            RegionState: region?.State ?? "unknown",
            RegionProtection: region?.Protection ?? "unknown",
            RegionType: region?.Type ?? "unknown",
            InitialValue: state.Values.Count == 0 ? null : state.Values[0],
            FinalValue: state.Values.Count == 0 ? null : state.Values[^1],
            SuccessfulReads: state.SuccessfulReads,
            FailedReads: state.FailedReads,
            ChangeCount: state.ChangeCount,
            FirstChangeSampleIndex: state.FirstChangeSampleIndex,
            FirstChangeAt: state.FirstChangeAt,
            StableAfterFirstChange: stableAfterFirstChange,
            ContextStartAddress: state.ContextStartAddress,
            ContextStartAddressHex: state.ContextStartAddress == 0 ? "n/a" : $"0x{state.ContextStartAddress:X}",
            ContextByteCount: state.ContextBytes.Length,
            ContextNonZeroByteCount: state.ContextBytes.Count(value => value != 0),
            ContextBytesHex: state.ContextBytes.Length == 0 ? string.Empty : Convert.ToHexString(state.ContextBytes),
            Score: clamped,
            Verdict: verdict,
            IsFavorite: false,
            IsSynchronized: isSynchronized,
            Reasons: reasons.ToArray(),
            Warnings: state.Warnings.ToArray());
    }

    private static IReadOnlyList<TreasuryDiscriminatorCandidateResult> ApplyFinalVerdict(
        IReadOnlyList<TreasuryDiscriminatorCandidateResult> candidates)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Address)
            .ToArray();
        var best = ordered[0];
        var secondScore = ordered.Length > 1 ? ordered[1].Score : 0;
        var margin = best.Score - secondScore;
        var bestGroup = ordered.Where(candidate => Math.Abs(candidate.Score - best.Score) < 0.01).ToArray();
        var hasFavorite = bestGroup.Length == 1 && margin >= FavoriteMarginThreshold && best.FailedReads == 0;

        return candidates
            .Select(candidate =>
            {
                if (hasFavorite && string.Equals(candidate.CandidateId, best.CandidateId, StringComparison.Ordinal))
                {
                    return candidate with
                    {
                        IsFavorite = true,
                        Verdict = candidate.Verdict == "Synchronise" ? "Favori de departage" : candidate.Verdict,
                        Reasons = candidate.Reasons.Concat(new[] { $"+0 favori unique, marge {margin:0.##}" }).ToArray()
                    };
                }

                if (bestGroup.Any(bestCandidate => string.Equals(bestCandidate.CandidateId, candidate.CandidateId, StringComparison.Ordinal)))
                {
                    return candidate with
                    {
                        Verdict = candidate.FailedReads > 0
                            ? "Lecture echouee"
                            : "Toujours ambigu"
                    };
                }

                return candidate;
            })
            .OrderBy(candidate => candidate.Address)
            .ToArray();
    }

    private static string BuildPreliminaryVerdict(
        CandidateObservationState state,
        bool isUniqueEarliest,
        bool isSynchronized,
        bool stableAfterFirstChange)
    {
        if (state.SuccessfulReads == 0)
        {
            return "Lecture echouee";
        }

        if (isUniqueEarliest)
        {
            return "Change en premier";
        }

        if (stableAfterFirstChange && state.ChangeCount == 1)
        {
            return "Plus stable";
        }

        if (state.ContextBytes.Count(value => value != 0) >= 16)
        {
            return "Contexte plus riche";
        }

        return isSynchronized ? "Synchronise" : "Toujours ambigu";
    }

    private static bool IsStableAfterFirstChange(CandidateObservationState state)
    {
        if (!state.FirstChangeSampleIndex.HasValue || state.Values.Count < 2)
        {
            return false;
        }

        var firstChangeIndex = state.FirstChangeSampleIndex.Value;
        if (firstChangeIndex >= state.Values.Count)
        {
            return false;
        }

        var valueAfterChange = state.Values[firstChangeIndex];
        return state.Values
            .Skip(firstChangeIndex)
            .All(value => value == valueAfterChange);
    }

    private static void ScoreRegion(
        MemoryRegionInfo? region,
        IReadOnlyDictionary<ulong, int> regionDensity,
        ref double score,
        List<string> reasons)
    {
        if (region is null)
        {
            Add(ref score, -10, reasons, "region inconnue");
            return;
        }

        if (region.State.Equals("MEM_COMMIT", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 5, reasons, "region commit");
        }

        if (region.IsReadable && region.IsWritable && !region.IsExecutable)
        {
            Add(ref score, 15, reasons, "region read/write non executable");
        }
        else
        {
            Add(ref score, -15, reasons, "protection region moins fiable");
        }

        if (region.Type.Equals("MEM_PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            Add(ref score, 8, reasons, "memoire privee gameplay probable");
        }

        if (regionDensity.TryGetValue(region.BaseAddress, out var countInRegion))
        {
            Add(ref score, countInRegion == 1 ? 5 : -3, reasons, countInRegion == 1 ? "candidat isole dans sa region" : "plusieurs candidats dans la meme region");
        }
    }

    private static void ScoreContext(CandidateObservationState state, ref double score, List<string> reasons)
    {
        if (state.ContextBytes.Length == 0)
        {
            Add(ref score, -5, reasons, "contexte memoire absent");
            return;
        }

        var nonZeroBytes = state.ContextBytes.Count(value => value != 0);
        var contextScore = Math.Min(12, nonZeroBytes / 8.0);
        Add(ref score, contextScore, reasons, $"{nonZeroBytes} octet(s) non zero autour");
    }

    private static void ReadCandidateContext(
        CandidateObservationState state,
        IProcessMemoryReader reader,
        MemoryMapSnapshot snapshot,
        TreasuryDiscriminatorOptions options,
        Action<string, Candidate?, string> log)
    {
        var candidate = state.Candidate;
        var addressHex = $"0x{candidate.Address:X}";
        var region = state.Region ?? FindRegion(snapshot, candidate.Address);
        if (region is null)
        {
            state.Warnings.Add($"Contexte impossible : aucune region lisible pour {addressHex}.");
            log("ContextError", candidate, $"Contexte impossible : aucune region lisible pour {addressHex}.");
            return;
        }

        var regionEnd = SafeRegionEnd(region);
        var start = candidate.Address > region.BaseAddress
            ? candidate.Address - Math.Min((ulong)options.ContextBeforeBytes, candidate.Address - region.BaseAddress)
            : region.BaseAddress;
        var requestedEnd = SafeAdd(candidate.Address, (ulong)sizeof(int) + (ulong)options.ContextAfterBytes);
        var end = Math.Min(regionEnd, requestedEnd);

        if (end <= start)
        {
            state.Warnings.Add($"Contexte vide autour de {addressHex}.");
            log("ContextError", candidate, $"Contexte vide autour de {addressHex}.");
            return;
        }

        var count = (int)Math.Min((ulong)int.MaxValue, end - start);
        state.ContextStartAddress = start;

        if (!reader.TryReadBytes(start, count, out var bytes, out var warning))
        {
            state.Warnings.Add(warning ?? $"Lecture contexte impossible autour de {addressHex}.");
            log("ContextError", candidate, warning ?? $"Lecture contexte impossible autour de {addressHex}.");
            return;
        }

        state.ContextBytes = bytes;
        log("Context", candidate, $"Contexte lu pour {addressHex}: start 0x{start:X}, {bytes.Length} octets.");
    }

    private static MemoryRegionInfo? FindRegion(MemoryMapSnapshot snapshot, ulong address)
    {
        return snapshot.Regions.FirstOrDefault(region =>
            region.IsReadable
            && address >= region.BaseAddress
            && address < SafeRegionEnd(region));
    }

    private static ulong SafeRegionEnd(MemoryRegionInfo region)
    {
        return ulong.MaxValue - region.BaseAddress < region.Size
            ? ulong.MaxValue
            : region.BaseAddress + region.Size;
    }

    private static ulong SafeAdd(ulong value, ulong delta)
    {
        return ulong.MaxValue - value < delta ? ulong.MaxValue : value + delta;
    }

    private static int ComputeSampleCount(TreasuryDiscriminatorOptions options)
    {
        var rawCount = (int)Math.Ceiling(options.ObservationDuration.TotalMilliseconds / options.PollInterval.TotalMilliseconds) + 1;
        return Math.Clamp(rawCount, 2, 10_000);
    }

    private static void ValidateInput(
        KnownValueScanSession session,
        IReadOnlyList<Candidate> candidates,
        TreasuryDiscriminatorOptions options)
    {
        if (session.ValueHistory.Count <= 1)
        {
            throw new InvalidOperationException("Le departage demande au moins un refine : le scan initial seul ne suffit pas.");
        }

        if (candidates.Count < 2)
        {
            throw new InvalidOperationException("Le departage sert uniquement quand plusieurs candidats restent.");
        }

        if (candidates.Count > options.MaxCandidates)
        {
            throw new InvalidOperationException($"Trop de candidats pour un departage direct : {candidates.Count}/{options.MaxCandidates}.");
        }
    }

    private static void ValidateOptions(TreasuryDiscriminatorOptions options)
    {
        if (options.ObservationDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "La duree d'observation doit etre positive.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "L'intervalle de lecture doit etre positif.");
        }

        if (options.ContextBeforeBytes < 0 || options.ContextAfterBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Le contexte autour du candidat ne peut pas etre negatif.");
        }

        if (options.MaxCandidates < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Le departage demande au moins 2 candidats maximum autorises.");
        }
    }

    private static void Add(ref double score, double delta, List<string> reasons, string reason)
    {
        score += delta;
        reasons.Add($"{delta:+0.##;-0.##;0} {reason}");
    }

    private sealed class CandidateObservationState
    {
        public CandidateObservationState(Candidate candidate, MemoryRegionInfo? region)
        {
            Candidate = candidate;
            Region = region;
        }

        public Candidate Candidate { get; }

        public MemoryRegionInfo? Region { get; }

        public List<int> Values { get; } = new();

        public List<int> ChangeSamples { get; } = new();

        public List<string> Warnings { get; } = new();

        public int SuccessfulReads { get; set; }

        public int FailedReads { get; set; }

        public int ChangeCount { get; set; }

        public int? FirstChangeSampleIndex { get; set; }

        public DateTimeOffset? FirstChangeAt { get; set; }

        public ulong ContextStartAddress { get; set; }

        public byte[] ContextBytes { get; set; } = Array.Empty<byte>();
    }
}
