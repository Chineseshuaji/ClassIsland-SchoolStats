# ClassIsland SchoolStats

用于 ClassIsland 2.x 的在校时间统计组件。插件按学期、作息和节假日规则计算实际在校天数与时长，并持续展示本周及整个学期的进度。

## 功能

- 自动统计学期内的在校天数、已过时长和剩余时长
- 排除周末、法定节假日、寒暑假及自定义假期
- 支持法定调休补班和自定义补课日
- 支持多个作息模板，以及按星期或指定日期套用模板
- 支持按日期排除活动、停课等时间段；重叠区间只扣除一次
- 以进度条、百分比、标准模式或紧凑模式展示结果
- 使用静态学期快照与前缀累计；秒级刷新只计算当天的动态进度
- 联网节假日数据可即时启停，网络不可用时依次使用最近缓存和内置数据

## 数据口径

规则优先级从高到低为：

1. 自定义补课日
2. 自定义假期
3. 法定调休补班
4. 法定节假日
5. 周末
6. 普通在校日

“已在校天数”包含参考日期当天（当天为在校日时）；“已在校时长”只累计到当前时刻。午休和手动排除时间不计入在校时长。

内置节假日数据只包含已经由国务院办公厅正式公布的年度安排。目前的 2025、2026 年数据分别依据：

- [国务院办公厅关于 2025 年部分节假日安排的通知](https://www.gov.cn/zhengce/zhengceku/202411/content_6986383.htm)
- [国务院办公厅关于 2026 年部分节假日安排的通知](https://www.gov.cn/zhengce/zhengceku/202511/content_7047091.htm)

学校自己的寒暑假、校运会、补课等安排仍需在组件设置中添加。

## 网络与隐私

联网更新默认关闭。启用后，插件会向 `https://timor.tech/api/holiday/year/` 请求所需年份的公开节假日数据；请求不包含课程表、学校、用户或统计结果。第三方服务仍能看到网络连接通常会暴露的公网 IP，以及插件名称和版本 User-Agent。成功响应会缓存在插件配置目录，用于断网回退。

`settings.json`、节假日缓存以及损坏配置备份均保存在 ClassIsland 分配给本插件的配置目录。其中可能包含用户填写的作息、假期和活动名称，请按普通本地配置文件的敏感程度管理该目录。

## 安装

1. 从 Releases 下载最新的 `.cipx` 文件。
2. 打开 ClassIsland 设置 → 插件 → 从本地安装。
3. 添加“在校时间统计”组件并完成学期配置。

ClassIsland 1.x 与 2.x 插件不兼容；本版本要求 ClassIsland `2.1.0.1` 或更高版本。该最低版本包含上游 D-Bus 安全依赖修复。

## 开发与验证

需要 .NET SDK `8.0.422` 和 PowerShell Core。建议在 ClassIsland `2.1.0.1` 或更高的稳定版本中调试。

```powershell
dotnet restore tests/ClassIsland.SchoolStats.Tests/ClassIsland.SchoolStats.Tests.csproj --locked-mode
dotnet build tests/ClassIsland.SchoolStats.Tests/ClassIsland.SchoolStats.Tests.csproj -c Release --no-restore
dotnet test tests/ClassIsland.SchoolStats.Tests/ClassIsland.SchoolStats.Tests.csproj -c Release --no-build
dotnet publish ClassIsland.SchoolStats.csproj -c Release -p:CreateCipx=true
```

发布 Tag 必须与 `manifest.yml` 中的版本完全一致，并使用不带 `v` 前缀的四段数字格式，例如 `0.1.0.0`。CI 会运行测试、验证版本并检查唯一的 CIPX 产物和校验文件。

## 许可

[MIT License](LICENSE)
