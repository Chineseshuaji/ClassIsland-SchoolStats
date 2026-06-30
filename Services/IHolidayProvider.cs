namespace ClassIsland.SchoolStats.Services;

public interface IHolidayProvider
{
    HolidayDataStatus Status { get; }
    Task<IReadOnlyList<Models.HolidayInfo>> GetHolidaysAsync(int year);
    void InvalidateCache(int year);
}

public class HolidayDataStatus
{
    public string Source { get; set; } = "未知";
    public string Message { get; set; } = "尚未加载节假日数据";
    public bool IsAvailable { get; set; }
    public DateTime? LastUpdatedAt { get; set; }

    public string DisplayText => LastUpdatedAt.HasValue
        ? $"{Source}：{Message}（{LastUpdatedAt:HH:mm:ss}）"
        : $"{Source}：{Message}";
}
