using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.SchoolStats.Controls;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SchoolStats;

[PluginEntrance]
public class SchoolStatsPlugin : PluginBase
{
    public SemesterConfiguration Config { get; private set; } = new();

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var configPath = Path.Combine(PluginConfigFolder, "settings.json");
        var pluginFolder = Path.GetDirectoryName(typeof(SchoolStatsPlugin).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var configurationStore = new PluginConfigurationStore(configPath);
        Config = configurationStore.Configuration;

        services.AddSingleton(configurationStore);
        services.AddSingleton(Config);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new LocalHolidayProvider(pluginFolder, PluginConfigFolder));
        services.AddSingleton(sp => new NetworkHolidayProvider(
            PluginConfigFolder,
            sp.GetService<ILogger<NetworkHolidayProvider>>()));
        services.AddSingleton<IHolidayProvider>(sp => new FallbackHolidayProvider(
            sp.GetRequiredService<NetworkHolidayProvider>(),
            sp.GetRequiredService<LocalHolidayProvider>(),
            sp.GetRequiredService<SemesterConfiguration>()));
        services.AddSingleton<IHolidayDataService, HolidayDataService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();

        services.AddComponent<StatsComponent, StatsComponentSettingsControl>();
    }
}
