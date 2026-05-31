using System.Buffers.Binary;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Tests;

public sealed class TreasuryMoneyWriterTests
{
    [Fact]
    public async Task WriteSelectedCandidateAsync_RefusesWithoutSelectedCandidate()
    {
        var accessor = new FakeMemoryAccessor();
        var session = CreateSession(CreateCandidate(0x1004));

        var result = await new TreasuryMoneyWriter().WriteSelectedCandidateAsync(
            session,
            candidate: null,
            accessor,
            accessor,
            desiredValue: 150000,
            stabilityDelay: TimeSpan.Zero);

        Assert.False(result.Success);
        Assert.Equal(TreasuryWriteStatuses.Refused, result.Status);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task WriteSelectedCandidateAsync_RefusesWhenCurrentMemoryValueDoesNotMatchSession()
    {
        var candidate = CreateCandidate(0x2004);
        var session = CreateSession(candidate, currentValue: 4500);
        var accessor = new FakeMemoryAccessor()
            .SetInt32(candidate.Address, 4300);

        var result = await new TreasuryMoneyWriter().WriteSelectedCandidateAsync(
            session,
            candidate,
            accessor,
            accessor,
            desiredValue: 150000,
            stabilityDelay: TimeSpan.Zero);

        Assert.False(result.Success);
        Assert.Equal(TreasuryWriteStatuses.Refused, result.Status);
        Assert.Contains("Refais un refine", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task WriteSelectedCandidateAsync_WritesOneInt32AndVerifiesByReadingBack()
    {
        var candidate = CreateCandidate(0x3004);
        var session = CreateSession(candidate, currentValue: 4500);
        var accessor = new FakeMemoryAccessor()
            .SetInt32(candidate.Address, 4500);

        var result = await new TreasuryMoneyWriter().WriteSelectedCandidateAsync(
            session,
            candidate,
            accessor,
            accessor,
            desiredValue: 150000,
            stabilityDelay: TimeSpan.Zero);

        Assert.True(result.Success);
        Assert.Equal(TreasuryWriteStatuses.Success, result.Status);
        Assert.Equal(1, accessor.WriteCount);
        Assert.Equal(150000, accessor.GetInt32(candidate.Address));
        Assert.Equal(4500, result.ValueBefore);
        Assert.Equal(150000, result.ValueAfterWrite);
        Assert.Equal(150000, result.ValueAfterStabilityDelay);
        Assert.True(accessor.ReadCount(candidate.Address) >= 3);
    }

    [Fact]
    public async Task WriteSelectedCandidateAsync_WithAmbiguousSessionWritesOnlySelectedAddress()
    {
        var selected = CreateCandidate(0x4004);
        var other = CreateCandidate(0x5004);
        var session = CreateSession(new[] { selected, other }, currentValue: 4500);
        var accessor = new FakeMemoryAccessor()
            .SetInt32(selected.Address, 4500)
            .SetInt32(other.Address, 4500);

        var result = await new TreasuryMoneyWriter().WriteSelectedCandidateAsync(
            session,
            selected,
            accessor,
            accessor,
            desiredValue: 150000,
            stabilityDelay: TimeSpan.Zero);

        Assert.True(result.Success);
        Assert.True(result.IsAmbiguousSelection);
        Assert.Equal(new[] { selected.Address }, accessor.WriteAddresses);
        Assert.Equal(150000, accessor.GetInt32(selected.Address));
        Assert.Equal(4500, accessor.GetInt32(other.Address));
        Assert.Contains(result.Warnings, warning => warning.Contains("Plusieurs candidats", StringComparison.OrdinalIgnoreCase));
    }

    private static KnownValueScanSession CreateSession(Candidate candidate, int currentValue = 4500)
    {
        return CreateSession(new[] { candidate }, currentValue);
    }

    private static KnownValueScanSession CreateSession(IReadOnlyList<Candidate> candidates, int currentValue = 4500)
    {
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: DateTimeOffset.Now,
            UpdatedAt: DateTimeOffset.Now,
            ValueHistory: new[] { 5000, currentValue },
            Candidates: candidates,
            Counters: new KnownValueScanCounters(1, 1, 0, 4, 0, candidates.Count, candidates.Count),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(ulong address)
    {
        var region = new MemoryRegionInfo(
            BaseAddress: address & 0xFFFFF000,
            Size: 0x1000,
            State: "MEM_COMMIT",
            Protection: "PAGE_READWRITE",
            Type: "MEM_PRIVATE",
            IsReadable: true,
            IsWritable: true,
            IsExecutable: false);

        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: $"treasury-int32-0x{address:X8}",
            Address: address,
            Type: "Int32",
            ObservedValue: "4500",
            ExpectedValue: "4500",
            Region: region,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: new[] { "test candidate evidence" },
            Warnings: Array.Empty<string>(),
            Confidence: 0.65);
    }

    private sealed class FakeMemoryAccessor : IProcessMemoryReader, IProcessMemoryWriter
    {
        private readonly Dictionary<ulong, int> _values = new();
        private readonly Dictionary<ulong, int> _readCounts = new();
        private readonly List<ulong> _writeAddresses = new();

        public bool HasWriteAccess { get; private set; }

        public int WriteCount => _writeAddresses.Count;

        public IReadOnlyList<ulong> WriteAddresses => _writeAddresses;

        public FakeMemoryAccessor SetInt32(ulong address, int value)
        {
            _values[address] = value;
            return this;
        }

        public int GetInt32(ulong address)
        {
            return _values[address];
        }

        public int ReadCount(ulong address)
        {
            return _readCounts.TryGetValue(address, out var count) ? count : 0;
        }

        public bool TryEnableWriteAccess(out string? warning)
        {
            warning = null;
            HasWriteAccess = true;
            return true;
        }

        public bool TryWriteBytes(ulong address, byte[] bytes, out string? warning)
        {
            warning = null;
            if (bytes.Length != sizeof(int))
            {
                warning = "Le fake writer accepte seulement Int32.";
                return false;
            }

            return TryWriteInt32(address, BinaryPrimitives.ReadInt32LittleEndian(bytes), out warning);
        }

        public bool TryWriteInt32(ulong address, int value, out string? warning)
        {
            warning = null;
            TryEnableWriteAccess(out _);
            _values[address] = value;
            _writeAddresses.Add(address);
            return true;
        }

        public bool TryReadInt32(ulong address, out int value, out string? warning)
        {
            warning = null;
            value = 0;
            _readCounts[address] = ReadCount(address) + 1;
            if (!_values.TryGetValue(address, out value))
            {
                warning = $"Adresse 0x{address:X} absente du fake.";
                return false;
            }

            return true;
        }

        public bool TryReadBytes(ulong address, int count, out byte[] bytes, out string? warning)
        {
            bytes = Array.Empty<byte>();
            warning = null;
            if (count != sizeof(int) || !TryReadInt32(address, out var value, out warning))
            {
                return false;
            }

            bytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
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
