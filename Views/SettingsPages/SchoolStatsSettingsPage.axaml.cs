using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using ClassIsland.Shared;

namespace ClassIsland.SchoolStats.Views.SettingsPages;

[SettingsPageInfo("classisland.schoolstats.settings", "在校时间统计", "fa-solid fa-graduation-cap", "配置学期起止日期、自定义假期与补班日")]
[Group("classisland.schoolstats")]
public partial class SchoolStatsSettingsPage : SettingsPageBase
{
    private SemesterConfiguration? _config;
    private IStatisticsService? _statsService;
    private ObservableCollection<HolidayDisplayItem> _holidays = [];
    private ObservableCollection<HolidayDisplayItem> _workdays = [];

    public SchoolStatsSettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        HolidayListBox.ItemsSource = _holidays;
        WorkdayListBox.ItemsSource = _workdays;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _config = IAppHost.GetService<SemesterConfiguration>();
        _statsService = IAppHost.GetService<IStatisticsService>();
        LoadConfig();

        StartDatePicker.SelectedDateChanged += (_, _) => { if (_config != null && StartDatePicker.SelectedDate.HasValue) { _config.StartDate = StartDatePicker.SelectedDate.Value.DateTime; InvalidateAndRefresh(); } };
        EndDatePicker.SelectedDateChanged += (_, _) => { if (_config != null && EndDatePicker.SelectedDate.HasValue) { _config.EndDate = EndDatePicker.SelectedDate.Value.DateTime; InvalidateAndRefresh(); } };
        DailyHoursSpinner.ValueChanged += (_, _) => { if (_config != null) _config.DailyHours = DailyHoursSpinner.Value; };
        ExcludeWeekendsCheck.IsCheckedChanged += (_, _) => { if (_config != null) { _config.ExcludeWeekends = ExcludeWeekendsCheck.IsChecked == true; InvalidateAndRefresh(); } };
        NetworkHolidayCheck.IsCheckedChanged += (_, _) => { if (_config != null) _config.EnableNetworkHolidayUpdate = NetworkHolidayCheck.IsChecked == true; } };
        AddHolidayBtn.Click += (_, _) => AddHoliday();
        RemoveHolidayBtn.Click += (_, _) => RemoveHoliday();
        AddWorkdayBtn.Click += (_, _) => AddWorkday();
        RemoveWorkdayBtn.Click += (_, _) => RemoveWorkday();
    }

    private void LoadConfig()
    {
        if (_config == null) return;
        StartDatePicker.SelectedDate = new DateTimeOffset(_config.StartDate);
        EndDatePicker.SelectedDate = new DateTimeOffset(_config.EndDate);
        DailyHoursSpinner.Value = _config.DailyHours;
        ExcludeWeekendsCheck.IsChecked = _config.ExcludeWeekends;
        NetworkHolidayCheck.IsChecked = _config.EnableNetworkHolidayUpdate;
        _holidays.Clear();
        foreach (var h in _config.CustomHolidays) _holidays.Add(new HolidayDisplayItem { Name = h.Name, StartDate = h.StartDate, EndDate = h.EndDate, Category = h.Category });
        _workdays.Clear();
        foreach (var w in _config.CustomWorkdays) _workdays.Add(new HolidayDisplayItem { Name = w.Name, StartDate = w.StartDate, EndDate = w.EndDate, Category = w.Category });
    }

    private void AddHoliday()
    {
        if (_config == null) return;
        var name = HolidayNameInput.Text ?? "自定义假期";
        var start = HolidayStartPicker.SelectedDate?.DateTime ?? DateTime.Now;
        var end = HolidayEndPicker.SelectedDate?.DateTime ?? start;
        _config.CustomHolidays.Add(new HolidayInfo { Name = name, StartDate = start, EndDate = end, Category = HolidayCategory.Custom });
        _holidays.Add(new HolidayDisplayItem { Name = name, StartDate = start, EndDate = end, Category = HolidayCategory.Custom });
        InvalidateAndRefresh();
    }

    private void RemoveHoliday()
    {
        if (_config == null || HolidayListBox.SelectedIndex < 0) return;
        var idx = HolidayListBox.SelectedIndex;
        if (idx < _config.CustomHolidays.Count) _config.CustomHolidays.RemoveAt(idx);
        _holidays.RemoveAt(idx);
        InvalidateAndRefresh();
    }

    private void AddWorkday()
    {
        if (_config == null) return;
        var name = WorkdayNameInput.Text ?? "调休补班";
        var date = WorkdayDatePicker.SelectedDate?.DateTime ?? DateTime.Now;
        _config.CustomWorkdays.Add(new HolidayInfo { Name = name, StartDate = date, EndDate = date, Category = HolidayCategory.MakeUpWorkday });
        _workdays.Add(new HolidayDisplayItem { Name = name, StartDate = date, EndDate = date, Category = HolidayCategory.MakeUpWorkday });
        InvalidateAndRefresh();
    }

    private void RemoveWorkday()
    {
        if (_config == null || WorkdayListBox.SelectedIndex < 0) return;
        var idx = WorkdayListBox.SelectedIndex;
        if (idx < _config.CustomWorkdays.Count) _config.CustomWorkdays.RemoveAt(idx);
        _workdays.RemoveAt(idx);
        InvalidateAndRefresh();
    }

    private void InvalidateAndRefresh() => (_statsService as StatisticsService)?.InvalidateCache();
}

public class HolidayDisplayItem
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public HolidayCategory Category { get; set; }
    public string DisplayText => EndDate == StartDate ? $"{Name}（{StartDate:yyyy-MM-dd}）" : $"{Name}（{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}）";
    public override string ToString() => DisplayText;
}
