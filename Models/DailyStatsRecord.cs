namespace ClassIsland.SchoolStats.Models;

public class DailyStatsRecord
{
    public DateTime Date { get; set; }
    public bool IsSchoolDay { get; set; }
    public string? ExclusionReason { get; set; }
}
