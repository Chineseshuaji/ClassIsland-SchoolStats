using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public interface IStatisticsService
{
    Task<AggregatedStats> CalculateStatsAsync(
        DateTime referenceDateTime,
        CancellationToken cancellationToken = default);

    void InvalidateCache();
}
