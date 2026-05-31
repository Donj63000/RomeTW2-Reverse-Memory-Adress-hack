using System.Text.Json;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class TreasuryStructureCaptureImporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void LoadCapture_AcceptsValidCapture()
    {
        var outputRoot = CreateOutputRoot();
        var path = WriteCapture(outputRoot, "valid.json", CreateCapture("before", 4500, CreateWindow(0x2000, 0, 4500)));

        var capture = new TreasuryStructureCaptureImporter().LoadCapture(path);

        Assert.Equal(KnownValueScanner.TreasuryFeatureId, capture.FeatureId);
        Assert.Single(capture.Candidates);
    }

    [Fact]
    public void ImportAndCompareTreasury_RejectsFeatureMismatch()
    {
        var outputRoot = CreateOutputRoot();
        var path = WriteCapture(outputRoot, "wrong-feature.json", CreateCapture("before", 4500, CreateWindow(0x2000, 0, 4500)) with
        {
            FeatureId = "campaign.other"
        });
        var session = CreateSession(CreateCandidate(0x2000));

        var error = Assert.Throws<InvalidDataException>(() =>
            new TreasuryStructureCaptureImporter().ImportAndCompareTreasury(session, new[] { path }, outputRoot));

        Assert.Contains("incompatible", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadCapture_RejectsInvalidCandidateAddress()
    {
        var outputRoot = CreateOutputRoot();
        var window = CreateWindow(0x2000, 0, 4500) with
        {
            Address = 0,
            AddressHex = "not-hex"
        };
        var path = WriteCapture(outputRoot, "bad-address.json", CreateCapture("before", 4500, window));

        var error = Assert.Throws<InvalidDataException>(() => new TreasuryStructureCaptureImporter().LoadCapture(path));

        Assert.Contains("adresse", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadCapture_RejectsEmptyWindow()
    {
        var outputRoot = CreateOutputRoot();
        var window = CreateWindow(0x2000, 0, 4500) with
        {
            ContextByteCount = 0,
            ContextBytesHex = string.Empty
        };
        var path = WriteCapture(outputRoot, "empty-window.json", CreateCapture("before", 4500, window));

        var error = Assert.Throws<InvalidDataException>(() => new TreasuryStructureCaptureImporter().LoadCapture(path));

        Assert.Contains("fenetre", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadCapture_RejectsIncompleteJson()
    {
        var outputRoot = CreateOutputRoot();
        var path = Path.Combine(outputRoot, "incomplete.json");
        File.WriteAllText(path, """{"featureId":"campaign.player_faction.treasury","candidates":[]}""");

        var error = Assert.Throws<InvalidDataException>(() => new TreasuryStructureCaptureImporter().LoadCapture(path));

        Assert.Contains("knownValues", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompareTreasury_KeepsNonContiguousOffsetThatFollowsKnownValues()
    {
        var session = CreateSession(CreateCandidate(0x2000));
        var captures = new[]
        {
            CreateCapture("before", 4500, CreateWindow(0x2000, 0x24, 4500)),
            CreateCapture("after", 4200, CreateWindow(0x2000, 0x24, 4200))
        };

        var report = new TreasuryStructureCaptureImporter(() => new DateTimeOffset(2026, 5, 31, 20, 0, 0, TimeSpan.Zero))
            .CompareTreasury(session, captures);

        var result = Assert.Single(report.Results);
        Assert.Equal("Probable", result.Status);
        Assert.Contains(result.FieldOffsets, field => field.RelativeOffset == 0x24 && field.Status == "FollowsKnownValues");
        Assert.Contains(result.Evidence, item => item.Contains("meilleur offset relatif 0x24", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompareTreasury_PenalizesSynchronizedFalsePositiveCandidates()
    {
        var first = CreateCandidate(0x2000);
        var second = CreateCandidate(0x3000);
        var session = CreateSession(first, second);
        var captures = new[]
        {
            CreateCapture("before", 4500, CreateWindow(first.Address, 0x24, 4500), CreateWindow(second.Address, 0x24, 4500)),
            CreateCapture("after", 4200, CreateWindow(first.Address, 0x24, 4200), CreateWindow(second.Address, 0x24, 4200))
        };

        var report = new TreasuryStructureCaptureImporter().CompareTreasury(session, captures);

        Assert.Equal("Ambiguous", report.OverallStatus);
        Assert.All(report.Results, result =>
        {
            Assert.Equal("Ambiguous", result.Status);
            Assert.Contains(result.Warnings, warning => warning.Contains("synchronise", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void ImportAndCompareTreasury_WritesEvidenceBundleFilesUniquely()
    {
        var outputRoot = CreateOutputRoot();
        var session = CreateSession(CreateCandidate(0x2000));
        var firstCapture = WriteCapture(outputRoot, "first.json", CreateCapture("before", 4500, CreateWindow(0x2000, 0x24, 4500)));
        var secondCapture = WriteCapture(outputRoot, "second.json", CreateCapture("after", 4200, CreateWindow(0x2000, 0x24, 4200)));
        var importer = new TreasuryStructureCaptureImporter(() => new DateTimeOffset(2026, 5, 31, 20, 15, 0, TimeSpan.Zero));

        var firstBundle = importer.ImportAndCompareTreasury(session, new[] { firstCapture, secondCapture }, outputRoot);
        var secondBundle = importer.ImportAndCompareTreasury(session, new[] { firstCapture, secondCapture }, outputRoot);

        Assert.True(File.Exists(firstBundle.ManifestPath));
        Assert.True(File.Exists(firstBundle.RankedCandidatesPath));
        Assert.True(File.Exists(firstBundle.ValidationReportPath));
        Assert.True(File.Exists(secondBundle.ManifestPath));
        Assert.NotEqual(firstBundle.BundleDirectory, secondBundle.BundleDirectory);
        Assert.EndsWith("manifest.json", firstBundle.ManifestPath);
        Assert.EndsWith("ranked-candidates.json", firstBundle.RankedCandidatesPath);
        Assert.EndsWith("validation-report.json", firstBundle.ValidationReportPath);
    }

    private static string CreateOutputRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "structure-capture-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteCapture(string directory, string fileName, LuaMemoryCaptureEnvelope capture)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(capture, JsonOptions));
        return path;
    }

    private static LuaMemoryCaptureEnvelope CreateCapture(string stepId, int uiTreasury, params CapturedCandidateWindow[] windows)
    {
        return new LuaMemoryCaptureEnvelope
        {
            CreatedAt = new DateTimeOffset(2026, 5, 31, 20, 0, 0, TimeSpan.Zero),
            Tool = "test",
            FeatureId = KnownValueScanner.TreasuryFeatureId,
            Process = new LuaCaptureProcessInfo
            {
                ProcessId = 123,
                ProcessName = "Rome2.exe",
                Architecture = "x86"
            },
            Scenario = new LuaCaptureScenario
            {
                ScenarioId = "treasury-test",
                StepId = stepId,
                Action = "test"
            },
            KnownValues = new LuaCaptureKnownValues
            {
                UiTreasury = uiTreasury,
                ValueHistory = new[] { uiTreasury }
            },
            Candidates = windows,
            Warnings = Array.Empty<string>()
        };
    }

    private static CapturedCandidateWindow CreateWindow(ulong candidateAddress, int relativeOffset, int value)
    {
        var fieldAddress = (ulong)((long)candidateAddress + relativeOffset);
        return new CapturedCandidateWindow
        {
            CandidateId = $"treasury-int32-0x{candidateAddress:X8}",
            Address = candidateAddress,
            AddressHex = $"0x{candidateAddress:X}",
            Region = new LuaCaptureRegion
            {
                BaseAddress = candidateAddress & 0xFFFFF000,
                BaseAddressHex = $"0x{candidateAddress & 0xFFFFF000:X}",
                Size = 0x1000,
                State = "MEM_COMMIT",
                Protection = "PAGE_READWRITE",
                Type = "MEM_PRIVATE",
                IsReadable = true,
                IsWritable = true,
                IsExecutable = false
            },
            ContextStart = candidateAddress - 0x200,
            ContextStartHex = $"0x{candidateAddress - 0x200:X}",
            ContextByteCount = 4,
            ContextBytesHex = "01020304",
            DecodedInt32Fields = new[]
            {
                new CapturedInt32Field
                {
                    RelativeOffset = relativeOffset,
                    RelativeOffsetHex = relativeOffset < 0 ? $"-0x{Math.Abs(relativeOffset):X}" : $"0x{relativeOffset:X}",
                    Address = fieldAddress,
                    AddressHex = $"0x{fieldAddress:X}",
                    Value = value,
                    MatchesUiValue = true
                },
                new CapturedInt32Field
                {
                    RelativeOffset = -0x40,
                    RelativeOffsetHex = "-0x40",
                    Address = candidateAddress - 0x40,
                    AddressHex = $"0x{candidateAddress - 0x40:X}",
                    Value = 777,
                    MatchesUiValue = false
                }
            },
            PointerLikeValues = Array.Empty<CapturedPointerLikeValue>(),
            Evidence = new[] { "test capture" },
            Warnings = Array.Empty<string>()
        };
    }

    private static KnownValueScanSession CreateSession(params Candidate[] candidates)
    {
        var now = DateTimeOffset.Now;
        return new KnownValueScanSession(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            ValueType: "Int32",
            CreatedAt: now,
            UpdatedAt: now,
            ValueHistory: new[] { 4500, 4200 },
            Candidates: candidates,
            Counters: new KnownValueScanCounters(1, 1, 0, 4, 0, candidates.Length, candidates.Length),
            Warnings: Array.Empty<string>());
    }

    private static Candidate CreateCandidate(ulong address)
    {
        var region = new MemoryRegionInfo(address & 0xFFFFF000, 0x1000, "MEM_COMMIT", "PAGE_READWRITE", "MEM_PRIVATE", true, true, false);
        return new Candidate(
            FeatureId: KnownValueScanner.TreasuryFeatureId,
            CandidateId: $"treasury-int32-0x{address:X8}",
            Address: address,
            Type: "Int32",
            ObservedValue: "4200",
            ExpectedValue: "4200",
            Region: region,
            SuspectedStructure: null,
            Owner: OwnerKind.Unknown,
            OwnerConfidence: 0,
            Evidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Confidence: 0.65);
    }
}
