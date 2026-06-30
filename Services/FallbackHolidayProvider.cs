using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class FallbackHolidayProvider : IHolidayProvider
{
    private readonly IHolidayProvider _primary;
    private readonly IHolidayProvider _fallback;
    private readonly object _statusLock = new();

    public HolidayDataStatus Status { get; } = new()
    {
        Source = "节假日数据",
        Message = "尚未加载节假日数据"
    };

    public FallbackHolidayProvider(IHolidayProvider primary, IHolidayProvider fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(int year)
    {
        var primaryResult = await _primary.GetHolidaysAsync(year);
        if (primaryResult.Count > 0)
        {
            UpdateStatus(
                "联网节假日数据",
                $"{year} 年已加载 {primaryResult.Count} 条记录",
                true);
            return primaryResult;
        }

        var fallbackResult = await _fallback.GetHolidaysAsync(year);
        if (fallbackResult.Count > 0)
        {
            UpdateStatus(
                "节假日数据",
                $"{year} 年联网暂无数据，已回退到本地数据（{fallbackResult.Count} 条）",
                true);
            return fallbackResult;
        }

        UpdateStatus(
            "节假日数据",
            $"{year} 年联网和本地均无可用数据",
            false);
        return [];
    }

    public void InvalidateCache(int year)
    {
        _primary.InvalidateCache(year);
        _fallback.InvalidateCache(year);
    }

    private void UpdateStatus(string source, string message, bool isAvailable)
    {
        lock (_statusLock)
        {
            Status.Source = source;
            Status.Message = message;
            Status.IsAvailable = isAvailable;
            Status.LastUpdatedAt = DateTime.Now;
        }
    }
}
