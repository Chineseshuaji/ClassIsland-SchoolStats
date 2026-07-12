using ClassIsland.SchoolStats.Controls;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Tests;

public sealed class StatsDisplayPolicyTests
{
    [Fact]
    public void EmptyExclusionListProducesNoTooltipSection()
    {
        Assert.Equal(string.Empty, StatsDisplayPolicy.FormatExclusions([]));
    }

    [Fact]
    public void ExclusionTooltipFormatsSingleDatesAndRanges()
    {
        var exclusions = new List<ExclusionDetail>
        {
            Detail("校运会", new DateTime(2026, 10, 12), new DateTime(2026, 10, 12), 1),
            Detail("期末活动", new DateTime(2026, 12, 28), new DateTime(2026, 12, 30), 3)
        };

        var text = StatsDisplayPolicy.FormatExclusions(exclusions);

        Assert.Equal(
            "已排除：\n校运会（10-12）：1 天\n期末活动（12-28~12-30）：3 天",
            text);
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(8, 3)]
    public void ExclusionTooltipReportsItemsBeyondVisibleLimit(
        int totalCount,
        int hiddenCount)
    {
        var exclusions = Enumerable.Range(1, totalCount)
            .Select(index => Detail(
                $"排除项 {index}",
                new DateTime(2026, 9, index),
                new DateTime(2026, 9, index),
                1))
            .ToList();

        var text = StatsDisplayPolicy.FormatExclusions(exclusions);

        Assert.Contains("排除项 5", text);
        Assert.DoesNotContain("排除项 6", text);
        Assert.EndsWith($"另有 {hiddenCount} 项未展开，请在组件设置中查看。", text);
    }

    [Fact]
    public void ExclusionTooltipDoesNotAddSummaryAtExactLimit()
    {
        var exclusions = Enumerable.Range(1, StatsDisplayPolicy.VisibleExclusionLimit)
            .Select(index => Detail(
                $"排除项 {index}",
                new DateTime(2026, 9, index),
                new DateTime(2026, 9, index),
                1))
            .ToList();

        var text = StatsDisplayPolicy.FormatExclusions(exclusions);

        Assert.DoesNotContain("未展开", text);
    }

    [Fact]
    public void ExclusionTooltipPreservesInputOrderWithoutMutation()
    {
        var first = Detail(
            "第一项",
            new DateTime(2026, 9, 2, 8, 0, 0),
            new DateTime(2026, 9, 2, 12, 0, 0),
            1);
        var second = Detail(
            "第二项",
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 3),
            3);
        var exclusions = new List<ExclusionDetail> { first, second };

        var text = StatsDisplayPolicy.FormatExclusions(exclusions);

        Assert.True(text.IndexOf("第一项", StringComparison.Ordinal)
                    < text.IndexOf("第二项", StringComparison.Ordinal));
        Assert.Same(first, exclusions[0]);
        Assert.Same(second, exclusions[1]);
        Assert.Contains("第一项（09-02）：1 天", text);
    }

    private static ExclusionDetail Detail(
        string name,
        DateTime start,
        DateTime end,
        int excludedDays)
        => new()
        {
            Name = name,
            StartDate = start,
            EndDate = end,
            ExcludedDays = excludedDays
        };
}
