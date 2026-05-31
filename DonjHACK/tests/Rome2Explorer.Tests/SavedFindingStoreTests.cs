using Rome2Explorer.Domain;
using Rome2Explorer.Trace;

namespace Rome2Explorer.Tests;

public sealed class SavedFindingStoreTests
{
    [Fact]
    public void Upsert_WritesKnownFindingUnderEvidenceDirectory()
    {
        var root = CreateTempRoot();
        var store = new SavedFindingStore();
        var finding = CreateFinding(0x24C817D4);

        var path = store.Upsert(finding, root);
        var loaded = store.Load(root);

        Assert.Equal(Path.Combine(root, "evidence", "known-findings", "known-findings.json"), path);
        Assert.True(File.Exists(path));
        Assert.Single(loaded);
        Assert.Equal("0x24C817D4", loaded[0].AddressHex);
        Assert.Equal(new[] { 67000, 64208 }, loaded[0].ValueHistory);
    }

    [Fact]
    public void Upsert_ReplacesSameFeatureAddressArchitecture()
    {
        var root = CreateTempRoot();
        var store = new SavedFindingStore();

        store.Upsert(CreateFinding(0x30547934, observedValue: "64208"), root);
        store.Upsert(CreateFinding(0x30547934, observedValue: "63800"), root);

        var loaded = store.Load(root);

        Assert.Single(loaded);
        Assert.Equal("63800", loaded[0].ObservedValue);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "donjhack-known-findings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static SavedMemoryFinding CreateFinding(ulong address, string observedValue = "64208")
    {
        return new SavedMemoryFinding(
            FeatureId: "campaign.player_faction.treasury",
            Label: $"Plausible treasury 0x{address:X}",
            SavedAt: DateTimeOffset.Now,
            ProcessId: 13548,
            ProcessName: "Rome2",
            Architecture: "X86",
            ProcessPath: @"C:\Games\Total War - Rome 2\Rome2.exe",
            Address: address,
            AddressHex: $"0x{address:X}",
            Type: "Int32",
            Status: "Plausible",
            ObservedValue: observedValue,
            ExpectedValue: observedValue,
            Confidence: 0.4,
            ModuleName: null,
            ModuleBaseHex: null,
            ModuleOffsetHex: null,
            RegionBaseHex: "0x30540000",
            RegionOffsetHex: "0x7934",
            RegionState: "MEM_COMMIT",
            RegionProtection: "PAGE_READWRITE",
            RegionType: "MEM_PRIVATE",
            HookStatus: "Aucun hook - lecture seule",
            OffsetStatus: "0x30540000+0x7934 (region dynamique)",
            PointerStatus: "Non resolu - pointer scan futur",
            StabilityStatus: "A valider par refine supplementaire",
            ValueHistory: new[] { 67000, 64208 },
            Evidence: new[] { "Scan initial", "Refine OK" },
            Warnings: Array.Empty<string>());
    }
}
