using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using ClassIsland.Shared;

namespace ClassIsland.SchoolStats.Controls;

public partial class StatsComponentSettingsControl : UserControl
{
    private StatsComponentSettings? _settings;
    private SemesterConfiguration? _config;
    private IStatisticsService? _statsService;
    private readonly ObservableCollection<HolidayDisplayItem> _holidays = [];
    private readonly ObservableCollection<HolidayDisplayItem> _workdays = [];

    public StatsComponentSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        HolidayListBox.ItemsSource = _holidays;
        WorkdayListBox.ItemsSource = _workdays;
    }

    public void SetSettings(StatsComponentSettings settings)
    {
        _settings = settings;
        ApplyDisplaySettingsToUi();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _config = IAppHost.GetService<SemesterConfiguration>();
        _statsService = IAppHost.GetService<IStatisticsService>();

        // 显示设置绑定
        ShowProgressBarCheck.IsCheckedChanged += (_, _) =>
        {
            if (_settings != null) _settings.ShowProgressBar = ShowProgressBarCheck.IsChecked == true;
        };
        ShowHoursCheck.IsCheckedChanged += (_, _) =>
        {
            if (_settings != null) _settings.ShowHours = ShowHoursCheck.IsChecked == true;
        };
        CompactModeCheck.IsCheckedChanged += (_, _) =>
        {
            if (_settings != null) _settings.CompactMode = CompactModeCheck.IsChecked == true;
        };

        ApplyDisplaySettingsToUi();

        // 学期配置绑定
        LoadSemesterConfig();
        StartDatePicker.SelectedDateChanged += (_, _) =>
        {
            if (_config != null && StartDatePicker.SelectedDate.HasValue)
            {
                _config.StartDate = StartDatePicker.SelectedDate.Value.DateTime;
                InvalidateServiceCache();
            }
        };
        EndDatePicker.SelectedDateChanged += (_, _) =>
        {
            if (_config != null && EndDatePicker.SelectedDate.HasValue)
            {
                _config.EndDate = EndDatePicker.SelectedDate.Value.DateTime;
                InvalidateServiceCache();
            }
        };
        DailyHoursSpinner.ValueChanged += (_, _) =>
        {
            if (_config != null && DailyHoursSpinner.Value.HasValue)
                _config.DailyHours = (double)DailyHoursSpinner.Value.Value;
        };
        ExcludeWeekendsCheck.IsCheckedChanged += (_, _) =>
        {
            if (_config != null)
            {
                _config.ExcludeWeekends = ExcludeWeekendsCheck.IsChecked == true;
                InvalidateServiceCache();
            }
        };
        NetworkHolidayCheck.IsCheckedChanged += (_, _) =>
        {
            if (_config != null)
                _config.EnableNetworkHolidayUpdate = NetworkHolidayCheck.IsChecked == true;
        };

        AddHolidayBtn.Click += (_, _) => AddHoliday();
        RemoveHolidayBtn.Click += (_, _) => RemoveHoliday();
        AddWorkdayBtn.Click += (_, _) => AddWorkday();
        RemoveWorkdayBtn.Click += (_, _) => RemoveWorkday();
    }

    private void ApplyDisplaySettingsToUi()
    {
        if (_settings == null) return;
        ShowProgressBarCheck.IsChecked = _settings.ShowProgressBar;
        ShowHoursCheck.IsChecked = _settings.ShowHours;
        CompactModeCheck.IsChecked = _settings.CompactMode;
    }

    private void LoadSemesterConfig()
    {
        if (_config == null) return;
        StartDatePicker.SelectedDate = new DateTimeOffset(_config.StartDate);
        EndDatePicker.SelectedDate = new DateTimeOffset(_config.EndDate);
        DailyHoursSpinner.Value = (decimal)_config.DailyHours;
        ExcludeWeekendsCheck.IsChecked = _config.ExcludeWeekends;
        NetworkHolidayCheck.IsChecked = _config.EnableNetworkHolidayUpdate;

        _holidays.Clear();
        foreach (var h in _config.CustomHolidays)
            _holidays.Add(new HolidayDisplayItem { Name = h.Name, StartDate = h.StartDate, EndDate = h.EndDate, Category = h.Category });

        _workdays.Clear();
        foreach (var w in _config.CustomWorkdays)
            _workdays.Add(new HolidayDisplayItem { Name = w.Name, StartDate = w.StartDate, EndDate = w.EndDate, Category = w.Category });
    }

    private void AddHoliday()
    {
        if (_config == null) return;
        var name = HolidayNameInput.Text ?? "自定义假期";
        var start = HolidayStartPicker.SelectedDate?.DateTime ?? DateTime.Now;
        var end = HolidayEndPicker.SelectedDate?.DateTime ?? start;
        _config.CustomHolidays.Add(new HolidayInfo { Name = name, StartDate = start, EndDate = end, Category = HolidayCategory.Custom });
        _holidays.Add(new HolidayDisplayItem { Name = name, StartDate = start, EndDate = end, Category = HolidayCategory.Custom });
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomHolidays));
    }

    private void RemoveHoliday()
    {
        if (_config == null || HolidayListBox.SelectedIndex < 0) return;
        var idx = HolidayListBox.SelectedIndex;
        if (idx < _config.CustomHolidays.Count) _config.CustomHolidays.RemoveAt(idx);
        _holidays.RemoveAt(idx);
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomHolidays));
    }

    private void AddWorkday()
    {
        if (_config == null) return;
        var name = WorkdayNameInput.Text ?? "调休补班";
        var date = WorkdayDatePicker.SelectedDate?.DateTime ?? DateTime.Now;
        _config.CustomWorkdays.Add(new HolidayInfo { Name = name, StartDate = date, EndDate = date, Category = HolidayCategory.MakeUpWorkday });
        _workdays.Add(new HolidayDisplayItem { Name = name, StartDate = date, EndDate = date, Category = HolidayCategory.MakeUpWorkday });
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomWorkdays));
    }

    private void RemoveWorkday()
    {
        if (_config == null || WorkdayListBox.SelectedIndex < 0) return;
        var idx = WorkdayListBox.SelectedIndex;
        if (idx < _config.CustomWorkdays.Count) _config.CustomWorkdays.RemoveAt(idx);
        _workdays.RemoveAt(idx);
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomWorkdays));
    }

    /// <summary>
    /// 触发 SemesterConfiguration.PropertyChanged，通知 StatisticsService 刷新缓存
    /// 并通知 StatsComponent 刷新显示
    /// </summary>
    private void NotifyConfigChanged(string propertyName)
    {
        if (_config == null) return;
        // 通过属性 setter 触发 PropertyChanged（List mutation 不自动触发）
        if (propertyName == nameof(SemesterConfiguration.CustomHolidays))
            _config.CustomHolidays = _config.CustomHolidays;
        else if (propertyName == nameof(SemesterConfiguration.CustomWorkdays))
            _config.CustomWorkdays = _config.CustomWorkdays;
        else
            InvalidateServiceCache();
    }

    private void InvalidateServiceCache()
    {
        (_statsService as StatisticsService)?.InvalidateCache();
    }
}

public class HolidayDisplayItem
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public HolidayCategory Category { get; set; }
    public string DisplayText => EndDate == StartDate
        ? $"{Name}（{StartDate:yyyy-MM-dd}）"
        : $"{Name}（{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}）";
    public override string ToString() => DisplayText;
}
