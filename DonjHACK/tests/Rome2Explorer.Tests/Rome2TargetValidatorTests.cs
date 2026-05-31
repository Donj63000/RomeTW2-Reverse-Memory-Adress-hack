using System.Diagnostics;
using Rome2Explorer.Validation;

namespace Rome2Explorer.Tests;

public sealed class Rome2TargetValidatorTests
{
    [Theory]
    [InlineData("Rome2")]
    [InlineData("Rome2.exe")]
    [InlineData("rome2")]
    [InlineData("ROME2.EXE")]
    public void IsRome2ProcessName_AcceptsRome2Names(string processName)
    {
        Assert.True(Rome2TargetValidator.IsRome2ProcessName(processName));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("Rome")]
    [InlineData("")]
    public void IsRome2ProcessName_RejectsNonRome2Names(string processName)
    {
        Assert.False(Rome2TargetValidator.IsRome2ProcessName(processName));
    }

    [Fact]
    public void EnsureRome2Process_RejectsCurrentNonRome2Process()
    {
        using var process = Process.GetCurrentProcess();

        if (Rome2TargetValidator.IsRome2ProcessName(process.ProcessName))
        {
            return;
        }

        Assert.Throws<InvalidOperationException>(() => Rome2TargetValidator.EnsureRome2Process(process));
    }
}
