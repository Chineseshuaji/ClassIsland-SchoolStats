using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using ClassIsland.Shared;

namespace ClassIsland.SchoolStats.Controls;

[ComponentInfo("4E8A2C7D-3F1B-4A9E-B6D5-08C1E3F7A9B2", "在校时间统计", "\uf19d", "统计学期内在校天数与时长，自动排除假期")]
public partial class StatsComponent : ComponentBase<StatsComponentSettings>
{
    private readonly IStatisticsService _statsService;
    private readonly SemesterConfiguration _config;
    private ILessonsService? _lessonsService;
    private DateTime _lastUpdateDate;
    private AggregatedStats? _cachedStats;

    public StatsComponent()
    {
        InitializeComponent();
        _statsService = IAppHost.GetService<IStatisticsService>();
        _config = IAppHost.GetService<SemesterConfiguration>();
        _lessonsService = GetLessonsServiceSafe();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SyncSettings();
        UpdateDisplay();

        // 主定时器：每分钟刷新日期检查
        if (_lessonsService != null)
            _lessonsService.PostMainTimerTicked += OnTimerTicked;

        // 监听设置变更，实时刷新组件显示
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _config.PropertyChanged += OnConfigPropertyChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_lessonsService != null)
            _lessonsService.PostMainTimerTicked -= OnTimerTicked;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _config.PropertyChanged -= OnConfigPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncSettings();
        ForceRefresh();
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ForceRefresh();
    }

    private void SyncSettings()
    {
        CompactStack.IsVisible = Settings.CompactMode;
        StandardStack.IsVisible = !Settings.CompactMode;
        ProgressBar.IsVisible = Settings.ShowProgressBar;
    }

    private void OnTimerTicked(object? sender, EventArgs e) => UpdateDisplay();

    private void UpdateDisplay()
    {
        var today = DateTime.Now.Date;

        // 同一天复用缓存数据，避免重复计算，但始终刷新 UI
        if (_cachedStats is null || _lastUpdateDate != today)
        {
            _lastUpdateDate = today;
            _cachedStats = _statsService.CalculateStats();
        }

        var stats = _cachedStats;

        DaysLabel.Text = $"{stats.PassedSchoolDays} 天 / {stats.TotalSchoolDays} 天";
        HoursLabel.Text = Settings.ShowHours
            ? $"约 {stats.PassedSchoolHours:F0} 小时 · 剩余 {stats.RemainingSchoolDays} 天"
            : $"剩余 {stats.RemainingSchoolDays} 天";

        ProgressBar.Maximum = 100;
        ProgressBar.Value = stats.ProgressPercentage;

        CompactDaysLabel.Text = $"{stats.PassedSchoolDays}/{stats.TotalSchoolDays} 天";
        CompactPercentLabel.Text = $"{stats.ProgressPercentage:F1}%";

        TooltipTitle.Text = $"在校时间统计（截至 {stats.ReferenceDate:yyyy-MM-dd}）";
        TooltipDetail.Text = $"学期：{_config.StartDate:yyyy-MM-dd} ~ {_config.EndDate:yyyy-MM-dd}\n"
            + $"已在校：{stats.PassedSchoolDays} 天（{stats.PassedSchoolHours:F0} 小时）\n"
            + $"剩余：{stats.RemainingSchoolDays} 天\n"
            + $"进度：{stats.ProgressPercentage:F1}%";

        if (stats.AppliedExclusions.Count > 0)
        {
            TooltipExclusions.Text = "已排除：\n"
                + string.Join("\n", stats.AppliedExclusions.Take(5).Select(e => $"{e.Name}：排除 {e.ExcludedDays} 天"));
        }
        else
        {
            TooltipExclusions.Text = "";
        }
    }

    public void ForceRefresh()
    {
        _lastUpdateDate = default;
        _cachedStats = null;
        UpdateDisplay();
    }

    private static ILessonsService? GetLessonsServiceSafe()
    {
        try { return IAppHost.GetService<ILessonsService>(); }
        catch { return null; }
    }
}
