using Rome2Explorer.Signatures;

namespace Rome2Explorer.Tests;

public sealed class AobPatternTests
{
    [Fact]
    public void Parse_AcceptsHexBytesAndWildcards()
    {
        var pattern = AobPattern.Parse("8B ?? 90 ?");

        Assert.Equal(4, pattern.Bytes.Count);
        Assert.Equal((byte)0x8B, pattern.Bytes[0].Value.GetValueOrDefault());
        Assert.True(pattern.Bytes[1].IsWildcard);
        Assert.Equal((byte)0x90, pattern.Bytes[2].Value.GetValueOrDefault());
        Assert.True(pattern.Bytes[3].IsWildcard);
    }

    [Fact]
    public void Parse_AcceptsCommonWhitespaceAndLowercaseHex()
    {
        var pattern = AobPattern.Parse("8b\t??\r\n90");

        Assert.Equal(3, pattern.Bytes.Count);
        Assert.Equal((byte)0x8B, pattern.Bytes[0].Value.GetValueOrDefault());
        Assert.True(pattern.Bytes[1].IsWildcard);
        Assert.Equal((byte)0x90, pattern.Bytes[2].Value.GetValueOrDefault());
    }

    [Fact]
    public void Parse_RejectsEmptyPatterns()
    {
        Assert.Throws<ArgumentException>(() => AobPattern.Parse("  "));
    }

    [Fact]
    public void Parse_RejectsInvalidTokens()
    {
        Assert.Throws<FormatException>(() => AobPattern.Parse("8B XYZ"));
    }
}
