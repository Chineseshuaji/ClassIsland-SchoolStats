using System.ComponentModel;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;

namespace ClassIsland.SchoolStats.Controls;

[ComponentInfo("4E8A2C7D-3F1B-4A9E-B6D5-08C1E3F7A9B2", "在校时间统计", "\uf19d", "统计学期内在校天数与时长，自动排除假期")]
public partial class StatsComponent : ComponentBase<StatsComponentSettings>, INotifyPropertyChanged
{
    private readonly IStatisticsService _statsService;
    private readonly SemesterConfiguration _config;
    private ILessonsService? _lessonsService;

    private string _daysText = "";
    private string _hoursText = "";
    private double _progress;
    private bool _compactMode;
    private bool _showProgressBar = true;
    private bool _showHours = true;

    public string DaysText { get => _daysText; set { if (value != _daysText) { _daysText = value; OnPropertyChanged(); } } }
    public string HoursText { get => _hoursText; set { if (value != _hoursText) { _hoursText = value; OnPropertyChanged(); } } }
    public double Progress { get => _progress; set { if (Math.Abs(value - _progress) > 0.01) { _progress = value; OnPropertyChanged(); } } }
    public bool CompactMode { get => _compactMode; set { if (value != _compactMode) { _compactMode = value; OnPropertyChanged(); } } }
    public bool ShowProgressBar { get => _showProgressBar; set { if (value != _showProgressBar) { _showProgressBar = value; OnPropertyChanged(); } } }
    public bool ShowHours { get => _showHours; set { if (value != _showHours) { _showHours = value; OnPropertyChanged(); } } }

    public StatsComponent()
    {
        InitializeComponent();
        _statsService = IAppHost.GetService<IStatisticsService>();
        _config = IAppHost.GetService<SemesterConfiguration>();
        _lessonsService = GetLessonsServiceSafe();
    }

    protected override void OnAttachedToVisualTree()
    {
        base.OnAttachedToVisualTree();
        SyncSettings();
        UpdateDisplay();
        if (_lessonsService != null) _lessonsService.PostMainTimerTicked += OnTimerTicked;
    }

    protected override void OnDetachedFromVisualTree()
    {
        base.OnDetachedFromVisualTree();
        if (_lessonsService != null) _lessonsService.PostMainTimerTicked -= OnTimerTicked;
    }

    private void SyncSettings()
    {
        CompactMode = Settings.CompactMode;
        ShowProgressBar = Settings.ShowProgressBar;
        ShowHours = Settings.ShowHours;
    }

    private void OnTimerTicked(object? sender, EventArgs e) => UpdateDisplay();

    private void UpdateDisplay()
    {
        var stats = _statsService.CalculateStats();
        DaysText = $"已在校 {stats.PassedSchoolDays} / {stats.TotalSchoolDays} 天";
        HoursText = ShowHours ? $"约 {stats.PassedSchoolHours:F0} 小时 · 剩余 {stats.RemainingSchoolDays} 天" : $"剩余 {stats.RemainingSchoolDays} 天";
        Progress = stats.ProgressPercentage;

        if (DaysLabel != null) DaysLabel.Text = $"{stats.PassedSchoolDays} 天 / {stats.TotalSchoolDays} 天";
        if (HoursLabel != null) HoursLabel.Text = HoursText;
        if (ProgressBar != null) { ProgressBar.IsVisible = ShowProgressBar; ProgressBar.Value = Progress; }
        if (CompactDaysLabel != null) CompactDaysLabel.Text = $"{stats.PassedSchoolDays}/{stats.TotalSchoolDays} 天";
        if (CompactPercentLabel != null) CompactPercentLabel.Text = $"{stats.ProgressPercentage:F1}%";
        if (TooltipTitle != null) TooltipTitle.Text = $"在校时间统计（截至 {stats.ReferenceDate:yyyy-MM-dd}）";
        if (TooltipDetail != null)
            TooltipDetail.Text = $"学期：{_config.StartDate:yyyy-MM-dd} ~ {_config.EndDate:yyyy-MM-dd}\n已在校：{stats.PassedSchoolDays} 天（{stats.PassedSchoolHours:F0} 小时）\n剩余：{stats.RemainingSchoolDays} 天\n进度：{stats.ProgressPercentage:F1}%";
        if (TooltipExclusions != null && stats.AppliedExclusions.Count > 0)
            TooltipExclusions.Text = "已排除：\n" + string.Join("\n", stats.AppliedExclusions.Take(5).Select(e => $"{e.Name}：排除 {e.ExcludedDays} 天"));
    }

    private static ILessonsService? GetLessonsServiceSafe()
    {
        try { return IAppHost.GetService<ILessonsService>(); }
        catch { return null; }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
