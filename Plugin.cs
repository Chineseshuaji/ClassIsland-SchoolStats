using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.SchoolStats.Controls;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
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

        // 选择节假日数据源：开启网络更新则用远程 API（含法定假+调休），否则用本地 JSON
        IHolidayProvider holidayProvider;
        if (Config.EnableNetworkHolidayUpdate)
        {
            holidayProvider = new NetworkHolidayProvider();
        }
        else
        {
            holidayProvider = new LocalHolidayProvider(PluginConfigFolder);
        }
        services.AddSingleton(holidayProvider);

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

        // 后台预热节假日数据，不阻塞插件初始化
        _ = Task.Run(async () =>
        {
            var year = DateTime.Now.Year;
            await holidayProvider.GetHolidaysAsync(year);
            await holidayProvider.GetHolidaysAsync(year + 1);
        });

        services.AddComponent<StatsComponent, StatsComponentSettingsControl>();
    }
}
