namespace ClassIsland.SchoolStats.Models;

public class AggregatedStats
{
    public int TotalSchoolDays { get; set; }
    public int PassedSchoolDays { get; set; }
    public int RemainingSchoolDays => TotalSchoolDays - PassedSchoolDays;
    public double PassedSchoolHours { get; set; }
    public double TotalSchoolHours { get; set; }
    public double ProgressPercentage =>
        TotalSchoolDays > 0 ? Math.Round((double)PassedSchoolDays / TotalSchoolDays * 100.0, 1) : 0.0;
    public List<ExclusionDetail> AppliedExclusions { get; set; } = [];
    public DateTime ReferenceDate { get; set; } = DateTime.Now.Date;
}

public class ExclusionDetail
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ExcludedDays { get; set; }
}
