using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public interface IStatisticsService
{
    AggregatedStats CalculateStats();
    AggregatedStats CalculateStats(DateTime referenceDateTime);
}
