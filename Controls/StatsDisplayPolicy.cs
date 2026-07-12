using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Controls;

internal static class StatsDisplayPolicy
{
    internal const int VisibleExclusionLimit = 5;

    internal static string FormatExclusions(IReadOnlyList<ExclusionDetail> exclusions)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        if (exclusions.Count == 0)
            return string.Empty;

        var visibleCount = Math.Min(exclusions.Count, VisibleExclusionLimit);
        var lines = new List<string>(visibleCount + 1);
        for (var index = 0; index < visibleCount; index++)
        {
            var detail = exclusions[index];
            lines.Add(detail.StartDate.Date == detail.EndDate.Date
                ? $"{detail.Name}（{detail.StartDate:MM-dd}）：1 天"
                : $"{detail.Name}（{detail.StartDate:MM-dd}~{detail.EndDate:MM-dd}）："
                  + $"{detail.ExcludedDays} 天");
        }

        var hiddenCount = exclusions.Count - visibleCount;
        if (hiddenCount > 0)
            lines.Add($"另有 {hiddenCount} 项未展开，请在组件设置中查看。");

        return "已排除：\n" + string.Join("\n", lines);
    }
}
