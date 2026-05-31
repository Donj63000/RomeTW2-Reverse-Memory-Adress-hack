using System.Buffers.Binary;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Tests;

public sealed class KnownValueScannerTests
{
    [Fact]
    public void StartExactInt32Scan_FindsKnownAlignedValue()
    {
        var region = CreateRegion(0x1000, 0x100);
        var bytes = new byte[0x100];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 4500);
        var reader = new FakeProcessMemoryReader((region.BaseAddress, bytes));
        var snapshot = CreateSnapshot(region);

        var session = new KnownValueScanner().StartExactInt32Scan(snapshot, reader, 4500);

        Assert.Single(session.Candidates);
        Assert.Equal(0x1008UL, session.Candidates[0].Address);
        Assert.Equal("4500", session.Candidates[0].ObservedValue);
    }

    [Fact]
    public void StartExactInt32Scan_IgnoresUnalignedInt32Value()
    {
        var region = CreateRegion(0x2000, 0x100);
        var bytes = new byte[0x100];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), 4500);
        var reader = new FakeProcessMemoryReader((region.BaseAddress, bytes));
        var snapshot = CreateSnapshot(region);

        var session = new KnownValueScanner().StartExactInt32Scan(snapshot, reader, 4500);

        Assert.Empty(session.Candidates);
        Assert.Contains(session.Warnings, warning => warning.Contains("Aucun candidat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartExactInt32Scan_FiltersUnsupportedRegions()
    {
        var supported = CreateRegion(0x3000, 0x100);
        var notWritable = supported with { BaseAddress = 0x4000, IsWritable = false };
        var executable = supported with { BaseAddress = 0x5000, IsExecutable = true };
        var notCommitted = supported with { BaseAddress = 0x6000, State = "MEM_RESERVE" };

        var supportedBytes = new byte[0x100];
        var skippedBytes = new byte[0x100];
        BinaryPrimitives.WriteInt32LittleEndian(supportedBytes.AsSpan(4), 777);
        BinaryPrimitives.WriteInt32LittleEndian(skippedBytes.AsSpan(4), 777);

        var reader = new FakeProcessMemoryReader(
            (supported.BaseAddress, supportedBytes),
            (notWritable.BaseAddress, skippedBytes),
            (executable.BaseAddress, skippedBytes),
            (notCommitted.BaseAddress, skippedBytes));
        var snapshot = CreateSnapshot(supported, notWritable, executable, notCommitted);

        var session = new KnownValueScanner().StartExactInt32Scan(snapshot, reader, 777);

        Assert.Single(session.Candidates);
        Assert.Equal(supported.BaseAddress + 4, session.Candidates[0].Address);
        Assert.Equal(3, session.Counters.RegionsSkipped);
    }

    [Fact]
    public void RefineExactInt32Scan_KeepsOnlyCandidatesWithNewValue()
    {
        var region = CreateRegion(0x7000, 0x100);
        var bytes = new byte[0x100];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 4500);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 4500);
        var reader = new FakeProcessMemoryReader((region.BaseAddress, bytes));
        var snapshot = CreateSnapshot(region);
        var scanner = new KnownValueScanner();

        var initial = scanner.StartExactInt32Scan(snapshot, reader, 4500);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 4200);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1111);

        var refined = scanner.RefineExactInt32Scan(initial, reader, 4200);

        Assert.Single(refined.Candidates);
        Assert.Equal(region.BaseAddress + 4, refined.Candidates[0].Address);
        Assert.Equal(new[] { 4500, 4200 }, refined.ValueHistory);
        Assert.True(refined.Candidates[0].Confidence > initial.Candidates[0].Confidence);
    }

    [Fact]
    public void StartExactInt32Scan_AddsWarningWhenReadFails()
    {
        var region = CreateRegion(0x8000, 0x100);
        var reader = new FakeProcessMemoryReader((region.BaseAddress, new byte[0x100]));
        reader.FailReadsAt(region.BaseAddress);
        var snapshot = CreateSnapshot(region);

        var session = new KnownValueScanner().StartExactInt32Scan(snapshot, reader, 123);

        Assert.Empty(session.Candidates);
        Assert.Equal(1, session.Counters.ReadFailures);
        Assert.Contains(session.Warnings, warning => warning.Contains("Lecture forcee en echec", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartExactInt32Scan_StopsAtCandidateLimitAndWarns()
    {
        var region = CreateRegion(0x9000, 0x100);
        var bytes = new byte[0x100];
        for (var offset = 0; offset < 32; offset += 4)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), 99);
        }

        var reader = new FakeProcessMemoryReader((region.BaseAddress, bytes));
        var snapshot = CreateSnapshot(region);
        var options = new KnownValueScanOptions(ChunkSizeBytes: 0x100, MaxCandidates: 2);

        var session = new KnownValueScanner().StartExactInt32Scan(snapshot, reader, 99, options);

        Assert.Equal(2, session.Candidates.Count);
        Assert.Contains(session.Warnings, warning => warning.Contains("Limite de 2 candidats", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartExactInt32Scan_RejectsUnalignedChunkSize()
    {
        var region = CreateRegion(0xA000, 0x100);
        var reader = new FakeProcessMemoryReader((region.BaseAddress, new byte[0x100]));
        var snapshot = CreateSnapshot(region);
        var options = new KnownValueScanOptions(ChunkSizeBytes: 7, MaxCandidates: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KnownValueScanner().StartExactInt32Scan(snapshot, reader, 1, options));
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

    private sealed class FakeProcessMemoryReader : IProcessMemoryReader
    {
        private readonly Dictionary<ulong, byte[]> _regions;
        private readonly HashSet<ulong> _forcedFailures = new();

        public FakeProcessMemoryReader(params (ulong BaseAddress, byte[] Bytes)[] regions)
        {
            _regions = regions.ToDictionary(region => region.BaseAddress, region => region.Bytes);
        }

        public void FailReadsAt(ulong address)
        {
            _forcedFailures.Add(address);
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

            if (_forcedFailures.Contains(address))
            {
                warning = $"Lecture forcee en echec a 0x{address:X}.";
                return false;
            }

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
