using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Controls;

public partial class StatsComponentSettingsControl : UserControl
{
    private StatsComponentSettings? _settings;

    public StatsComponentSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void SetSettings(StatsComponentSettings settings)
    {
        _settings = settings;
        ApplyToUi();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ShowProgressBarCheck.IsCheckedChanged += (_, _) => { if (_settings != null) _settings.ShowProgressBar = ShowProgressBarCheck.IsChecked == true; };
        ShowHoursCheck.IsCheckedChanged += (_, _) => { if (_settings != null) _settings.ShowHours = ShowHoursCheck.IsChecked == true; };
        CompactModeCheck.IsCheckedChanged += (_, _) => { if (_settings != null) _settings.CompactMode = CompactModeCheck.IsChecked == true; };
        ApplyToUi();
    }

    private void ApplyToUi()
    {
        if (_settings == null) return;
        ShowProgressBarCheck.IsChecked = _settings.ShowProgressBar;
        ShowHoursCheck.IsChecked = _settings.ShowHours;
        CompactModeCheck.IsChecked = _settings.CompactMode;
    }
}
