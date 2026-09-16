# AIHOT Desktop · YuMir 的 AI 阅读角落

独立的 Windows AI 资讯桌面客户端：桌面播报、阅读室、日报/周报/月报以及带原文二维码的分享卡片。项目不是 AIHOT 官方客户端。

## 当前版本

- 北京时间当天的全部动态，自动翻页、按发布时间排序，不凑 100 条。
- 点击资讯打开接口提供的原文链接。
- 阅读室支持分类、搜索和历史报告；分享卡片支持多种样式。
- 不注册 Ctrl+Alt+A 全局快捷键。
- 缓存与设置保存在本机，不随源码分发。Telegram 推送为可选配置。

## 从源码构建

环境：Windows 10/11、PowerShell 7、.NET 10 SDK。运行生成程序需要 .NET 10 Desktop Runtime；安装 SDK 的开发机已包含该运行环境。

```powershell
./build.ps1
./test.ps1
./artifacts/app/AIHOT.Desktop.exe
```

源码构建不会替换已安装的程序，也不会迁移用户资料。发布目录为 `artifacts/app`，依赖系统安装的运行环境；旧 Releases 包不代表最新源码。

`core/` 已补入主程序源码。构建先编译主程序，再编译 `src/` 增强模块和 `modules/` 数据模块，最后通过有源码的 Mono.Cecil 构建工具连接这些历史模块。`lib/AIHOT.Desktop.dll` 是旧兼容引用，构建时会被源码生成的版本替换；不再依靠它作为唯一的主程序实现。后续计划逐步用普通项目引用取代程序集补丁。

测试默认离线，覆盖分页、130 条不截断、跨日、去重、Telegram 去重及分享渲染连接。可选真实数据测试：`dotnet run --project modules/HotTests/Test.csproj -- --live`。首次开源整理的构建已验证，但从旧源码重建的界面细节仍需人工回归；不要假定与历史打包二进制逐字节一致。

## 许可与参与

项目自有代码采用 [MIT](LICENSE)；第三方组件与资讯内容不包含在此授权中，见 [第三方说明](THIRD_PARTY_NOTICES.md)。参与方式见 [CONTRIBUTING](CONTRIBUTING.md)，安全说明见 [SECURITY](SECURITY.md)。

项目创作与维护｜YuMir

---

## 历史维护记录（不作为当前构建说明）

2026-09-10 更新：日报/周报/月报长图、独立资讯挑选面板、统一深色控件、固定署名与标题平滑滚动。

2026-09-11 更新：桌面播报满 100 条时的状态文案由 `TOP100` 精简为 `TOP`，资讯数量与补齐规则不变。

## 公司电脑替换

1. 从 Releases 下载完整程序压缩包，解压到一个新文件夹。
2. 从托盘退出旧版 AIHOT，再双击新文件夹中的 AIHOT.Desktop.exe。
3. 运行正常后，可移走旧版快捷方式，再为新版 exe 创建桌面快捷方式。旧程序文件夹先留作备份。

同一 Windows 账户的资讯存档与设置仍保存在本机 AppData，不在这个仓库中。另一台电脑不会自动获得家里电脑的存档。

## 文件说明

- app：已验证的更新程序；仓库版本不含 runtime，替换旧版时请保留原 runtime 文件夹。首次使用建议下载 Releases 完整包。
- src：本次增强模块的全部 C# 源码。
- tools：补丁工具与离屏验证工具。
- lib：原始主程序程序集及编译依赖。

这是一份增强模块源码和程序备份，原主程序以程序集保存，并非原主程序的完整源码工程。

## 编译

Windows 上安装 .NET 10 SDK，运行：

    dotnet build tools/CardTools.csproj -c Release

把 src/bin/Release/net10.0-windows/AIHOT.Cards.dll 复制到程序目录即可更新增强模块。重建主程序集补丁时，以 app/AIHOT.Desktop.dll 为输入，输出到另一个文件名：

    dotnet tools/bin/Release/net10.0-windows/CardTools.dll patch app/AIHOT.Desktop.dll src/bin/Release/net10.0-windows/AIHOT.Cards.dll patched.dll

不要将 test-host 生成的测试程序集用于正式程序。

## Telegram 自动推送

程序运行时跟随现有精选刷新，约每 5 分钟获取资讯，发现新条目后发到配置频道。首次运行仅建立基线，不补发历史。已发送 ID 与待发送队列保存在本机；明确失败会重试，结果不确定的中断请求不盲目重发。

本地配置位于 %LOCALAPPDATA%/YuMir/AIHOT.Desktop/telegram.local.json，字段为 bot_username、bot_token、channel_id；可添加 enabled:false 暂停。Token 不包含在源码、程序包中。状态写入同目录 telegram-status.txt。另一台电脑须单独配置，建议只在一台电脑启用以免跨电脑重复。

## 2026-09-11 热榜与原文跳转更新

桌面播报现跟随 AIHOT 网站过去 48 小时热点榜，按网站事件顺序展示，不再使用当天精选或补齐 100 条规则。点击直达该榜单摘要对应报道的原始网址；原文链接通过事件内同标题报道及其原文按钮核对，不猜测链接。分享落款统一为 `YUMIR / 每天一点AI新知`。

热榜使用独立的 hot-cache.json，不改原有资讯历史。网站结构变化或原文解析失败时保留上次成功缓存。热榜读取依赖网站公开页面结构，后续页面变动可能需要更新解析器。

新增源码和验证工具位于 modules。编译：`dotnet build modules/HotFeed/HotFeed.csproj -c Release`。运行验证：`dotnet run --project modules/HotTests/Test.csproj`。测试使用独立临时缓存，不改用户数据。

HotPatch 可将主程序集的获取、展示与元信息方法接到热榜模块，并生成对应 deps.json。输入主程序集和模块路径，输出必须使用另一个文件名。同步包含 app 中已安装的程序集、热榜模块及依赖清单；此次未更新 Releases 压缩包，下载旧 Release 不包含本次改动。
## 2026-09-14 今日全部动态
桌面播报与阅读面板统一读取全部动态接口的所有分页，按北京时间显示当天内容，最新在前，无 TOP100 上限。独立 today-all-cache.json 缓存，跨日不展示昨日内容；原文链接沿用接口字段。已验证分页、130 条无截断、去重、日期边界及真实接口。此次未更新 Release 压缩包。
