namespace ClassIsland.SchoolStats.Services;

public interface IHolidayProvider
{
    Task<IReadOnlyList<Models.HolidayInfo>> GetHolidaysAsync(int year);
    void InvalidateCache(int year);
}
