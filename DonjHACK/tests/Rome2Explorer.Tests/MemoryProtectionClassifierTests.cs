using Rome2Explorer.Memory;

namespace Rome2Explorer.Tests;

public sealed class MemoryProtectionClassifierTests
{
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadonly = 0x02;
    private const uint PageReadwrite = 0x04;
    private const uint PageExecuteRead = 0x20;
    private const uint PageGuard = 0x100;
    private const uint MemPrivate = 0x20000;

    [Fact]
    public void Classify_ReadOnlyCommittedRegion_IsReadableOnly()
    {
        var result = MemoryProtectionClassifier.Classify(PageReadonly, MemCommit, MemPrivate);

        Assert.True(result.IsReadable);
        Assert.False(result.IsWritable);
        Assert.False(result.IsExecutable);
        Assert.Equal("PAGE_READONLY", result.Protection);
    }

    [Fact]
    public void Classify_ReadWriteCommittedRegion_IsReadableAndWritable()
    {
        var result = MemoryProtectionClassifier.Classify(PageReadwrite, MemCommit, MemPrivate);

        Assert.True(result.IsReadable);
        Assert.True(result.IsWritable);
        Assert.False(result.IsExecutable);
    }

    [Fact]
    public void Classify_ExecuteReadCommittedRegion_IsReadableAndExecutable()
    {
        var result = MemoryProtectionClassifier.Classify(PageExecuteRead, MemCommit, MemPrivate);

        Assert.True(result.IsReadable);
        Assert.False(result.IsWritable);
        Assert.True(result.IsExecutable);
    }

    [Fact]
    public void Classify_NoAccessRegion_IsNotReadable()
    {
        var result = MemoryProtectionClassifier.Classify(PageNoAccess, MemCommit, MemPrivate);

        Assert.False(result.IsReadable);
        Assert.False(result.IsWritable);
        Assert.False(result.IsExecutable);
    }

    [Fact]
    public void Classify_GuardRegion_IsNotReadable()
    {
        var result = MemoryProtectionClassifier.Classify(PageReadwrite | PageGuard, MemCommit, MemPrivate);

        Assert.False(result.IsReadable);
        Assert.False(result.IsWritable);
        Assert.False(result.IsExecutable);
        Assert.Contains("PAGE_GUARD", result.Protection);
    }

    [Fact]
    public void Classify_ReservedRegion_IsNotReadableEvenWithReadProtection()
    {
        var result = MemoryProtectionClassifier.Classify(PageReadonly, MemReserve, MemPrivate);

        Assert.False(result.IsReadable);
    }
}
