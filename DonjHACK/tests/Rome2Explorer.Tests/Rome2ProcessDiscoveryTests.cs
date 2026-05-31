using Rome2Explorer.Domain;
using Rome2Explorer.Memory;

namespace Rome2Explorer.Tests;

public sealed class Rome2ProcessDiscoveryTests
{
    [Fact]
    public void MarkRecommended_ReturnsEmptyListWhenNoProcessExists()
    {
        var candidates = Rome2ProcessDiscovery.MarkRecommended(Array.Empty<Rome2ProcessCandidate>());

        Assert.Empty(candidates);
    }

    [Fact]
    public void MarkRecommended_PrefersProcessWithMainWindow()
    {
        var oldWithWindow = CreateCandidate(100, new DateTimeOffset(2026, 5, 31, 1, 0, 0, TimeSpan.Zero), hasMainWindow: true);
        var recentWithoutWindow = CreateCandidate(200, new DateTimeOffset(2026, 5, 31, 2, 0, 0, TimeSpan.Zero), hasMainWindow: false);

        var candidates = Rome2ProcessDiscovery.MarkRecommended(new[] { recentWithoutWindow, oldWithWindow });

        Assert.Equal(100, candidates.Single(candidate => candidate.IsRecommended).ProcessId);
    }

    [Fact]
    public void MarkRecommended_PrefersMostRecentWhenWindowStateIsEqual()
    {
        var oldProcess = CreateCandidate(100, new DateTimeOffset(2026, 5, 31, 1, 0, 0, TimeSpan.Zero), hasMainWindow: true);
        var recentProcess = CreateCandidate(200, new DateTimeOffset(2026, 5, 31, 2, 0, 0, TimeSpan.Zero), hasMainWindow: true);

        var candidates = Rome2ProcessDiscovery.MarkRecommended(new[] { oldProcess, recentProcess });

        Assert.Equal(200, candidates.Single(candidate => candidate.IsRecommended).ProcessId);
        Assert.Equal(200, candidates[0].ProcessId);
    }

    [Fact]
    public void MarkRecommended_MarksOnlyOneProcess()
    {
        var candidates = Rome2ProcessDiscovery.MarkRecommended(new[]
        {
            CreateCandidate(100, new DateTimeOffset(2026, 5, 31, 1, 0, 0, TimeSpan.Zero), hasMainWindow: true),
            CreateCandidate(200, new DateTimeOffset(2026, 5, 31, 2, 0, 0, TimeSpan.Zero), hasMainWindow: true),
            CreateCandidate(300, new DateTimeOffset(2026, 5, 31, 3, 0, 0, TimeSpan.Zero), hasMainWindow: false)
        });

        Assert.Single(candidates.Where(candidate => candidate.IsRecommended));
    }

    private static Rome2ProcessCandidate CreateCandidate(int processId, DateTimeOffset startTime, bool hasMainWindow)
    {
        return new Rome2ProcessCandidate(
            ProcessId: processId,
            Name: "Rome2",
            Path: $@"C:\Games\Rome2-{processId}.exe",
            StartTime: startTime,
            MainWindowTitle: hasMainWindow ? "Total War: ROME II" : string.Empty,
            HasMainWindow: hasMainWindow,
            IsRecommended: false,
            RecommendationReason: string.Empty);
    }
}
