# AIHOT Desktop

**把 AI 新知，放在桌面上。**

一款独立的 Windows AI 资讯阅读工具，提供桌面播报、阅读室、日报 / 周报 / 月报和带原文二维码的分享卡片。基于 .NET 10 与 WPF，自有代码采用 MIT 许可证。

> 本项目不是 AIHOT 官方客户端。资讯来自第三方数据源，摘要仅供快速浏览，请以原文为准。

## 界面预览

以下为维护者提供的真实程序截图，拍摄于 2026-09-16。截图中的文章、日期和数量仅展示当时的界面状态，不代表实时数据或对新闻内容的核实。

### 桌面播报

在桌面查看资讯标题和来源，点击跳转原文；支持位置锁定。

![AIHOT 桌面播报栏](docs/images/desktop-ticker.png)

### 阅读室

左侧搜索、分类和浏览列表，右侧阅读摘要；可切换资讯、日报、周报和月报。

![AIHOT 阅读室：资讯列表与正文](docs/images/reading-room.png)

### 分享卡片

编辑标题与摘要、切换版式，预览后复制图片或保存 PNG，二维码指向原文。

![AIHOT 分享卡片编辑与预览](docs/images/share-editor.png)

## 主要功能

- **当天动态**：按北京时间筛选当天内容，自动获取全部分页，按发布时间从新到旧排列。
- **桌面播报**：轮播标题、查看来源、点击阅读原文，不注册 Ctrl+Alt+A 全局快捷键。
- **阅读室**：支持关键词搜索、分类浏览和明暗主题切换。
- **周期报告**：根据本机已收录的数据生成日报、周报和月报，不代表数据源的完整历史。
- **分享卡片**：多种版式、原文二维码、图片复制与 PNG 导出。
- **本地存储**：设置、缓存和历史保存在本机。Telegram 推送为可选功能。

当天资讯来自 AIHOT 数据源，随数据源更新；具体内容以阅读室实际展示为准。

## 获取与运行

### 直接下载使用

查看 [Releases](https://github.com/YuMir1688/AIHOT-Desktop/releases) 的版本说明后下载程序包，解压到独立目录并运行 `AIHOT.Desktop.exe`。

**当前最新 Release 仍是 2026-09-10 的旧包，不包含后续所有功能。** 想体验当前源码版本，请按下一节构建。仓库的 `app/` 目录是历史打包程序，不包含运行环境，不应视为最新源码的自动构建产物。

更新前先退出旧程序，保留旧目录作备份。不要混用不同版本的 DLL。替换程序目录不会自动迁移另一台电脑上的用户数据。

### 从源码构建

需要 Windows 10/11、PowerShell 7 和 .NET 10 SDK。在项目根目录运行：

```powershell
./build.ps1
./test.ps1
./artifacts/app/AIHOT.Desktop.exe
```

产物位于 `artifacts/app/`。构建不会替换已安装的程序；运行产物需要 .NET 10 Desktop Runtime，安装 SDK 的开发机已包含该环境。

测试默认离线，覆盖分页、超过 100 条、日期边界、去重、Telegram 待发送队列和分享渲染连接。需要联网验证时运行：

```powershell
dotnet run --project modules/HotTests/Test.csproj -- --live
```

构建、测试与当前限制详见 [开发说明](docs/DEVELOPMENT.md)。自动构建状态见 [GitHub Actions](https://github.com/YuMir1688/AIHOT-Desktop/actions)。

## 数据与隐私

本地数据目录：`%LOCALAPPDATA%\YuMir\AIHOT.Desktop`。

- 程序需要联网读取资讯；访问原文会打开外部网站。
- 周期报告只反映本机已保存的内容，长时间未运行时可能存在缺失。
- 不要上传配置、缓存、历史、推送状态或访问凭据。
- Telegram 配置见 [可选推送说明](docs/TELEGRAM.md)，未配置时无需启用。

## 项目结构

| 目录 | 用途 |
| --- | --- |
| `core/` | 主程序 C# / XAML 源码 |
| `src/` | 阅读室、报告、分享卡片等增强模块 |
| `modules/` | 数据读取、构建补丁及测试 |
| `tools/` | 构建连接与验证工具 |
| `lib/` | 历史兼容引用及第三方依赖 |
| `app/` | 历史打包程序，不等同于最新源码构建 |
| `docs/` | 文档与真实界面截图 |

## 参与与许可

欢迎通过 [Issues](https://github.com/YuMir1688/AIHOT-Desktop/issues) 反馈问题，提交时请说明版本、操作步骤和预期结果，并遮蔽私人信息。

[贡献指南](CONTRIBUTING.md) · [安全说明](SECURITY.md) · [MIT 许可证](LICENSE) · [第三方说明](THIRD_PARTY_NOTICES.md)

MIT 仅覆盖项目自有代码；第三方组件、新闻、摘要、图片及品牌名称仍遵循各自许可与权利范围。

项目创作与维护｜YuMir
