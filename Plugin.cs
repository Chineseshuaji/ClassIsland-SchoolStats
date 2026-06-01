using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.SchoolStats.Controls;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using ClassIsland.SchoolStats.Views.SettingsPages;
using ClassIsland.Shared.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.SchoolStats;

[PluginEntrance]
public class SchoolStatsPlugin : PluginBase
{
    public SemesterConfiguration Config { get; set; } = new();

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var configPath = Path.Combine(PluginConfigFolder, "settings.json");
        Config = ConfigureFileHelper.LoadConfig<SemesterConfiguration>(configPath);
        Config.PropertyChanged += (_, _) =>
            ConfigureFileHelper.SaveConfig(configPath, Config);

        var holidayProvider = new LocalHolidayProvider(PluginConfigFolder);
        services.AddSingleton<IHolidayProvider>(holidayProvider);

        services.AddSingleton(Config);
        services.AddSingleton<IHolidayDataService>(sp =>
        {
            var hp = sp.GetRequiredService<IHolidayProvider>();
            return new HolidayDataService(hp, Config);
        });
        services.AddSingleton<IStatisticsService>(sp =>
        {
            var hs = sp.GetRequiredService<IHolidayDataService>();
            return new StatisticsService(Config, hs, PluginConfigFolder);
        });

        var currentYear = DateTime.Now.Year;
        holidayProvider.GetHolidaysAsync(currentYear).GetAwaiter().GetResult();
        holidayProvider.GetHolidaysAsync(currentYear + 1).GetAwaiter().GetResult();

        services.AddComponent<StatsComponent, StatsComponentSettingsControl>();
        services.AddSettingsPage<SchoolStatsSettingsPage>();
    }
}
