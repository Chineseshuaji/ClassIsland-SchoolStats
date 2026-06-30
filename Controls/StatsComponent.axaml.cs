using System.ComponentModel;
using Avalonia.Threading;
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
    private readonly ILessonsService _lessonsService;
    private readonly IExactTimeService _exactTimeService;
    private readonly DispatcherTimer _refreshTimer = new();
    private DateTime _lastReferenceSecond;
    private AggregatedStats? _cachedStats;
    private bool _isSubscribed;

    public StatsComponent()
        : this(
            IAppHost.GetService<IStatisticsService>(),
            IAppHost.GetService<SemesterConfiguration>(),
            IAppHost.GetService<ILessonsService>(),
            IAppHost.GetService<IExactTimeService>())
    {
    }

    public StatsComponent(
        IStatisticsService statsService,
        SemesterConfiguration config,
        ILessonsService lessonsService,
        IExactTimeService exactTimeService)
    {
        _statsService = statsService;
        _config = config;
        _lessonsService = lessonsService;
        _exactTimeService = exactTimeService;
        InitializeComponent();
        _refreshTimer.Tick += (_, _) => UpdateDisplay();
        AttachedToVisualTree += (_, _) =>
        {
            if (_isSubscribed)
                return;

            _isSubscribed = true;
            SyncSettings();
            UpdateDisplay();
            RestartRefreshTimer();
            _lessonsService.PostMainTimerTicked += OnTimerTicked;
            Settings.PropertyChanged += OnSettingsPropertyChanged;
            _config.PropertyChanged += OnConfigPropertyChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (!_isSubscribed)
                return;

            _isSubscribed = false;
            _refreshTimer.Stop();
            _lessonsService.PostMainTimerTicked -= OnTimerTicked;
            Settings.PropertyChanged -= OnSettingsPropertyChanged;
            _config.PropertyChanged -= OnConfigPropertyChanged;
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncSettings();
        RestartRefreshTimer();
        ForceRefresh();
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueForceRefresh();
    }

    private void SyncSettings()
    {
        CompactStack.IsVisible = Settings.CompactMode;
        StandardStack.IsVisible = !Settings.CompactMode;
        ProgressBar.IsVisible = Settings.ShowProgressBar;
    }

    private void OnTimerTicked(object? sender, EventArgs e) => QueueUpdateDisplay();

    private void QueueForceRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ForceRefresh();
            return;
        }

        Dispatcher.UIThread.Post(ForceRefresh);
    }

    private void QueueUpdateDisplay()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateDisplay();
            return;
        }

        Dispatcher.UIThread.Post(UpdateDisplay);
    }

    private void RestartRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = GetRefreshInterval();
        _refreshTimer.Start();
    }

    private TimeSpan GetRefreshInterval()
    {
        return Settings.RemainingTimePrecision switch
        {
            RemainingTimeDisplayPrecision.Days => TimeSpan.FromMinutes(5),
            RemainingTimeDisplayPrecision.Hours => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromSeconds(1)
        };
    }

    private void UpdateDisplay()
    {
        var referenceTime = _exactTimeService.GetCurrentLocalDateTime();
        var referenceSecond = new DateTime(
            referenceTime.Year,
            referenceTime.Month,
            referenceTime.Day,
            referenceTime.Hour,
            referenceTime.Minute,
            referenceTime.Second);

        // 同一秒复用缓存数据，跟随主界面时间刷新，避免主计时器高频触发时重复计算。
        if (_cachedStats is null || _lastReferenceSecond != referenceSecond)
        {
            _lastReferenceSecond = referenceSecond;
            _cachedStats = _statsService.CalculateStats(referenceTime);
        }

        var stats = _cachedStats;
        var remainingTimeText = stats.FormatRemainingSchoolTime(Settings.RemainingTimePrecision);
        var compactRemainingTimeText = stats.FormatRemainingSchoolTime(Settings.RemainingTimePrecision, true);
        var approximateHoursText = Settings.ShowHours
            && Settings.RemainingTimePrecision != RemainingTimeDisplayPrecision.Hours
                ? $"（约 {stats.RemainingSchoolHours:F1} 小时）"
                : "";

        DaysLabel.Text = $"{stats.PassedSchoolDays} 天 / {stats.TotalSchoolDays} 天";
        HoursLabel.Text = $"实际在校时间还有 {remainingTimeText}{approximateHoursText}";
        WeekLabel.Text = $"本周剩余 {stats.CurrentWeek.RemainingSchoolHours:F1} 小时 · {stats.CurrentWeek.ProgressPercentage:F1}%";

        ProgressBar.Maximum = 100;
        ProgressBar.Value = stats.ProgressPercentage;

        CompactDaysLabel.Text = $"余 {compactRemainingTimeText}";
        CompactPercentLabel.Text = $"{stats.ProgressPercentage:F1}%";

        TooltipTitle.Text = $"在校时间统计（截至 {referenceTime:yyyy-MM-dd HH:mm:ss}）";
        TooltipDetail.Text = $"学期：{_config.StartDate:yyyy-MM-dd} ~ {_config.EndDate:yyyy-MM-dd}\n"
            + $"已在校：{stats.PassedSchoolDays} 天（{stats.PassedSchoolHours:F0} 小时）\n"
            + $"剩余实际在校时间：{remainingTimeText}（约 {stats.RemainingSchoolHours:F1} 小时）\n"
            + $"剩余完整在校日：{stats.RemainingSchoolDays} 天\n"
            + $"本周：{stats.CurrentWeek.StartDate:MM-dd} ~ {stats.CurrentWeek.EndDate:MM-dd}\n"
            + $"本周已在校：{stats.CurrentWeek.PassedSchoolDays}/{stats.CurrentWeek.TotalSchoolDays} 天"
            + $"（{stats.CurrentWeek.PassedSchoolHours:F1}/{stats.CurrentWeek.TotalSchoolHours:F1} 小时）\n"
            + $"本周剩余：{stats.CurrentWeek.RemainingSchoolDays} 天"
            + $"（{stats.CurrentWeek.RemainingSchoolHours:F1} 小时）\n"
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
        _lastReferenceSecond = default;
        _cachedStats = null;
        UpdateDisplay();
    }
}
