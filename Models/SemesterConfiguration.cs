using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ClassIsland.SchoolStats.Models;

public class SemesterConfiguration : INotifyPropertyChanged
{
    private DateTime _startDate = DateTime.Now;
    private DateTime _endDate = DateTime.Now.AddMonths(4);
    private double _dailyHours = 8.0;
    private bool _excludeWeekends = true;
    private bool _enableNetworkHolidayUpdate;
    private List<HolidayInfo> _customHolidays = [];
    private List<HolidayInfo> _customWorkdays = [];

    public DateTime StartDate
    {
        get => _startDate;
        set { if (value != _startDate) { _startDate = value; OnPropertyChanged(); } }
    }

    public DateTime EndDate
    {
        get => _endDate;
        set { if (value != _endDate) { _endDate = value; OnPropertyChanged(); } }
    }

    public double DailyHours
    {
        get => _dailyHours;
        set { if (Math.Abs(value - _dailyHours) > 0.01) { _dailyHours = value; OnPropertyChanged(); } }
    }

    public bool ExcludeWeekends
    {
        get => _excludeWeekends;
        set { if (value != _excludeWeekends) { _excludeWeekends = value; OnPropertyChanged(); } }
    }

    public bool EnableNetworkHolidayUpdate
    {
        get => _enableNetworkHolidayUpdate;
        set { if (value != _enableNetworkHolidayUpdate) { _enableNetworkHolidayUpdate = value; OnPropertyChanged(); } }
    }

    public List<HolidayInfo> CustomHolidays
    {
        get => _customHolidays;
        set { _customHolidays = value; OnPropertyChanged(); }
    }

    public List<HolidayInfo> CustomWorkdays
    {
        get => _customWorkdays;
        set { _customWorkdays = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public int TotalCalendarDays => (EndDate - StartDate).Days + 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
