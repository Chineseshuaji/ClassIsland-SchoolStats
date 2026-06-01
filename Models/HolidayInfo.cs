namespace ClassIsland.SchoolStats.Models;

public class HolidayInfo
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public HolidayCategory Category { get; set; } = HolidayCategory.Custom;

    public bool Contains(DateTime date)
        => date.Date >= StartDate.Date && date.Date <= EndDate.Date;
}

public enum HolidayCategory
{
    LegalHoliday,
    WinterBreak,
    SummerBreak,
    Custom,
    MakeUpWorkday
}
