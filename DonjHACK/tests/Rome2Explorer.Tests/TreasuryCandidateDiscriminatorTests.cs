using System.Buffers.Binary;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Tests;

public sealed class TreasuryCandidateDiscriminatorTests
{
    private static readonly TreasuryDiscriminatorOptions FastOptions = new(
        ObservationDuration: TimeSpan.FromMilliseconds(3),
        PollInterval: TimeSpan.FromMilliseconds(1),
        ContextBeforeBytes: 128,
        ContextAfterBytes: 128,
        MaxCandidates: 10);

    [Fact]
    public async Task RunAsync_KeepsSynchronizedCandidatesAmbiguous()
    {
        var first = CreateCandidate(0x1010);
        var second = CreateCandidate(0x1110);
        var reader = new ScriptedMemoryReader()
            .AddRegion(0x1000, CreateBytes(0x200))
            .SetInt32Script(first.Address, 100, 100, 90, 90)
            .SetInt32Script(second.Address, 100, 100, 90, 90);
        var session = CreateSession(first, second);
        var snapshot = CreateSnapshot(first.Region!, second.Region!);

        var result = await new TreasuryCandidateDiscriminator().RunAsync(session, session.Candidates, reader, snapshot, FastOptions);

        Assert.True(result.IsAmbiguous);
        Assert.Null(result.FavoriteCandidateId);
        Assert.Contains("Toujours ambigu", result.OverallVerdict, StringComparison.OrdinalIgnoreCase);
        Assert.All(result.Candidates, candidate => Assert.False(candidate.IsFavorite));
    }

    [Fact]
    public async Task RunAsync_MarksCandidateChangingFirstAsFavorite()
    {
        var first = CreateCandidate(0x2010);
        var second = CreateCandidate(0x2110);
        var reader = new ScriptedMemoryReader()
            .AddRegion(0x2000, CreateBytes(0x200))
            .SetInt32Script(first.Address, 100, 90, 90, 90)
            .SetInt32Script(second.Address, 100, 100, 90, 90);
        var session = CreateSession(first, second);
        var snapshot = CreateSnapshot(first.Region!, second.Region!);

        var result = await new TreasuryCandidateDiscriminator().RunAsync(session, session.Candidates, reader, snapshot, FastOptions);

        Assert.False(result.IsAmbiguous);
        Assert.Equal(first.CandidateId, result.FavoriteCandidateId);
        Assert.Equal("Change en premier", result.Candidates.Single(candidate => candidate.CandidateId == first.CandidateId).Verdict);
    }

    [Fact]
    public async Task RunAsync_PenalizesUnreadableCandidateWithoutThrowing()
    {
        var readable = CreateCandidate(0x3010);
        var unreadable = CreateCandidate(0x3110);
        var reader = new ScriptedMemoryReader()
            .AddRegion(0x3000, CreateBytes(0x200))
            .SetInt32Script(readable.Address, 100, 100, 100, 100)
            .FailInt32(unreadable.Address);
        var session = CreateSession(readable, unreadable);
        var snapshot = CreateSnapshot(readable.Region!, unreadable.Region!);

        var result = await new TreasuryCandidateDiscriminator().RunAsync(session, session.Candidates, reader, snapshot, FastOptions);

        var failed = result.Candidates.Single(candidate => candidate.CandidateId == unreadable.CandidateId);
        Assert.Equal("Lecture echouee", failed.Verdict);
        Assert.True(failed.FailedReads > 0);
        Assert.NotEmpty(result.Log);
        Assert.Contains(result.Log, entry => entry.EventType == "ReadError");
    }

    [Fact]
    public async Task RunAsync_BoundsContextReadInsideRegion()
    {
        var candidate = CreateCandidate(0x4004);
        var other = CreateCandidate(0x4080);
        var reader = new ScriptedMemoryReader()
            .AddRegion(0x4000, CreateBytes(0x100))
            .SetInt32Script(candidate.Address, 100, 80, 80, 80)
            .SetInt32Script(other.Address, 100, 100, 80, 80);
        var session = CreateSession(candidate, other);
        var snapshot = CreateSnapshot(candidate.Region!, other.Region!);

        var result = await new TreasuryCandidateDiscriminator().RunAsync(session, session.Candidates, reader, snapshot, FastOptions);

        var analyzed = result.Candidates.Single(item => item.CandidateId == candidate.CandidateId);
        Assert.Equal(0x4000UL, analyzed.ContextStartAddress);
        Assert.InRange(analyzed.ContextByteCount, 1, 0x100);
        Assert.Contains(result.Log, entry => entry.EventType == "Context" && entry.AddressHex == "0x4004");
    }

    [Fact]
    public void CanRun_RequiresSeveralCandidatesAfterRefine()
    {
        var first = CreateCandidate(0x5010);
        var second = CreateCandidate(0x5110);

        Assert.False(TreasuryCandidateDiscriminator.CanRun(null));
        Assert.False(TreasuryCandidateDiscriminator.CanRun(CreateSession(new[] { 100 }, first, second)));
        Assert.False(TreasuryCandidateDiscriminator.CanRun(CreateSession(new[] { 100, 90 }, first)));
        Assert.True(TreasuryCandidateDiscriminator.CanRun(CreateSession(new[] { 100, 90 }, first, second)));
    }

    private static KnownValueScanSession CreateSession(params Candidate[] candidates)
    {
        return CreateSession(new[] { 100, 90 }, candidates);
    }

    private static KnownValueScanSession CreateSession(IReadOnlyList<int> history, params Candidate[] candidates)
    {
        var now = DateTimeOffset.Now;
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: now,
            UpdatedAt: now,
            ValueHistory: history,
            Candidates: candidates,
            Counters: new KnownValueScanCounters(1, 1, 0, 4, 0, candidates.Length, candidates.Length),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(ulong address)
    {
        var regionBase = address & 0xFFFFF000;
        var region = new MemoryRegionInfo(regionBase, 0x1000, "MEM_COMMIT", "PAGE_READWRITE", "MEM_PRIVATE", true, true, false);
        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: $"treasury-int32-0x{address:X8}",
            Address: address,
            Type: "Int32",
            ObservedValue: "90",
            ExpectedValue: "90",
            Region: region,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Confidence: 0.65);
    }

    private static MemoryMapSnapshot CreateSnapshot(params MemoryRegionInfo[] regions)
    {
        return new MemoryMapSnapshot(
            CreatedAt: DateTimeOffset.Now,
            Process: new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2"),
            Modules: Array.Empty<ModuleInfo>(),
            Regions: regions.DistinctBy(region => region.BaseAddress).ToArray(),
            Summary: new MemoryMapSummary(0, regions.Length, 0, 0, 0));
    }

    private static byte[] CreateBytes(int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i % 251) + 1);
        }

        return bytes;
    }

    private sealed class ScriptedMemoryReader : IProcessMemoryReader
    {
        private readonly Dictionary<ulong, byte[]> _regions = new();
        private readonly Dictionary<ulong, Queue<int>> _scripts = new();
        private readonly HashSet<ulong> _failedInt32Reads = new();

        public ScriptedMemoryReader AddRegion(ulong baseAddress, byte[] bytes)
        {
            _regions[baseAddress] = bytes;
            return this;
        }

        public ScriptedMemoryReader SetInt32Script(ulong address, params int[] values)
        {
            _scripts[address] = new Queue<int>(values);
            return this;
        }

        public ScriptedMemoryReader FailInt32(ulong address)
        {
            _failedInt32Reads.Add(address);
            return this;
        }

        public Rome2ProcessInfo AttachToRome2() => throw new NotSupportedException();

        public Rome2ProcessInfo Attach(int processId) => throw new NotSupportedException();

        public IReadOnlyList<ModuleInfo> EnumerateModules() => Array.Empty<ModuleInfo>();

        public IReadOnlyList<MemoryRegionInfo> EnumerateRegions() => Array.Empty<MemoryRegionInfo>();

        public MemoryMapSnapshot CaptureMemoryMap() => throw new NotSupportedException();

        public byte[] ReadBytes(ulong address, int count)
        {
            if (!TryReadBytes(address, count, out var bytes, out var warning))
            {
                throw new InvalidOperationException(warning);
            }

            return bytes;
        }

        public bool TryReadBytes(ulong address, int count, out byte[] bytes, out string? warning)
        {
            bytes = Array.Empty<byte>();
            warning = null;

            foreach (var (baseAddress, regionBytes) in _regions)
            {
                var endAddress = baseAddress + (ulong)regionBytes.Length;
                if (address < baseAddress || address + (ulong)count > endAddress)
                {
                    continue;
                }

                bytes = new byte[count];
                Buffer.BlockCopy(regionBytes, checked((int)(address - baseAddress)), bytes, 0, count);
                return true;
            }

            warning = $"Adresse 0x{address:X} hors fake memory.";
            return false;
        }

        public bool TryReadInt32(ulong address, out int value, out string? warning)
        {
            value = 0;
            warning = null;

            if (_failedInt32Reads.Contains(address))
            {
                warning = $"Lecture forcee en echec a 0x{address:X}.";
                return false;
            }

            if (_scripts.TryGetValue(address, out var queue) && queue.Count > 0)
            {
                value = queue.Count == 1 ? queue.Peek() : queue.Dequeue();
                return true;
            }

            if (!TryReadBytes(address, sizeof(int), out var bytes, out warning))
            {
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return true;
        }

        public void Dispose()
        {
        }
    }
}
