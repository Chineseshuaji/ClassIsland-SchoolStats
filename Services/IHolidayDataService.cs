namespace ClassIsland.SchoolStats.Services;

public interface IHolidayDataService
{
    Task<SchoolDayResult> GetSchoolDayAsync(
        DateTime date,
        CancellationToken cancellationToken = default);

    Task WarmUpAsync(
        IEnumerable<int> years,
        CancellationToken cancellationToken = default);

    void InvalidateCache();
}
