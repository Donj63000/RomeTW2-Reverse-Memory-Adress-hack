using Rome2Explorer.Domain;

namespace Rome2Explorer.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Candidate_StoresFutureScanMetadata()
    {
        var region = new MemoryRegionInfo(
            BaseAddress: 0x5000,
            Size: 0x1000,
            State: "MEM_COMMIT",
            Protection: "PAGE_READWRITE",
            Type: "MEM_PRIVATE",
            IsReadable: true,
            IsWritable: true,
            IsExecutable: false);

        var candidate = new Candidate(
            FeatureId: "campaign.player_faction.treasury",
            CandidateId: "candidate-1",
            Address: 0x5120,
            Type: "int32",
            ObservedValue: "4500",
            ExpectedValue: "4500",
            Region: region,
            SuspectedStructure: new SuspectedStructure("FactionEconomy", 0x5000, 0x120, 0.55),
            Owner: OwnerKind.Player,
            OwnerConfidence: 0.6,
            Evidence: new[] { "snapshot initial" },
            Warnings: Array.Empty<string>(),
            Confidence: 0.5);

        Assert.Equal("campaign.player_faction.treasury", candidate.FeatureId);
        Assert.Equal(0x5120UL, candidate.Address);
        Assert.True(candidate.Region!.IsWritable);
        Assert.Equal(OwnerKind.Player, candidate.Owner);
    }

    [Fact]
    public void DetectionResult_StoresValidationStateAndWarnings()
    {
        var result = new DetectionResult(
            FeatureId: "diplomacy.force_accept",
            Address: null,
            StructureName: "DiplomacyDecision",
            StructureBase: null,
            FieldOffset: null,
            Owner: OwnerKind.AI,
            Confidence: 0.25,
            Evidence: new[] { "modele reserve pour milestone futur" },
            Warnings: new[] { "non valide en V1" },
            Status: DetectionStatus.Candidate);

        Assert.Equal(DetectionStatus.Candidate, result.Status);
        Assert.Contains("non valide en V1", result.Warnings);
        Assert.Null(result.Address);
    }
}
