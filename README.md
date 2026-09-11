# AIHOT 桌面播报 · 私有备份

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
