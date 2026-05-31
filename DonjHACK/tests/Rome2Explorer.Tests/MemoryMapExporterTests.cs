using System.Text.Json;
using System.Text.Json.Serialization;
using Rome2Explorer.Domain;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class MemoryMapExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Export_WritesMemoryMapJsonUnderEvidenceDirectory()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "test-output", Guid.NewGuid().ToString("N"));
        var snapshot = CreateSnapshot();

        var exportPath = new MemoryMapExporter().Export(snapshot, outputRoot);

        Assert.True(File.Exists(exportPath));
        Assert.EndsWith(".json", exportPath);
        Assert.Contains(Path.Combine("evidence", "memory-maps"), exportPath);

        var json = File.ReadAllText(exportPath);
        var envelope = JsonSerializer.Deserialize<MemoryMapExportEnvelope>(json, JsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal(snapshot.Process.ProcessId, envelope.Snapshot.Process.ProcessId);
        Assert.Equal(ProcessArchitecture.X64, envelope.Snapshot.Process.Architecture);
        Assert.Single(envelope.Snapshot.Modules);
        Assert.Single(envelope.Snapshot.Regions);
        Assert.Equal(1, envelope.Counters.ModuleCount);
        Assert.Equal(1, envelope.Counters.RegionCount);
        Assert.Equal(envelope.Snapshot.Summary.TotalReadableBytes, envelope.Counters.TotalReadableBytes);
    }

    [Fact]
    public void Export_DoesNotOverwriteWhenTwoExportsShareTheSameSecond()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "test-output", Guid.NewGuid().ToString("N"));
        var snapshot = CreateSnapshot();
        var fixedClock = new DateTimeOffset(2026, 5, 31, 2, 30, 0, TimeSpan.Zero);
        var exporter = new MemoryMapExporter(() => fixedClock);

        var firstPath = exporter.Export(snapshot, outputRoot);
        var secondPath = exporter.Export(snapshot, outputRoot);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.NotEqual(firstPath, secondPath);
        Assert.EndsWith("memory-map-20260531-023000.json", firstPath);
        Assert.EndsWith("memory-map-20260531-023000-001.json", secondPath);
    }

    [Fact]
    public void ResolveRoot_FallsBackToDonjHackChildDirectory()
    {
        var startDirectory = Path.Combine(Path.GetTempPath(), "DonjHACK-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(startDirectory);

        var root = DonjHackPathResolver.ResolveRoot(startDirectory);

        Assert.Equal(Path.Combine(startDirectory, "DonjHACK"), root);
    }

    private static MemoryMapSnapshot CreateSnapshot()
    {
        var process = new Rome2ProcessInfo(
            ProcessId: 1234,
            Name: "Rome2",
            Path: @"C:\Games\Total War - Rome 2\Rome2.exe",
            Architecture: ProcessArchitecture.X64,
            StartTime: new DateTimeOffset(2026, 5, 31, 1, 0, 0, TimeSpan.Zero),
            MainWindowTitle: "Total War: ROME 2");

        var module = new ModuleInfo(
            Name: "Rome2.exe",
            BaseAddress: 0x140000000,
            Size: 0x1000,
            Path: @"C:\Games\Total War - Rome 2\Rome2.exe");

        var region = new MemoryRegionInfo(
            BaseAddress: 0x140000000,
            Size: 0x1000,
            State: "MEM_COMMIT",
            Protection: "PAGE_READONLY",
            Type: "MEM_IMAGE",
            IsReadable: true,
            IsWritable: false,
            IsExecutable: false);

        return new MemoryMapSnapshot(
            CreatedAt: new DateTimeOffset(2026, 5, 31, 1, 1, 0, TimeSpan.Zero),
            Process: process,
            Modules: new[] { module },
            Regions: new[] { region },
            Summary: new MemoryMapSummary(
                ModuleCount: 1,
                RegionCount: 1,
                TotalReadableBytes: 0x1000,
                TotalWritableBytes: 0,
                TotalExecutableBytes: 0));
    }
}
