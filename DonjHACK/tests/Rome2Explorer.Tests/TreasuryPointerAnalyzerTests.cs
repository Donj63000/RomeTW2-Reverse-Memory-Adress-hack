using System.Buffers.Binary;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Tests;

public sealed class TreasuryPointerAnalyzerTests
{
    [Fact]
    public void Analyze_FindsDirectPointerToTreasuryCandidate()
    {
        var candidate = CreateCandidate(0x1010);
        var candidateRegion = CreateRegion(0x1000, 0x1000);
        var pointerRegion = CreateRegion(0x5000, 0x100);
        var reader = new FakeReader()
            .AddRegion(candidateRegion, new byte[0x1000])
            .AddRegion(pointerRegion, BytesWithUInt32(0x100, 0x20, 0x1010));
        var snapshot = CreateSnapshot(candidateRegion, pointerRegion);
        var session = CreateSession(candidate);

        var result = new TreasuryPointerAnalyzer().Analyze(session, candidate, reader, snapshot);

        Assert.Equal(TreasuryPointerAnalysisVerdicts.RawCandidateOnly, result.OverallVerdict);
        Assert.Contains(result.PointerHits, hit => hit.TargetAddress == candidate.Address && hit.TargetKind == "TreasuryValue");
    }

    [Fact]
    public void Analyze_FindsPointerToProbableStructureBase()
    {
        var candidate = CreateCandidate(0x1120, confidence: 0.80);
        var candidateRegion = CreateRegion(0x1000, 0x1000);
        var candidateBytes = new byte[0x1000];
        candidateBytes[0x120] = 0x2A;
        var pointerRegion = CreateRegion(0x5000, 0x100);
        var reader = new FakeReader()
            .AddRegion(candidateRegion, candidateBytes)
            .AddRegion(pointerRegion, BytesWithUInt32(0x100, 0x20, 0x1000));
        var snapshot = CreateSnapshot(candidateRegion, pointerRegion);
        var session = CreateSession(candidate);

        var result = new TreasuryPointerAnalyzer().Analyze(session, candidate, reader, snapshot);

        Assert.Equal(TreasuryPointerAnalysisVerdicts.ProbableStructure, result.OverallVerdict);
        Assert.NotNull(result.BestStructureBase);
        Assert.Equal(0x1000UL, result.BestStructureBase!.BaseAddress);
        Assert.Equal(0x120, result.BestStructureBase.TreasuryOffset);
        Assert.Contains(result.PointerHits, hit => hit.TargetAddress == 0x1000UL && hit.TargetKind == "StructureBase");
    }

    [Fact]
    public void Analyze_RespectsFourByteAlignment()
    {
        var candidate = CreateCandidate(0x2010);
        var candidateRegion = CreateRegion(0x2000, 0x1000);
        var pointerRegion = CreateRegion(0x6000, 0x100);
        var bytes = BytesWithUInt32(0x100, 0x02, 0x2010);
        var reader = new FakeReader()
            .AddRegion(candidateRegion, new byte[0x1000])
            .AddRegion(pointerRegion, bytes);
        var snapshot = CreateSnapshot(candidateRegion, pointerRegion);
        var session = CreateSession(candidate);

        var result = new TreasuryPointerAnalyzer().Analyze(session, candidate, reader, snapshot);

        Assert.Equal(TreasuryPointerAnalysisVerdicts.NoPointerFound, result.OverallVerdict);
        Assert.Empty(result.PointerHits);
    }

    [Fact]
    public void Analyze_IgnoresExecutableRegions()
    {
        var candidate = CreateCandidate(0x3010);
        var candidateRegion = CreateRegion(0x3000, 0x1000);
        var executableRegion = CreateRegion(0x7000, 0x100) with
        {
            Protection = "PAGE_EXECUTE_READ",
            IsWritable = false,
            IsExecutable = true
        };
        var reader = new FakeReader()
            .AddRegion(candidateRegion, new byte[0x1000])
            .AddRegion(executableRegion, BytesWithUInt32(0x100, 0x20, 0x3010));
        var snapshot = CreateSnapshot(candidateRegion, executableRegion);
        var session = CreateSession(candidate);

        var result = new TreasuryPointerAnalyzer().Analyze(session, candidate, reader, snapshot);

        Assert.Equal(TreasuryPointerAnalysisVerdicts.NoPointerFound, result.OverallVerdict);
        Assert.Empty(result.PointerHits);
    }

    [Fact]
    public void Analyze_StopsWhenPointerHitLimitIsReached()
    {
        var candidate = CreateCandidate(0x4010);
        var candidateRegion = CreateRegion(0x4000, 0x1000);
        var pointerRegion = CreateRegion(0x8000, 0x100);
        var bytes = new byte[0x100];
        for (var offset = 0; offset < 0x40; offset += 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0x4010);
        }

        var reader = new FakeReader()
            .AddRegion(candidateRegion, new byte[0x1000])
            .AddRegion(pointerRegion, bytes);
        var snapshot = CreateSnapshot(candidateRegion, pointerRegion);
        var session = CreateSession(candidate);
        var options = TreasuryPointerAnalysisOptions.Default with { MaxPointerHits = 3 };

        var result = new TreasuryPointerAnalyzer().Analyze(session, candidate, reader, snapshot, options);

        Assert.True(result.HitLimitReached);
        Assert.True(result.PointerHits.Count <= 3);
        Assert.Contains(result.Warnings, warning => warning.Contains("Limite de 3 pointeurs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ClassifiesEquivalentStructureBasesAsAmbiguous()
    {
        var candidate = CreateCandidate(0x1100, confidence: 0.80);
        var candidateRegion = CreateRegion(0x1000, 0x1000);
        var candidateBytes = new byte[0x1000];
        candidateBytes[0x100] = 0x2A;
        var pointerRegion = CreateRegion(0x9000, 0x100);
        var pointerBytes = new byte[0x100];
        BinaryPrimitives.WriteUInt32LittleEndian(pointerBytes.AsSpan(0x20), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(pointerBytes.AsSpan(0x24), 0x1080);
        var reader = new FakeReader()
            .AddRegion(candidateRegion, candidateBytes)
            .AddRegion(pointerRegion, pointerBytes);
        var snapshot = CreateSnapshot(candidateRegion, pointerRegion);
        var session = CreateSession(candidate);

        var result = new TreasuryPointerAnalyzer().Analyze(session, candidate, reader, snapshot);

        Assert.Equal(TreasuryPointerAnalysisVerdicts.AmbiguousStructure, result.OverallVerdict);
        Assert.True(result.StructureBases.Count >= 2);
    }

    private static KnownValueScanSession CreateSession(Candidate candidate)
    {
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: DateTimeOffset.Now,
            UpdatedAt: DateTimeOffset.Now,
            ValueHistory: new[] { 4500, 4200 },
            Candidates: new[] { candidate },
            Counters: new KnownValueScanCounters(1, 1, 0, 4, 0, 1, 1),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(ulong address, double confidence = 0.65)
    {
        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: $"treasury-int32-0x{address:X8}",
            Address: address,
            Type: "Int32",
            ObservedValue: "4200",
            ExpectedValue: "4200",
            Region: CreateRegion(address & 0xFFFFF000, 0x1000),
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: new[] { "test candidate evidence" },
            Warnings: Array.Empty<string>(),
            Confidence: confidence);
    }

    private static MemoryRegionInfo CreateRegion(ulong baseAddress, ulong size)
    {
        return new MemoryRegionInfo(
            BaseAddress: baseAddress,
            Size: size,
            State: "MEM_COMMIT",
            Protection: "PAGE_READWRITE",
            Type: "MEM_PRIVATE",
            IsReadable: true,
            IsWritable: true,
            IsExecutable: false);
    }

    private static MemoryMapSnapshot CreateSnapshot(params MemoryRegionInfo[] regions)
    {
        return new MemoryMapSnapshot(
            CreatedAt: DateTimeOffset.Now,
            Process: new Rome2ProcessInfo(123, "Rome2", @"C:\Rome2.exe", ProcessArchitecture.X86, DateTimeOffset.Now, "Rome2"),
            Modules: Array.Empty<ModuleInfo>(),
            Regions: regions,
            Summary: new MemoryMapSummary(0, regions.Length, 0, 0, 0));
    }

    private static byte[] BytesWithUInt32(int size, int offset, uint value)
    {
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        return bytes;
    }

    private sealed class FakeReader : IProcessMemoryReader
    {
        private readonly Dictionary<ulong, byte[]> _regions = new();

        public FakeReader AddRegion(MemoryRegionInfo region, byte[] bytes)
        {
            _regions[region.BaseAddress] = bytes;
            return this;
        }

        public bool TryReadBytes(ulong address, int count, out byte[] bytes, out string? warning)
        {
            bytes = Array.Empty<byte>();
            warning = null;

            foreach (var (baseAddress, regionBytes) in _regions)
            {
                if (address < baseAddress || address + (ulong)count > baseAddress + (ulong)regionBytes.Length)
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
            if (!TryReadBytes(address, sizeof(int), out var bytes, out warning))
            {
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return true;
        }

        public byte[] ReadBytes(ulong address, int count)
        {
            if (!TryReadBytes(address, count, out var bytes, out var warning))
            {
                throw new InvalidOperationException(warning);
            }

            return bytes;
        }

        public Rome2ProcessInfo AttachToRome2() => throw new NotSupportedException();

        public Rome2ProcessInfo Attach(int processId) => throw new NotSupportedException();

        public IReadOnlyList<ModuleInfo> EnumerateModules() => Array.Empty<ModuleInfo>();

        public IReadOnlyList<MemoryRegionInfo> EnumerateRegions() => Array.Empty<MemoryRegionInfo>();

        public MemoryMapSnapshot CaptureMemoryMap() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
