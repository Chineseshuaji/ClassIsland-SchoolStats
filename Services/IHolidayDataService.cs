namespace ClassIsland.SchoolStats.Services;

public interface IHolidayDataService
{
    bool IsSchoolDay(DateTime date, out string? reason);
}
