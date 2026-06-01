# ClassIsland SchoolStats

统计学期内在校天数与时长，自动排除周末、法定节假日和寒暑假。

## 功能

- 📊 **在校时间统计**：自动计算学期内在校天数、在校小时数
- 📅 **智能排除**：自动排除周末、法定节假日、寒暑假
- 🔄 **调休处理**：支持调休补班日（补课日记为在校日）
- 🏖️ **自定义假期**：支持手动添加校运会、特殊放假等自定义假期
- 📈 **进度显示**：进度条 + 百分比直观展示学期进度
- 💾 **增量缓存**：每日增量更新，避免全量循环计算

## 安装

1. 下载最新的 `.cipx` 文件
2. 在 ClassIsland 设置 → 插件 → 从本地安装
3. 启用「在校时间统计」插件

## 开发

```bash
dotnet new install ClassIsland.PluginTemplate.Packaging
dotnet restore
dotnet build -c Release
```

## 许可

MIT License
