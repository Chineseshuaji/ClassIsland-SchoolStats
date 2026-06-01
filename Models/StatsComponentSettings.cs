using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassIsland.SchoolStats.Models;

public class StatsComponentSettings : INotifyPropertyChanged
{
    private bool _showProgressBar = true;
    private bool _compactMode;
    private bool _showHours = true;

    public bool ShowProgressBar
    {
        get => _showProgressBar;
        set { if (value != _showProgressBar) { _showProgressBar = value; OnPropertyChanged(); } }
    }

    public bool CompactMode
    {
        get => _compactMode;
        set { if (value != _compactMode) { _compactMode = value; OnPropertyChanged(); } }
    }

    public bool ShowHours
    {
        get => _showHours;
        set { if (value != _showHours) { _showHours = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
