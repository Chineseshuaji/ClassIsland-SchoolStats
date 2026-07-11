namespace ClassIsland.SchoolStats.Models;

public class AggregatedStats
{
    public int TotalSchoolDays { get; set; }
    public int PassedSchoolDays { get; set; }
    public int RemainingSchoolDays => Math.Max(0, TotalSchoolDays - PassedSchoolDays);
    public double PassedSchoolHours { get; set; }
    public double TotalSchoolHours { get; set; }
    public double RemainingSchoolHours => Math.Max(0, TotalSchoolHours - PassedSchoolHours);
    public double RemainingActualSchoolDays =>
        TotalSchoolDays > 0 && TotalSchoolHours > 0
            ? Math.Round(RemainingSchoolHours / (TotalSchoolHours / TotalSchoolDays), 1)
            : 0;
    public string RemainingSchoolTimeText => FormatSchoolTime(RemainingTimeDisplayPrecision.Seconds, false);
    public string RemainingSchoolCompactTimeText => FormatSchoolTime(RemainingTimeDisplayPrecision.Seconds, true);
    public double ProgressPercentage =>
        TotalSchoolHours > 0
            ? Math.Round(Math.Clamp(PassedSchoolHours / TotalSchoolHours * 100.0, 0, 100), 1)
            : 0.0;
    public WeeklyStats CurrentWeek { get; set; } = new();
    public List<ExclusionDetail> AppliedExclusions { get; set; } = [];
    public DateTime ReferenceDate { get; set; } = DateTime.Now.Date;

    public string FormatRemainingSchoolTime(RemainingTimeDisplayPrecision precision, bool compact = false)
        => FormatSchoolTime(precision, compact);

    private string FormatSchoolTime(RemainingTimeDisplayPrecision precision, bool compact)
    {
        if (TotalSchoolDays <= 0 || TotalSchoolHours <= 0)
        {
            return precision switch
            {
                RemainingTimeDisplayPrecision.Days => "0 天",
                RemainingTimeDisplayPrecision.Hours => "0 小时",
                RemainingTimeDisplayPrecision.Minutes => compact ? "00:00" : "0 分钟",
                _ => compact ? "00:00:00" : "0 秒"
            };
        }

        var hoursPerDay = TotalSchoolHours / TotalSchoolDays;
        if (hoursPerDay <= 0)
        {
            return precision switch
            {
                RemainingTimeDisplayPrecision.Days => "0 天",
                RemainingTimeDisplayPrecision.Hours => "0 小时",
                RemainingTimeDisplayPrecision.Minutes => compact ? "00:00" : "0 分钟",
                _ => compact ? "00:00:00" : "0 秒"
            };
        }

        if (precision == RemainingTimeDisplayPrecision.Days)
        {
            return $"{RemainingActualSchoolDays:F1} 天";
        }

        if (precision == RemainingTimeDisplayPrecision.Hours)
        {
            return $"{RemainingSchoolHours:F1} 小时";
        }

        var remainingSeconds = Math.Max(0, (int)Math.Round(RemainingSchoolHours * 3600));
        var secondsPerSchoolDay = Math.Max(1, (int)Math.Round(hoursPerDay * 3600));
        var days = remainingSeconds / secondsPerSchoolDay;
        var secondsAfterDays = remainingSeconds % secondsPerSchoolDay;
        var hours = secondsAfterDays / 3600;
        var minutes = secondsAfterDays % 3600 / 60;
        var seconds = secondsAfterDays % 60;
        var showSeconds = precision == RemainingTimeDisplayPrecision.Seconds;

        if (compact)
        {
            if (showSeconds)
            {
                return days > 0
                    ? $"{days}天 {hours:D2}:{minutes:D2}:{seconds:D2}"
                    : $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }

            return days > 0
                ? $"{days}天 {hours:D2}:{minutes:D2}"
                : $"{hours:D2}:{minutes:D2}";
        }

        if (days > 0)
        {
            return showSeconds
                ? $"{days} 天 {hours} 小时 {minutes} 分钟 {seconds} 秒"
                : $"{days} 天 {hours} 小时 {minutes} 分钟";
        }

        if (hours > 0)
        {
            return showSeconds
                ? $"{hours} 小时 {minutes} 分钟 {seconds} 秒"
                : $"{hours} 小时 {minutes} 分钟";
        }

        if (minutes > 0)
        {
            return showSeconds
                ? $"{minutes} 分钟 {seconds} 秒"
                : $"{minutes} 分钟";
        }

        return showSeconds ? $"{seconds} 秒" : "不足 1 分钟";
    }
}

public class ExclusionDetail
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ExcludedDays { get; set; }
}

public class WeeklyStats
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalSchoolDays { get; set; }
    public int PassedSchoolDays { get; set; }
    public int RemainingSchoolDays => Math.Max(0, TotalSchoolDays - PassedSchoolDays);
    public double TotalSchoolHours { get; set; }
    public double PassedSchoolHours { get; set; }
    public double RemainingSchoolHours => Math.Max(0, TotalSchoolHours - PassedSchoolHours);
    public double ProgressPercentage =>
        TotalSchoolHours > 0
            ? Math.Round(Math.Clamp(PassedSchoolHours / TotalSchoolHours * 100.0, 0, 100), 1)
            : 0.0;
}
