# 寒淋待办 · HanLin-ToDo

<img src="docs/icon.png" width="72" height="72" alt="寒淋待办的「办」字图标">

一个 Windows 本地待办便签：**数据全在本机，不需要账号，不联网**。

HanLin-ToDo 是嵌在 Windows 桌面上的 Markdown 待办清单。便签常驻桌面层，按 Win + D 后依然可见；任务直接读写本地 Markdown 文件，支持截止日期、提醒、重复、标签、步骤和附件。界面采用 HanLin 系列的暖白、珊瑚红配色。[HanLin 系列设计规范](docs/HANLIN-SERIES.md)

> 任务文件是普通 Markdown，任何编辑器都能打开和备份。

| | |
| --- | --- |
| 版本 | `1.1.0` |
| 运行环境 | Windows 10 / 11 x64，.NET 10 自包含单文件 |
| 发布产物 | `HanLin-ToDo-Setup-<版本>.exe`（安装包）、`HanLin-ToDo.exe`（便携单文件） |
| 许可证 | 未声明（仓库暂无 LICENSE 文件）

## 目录

**按意图查找**

| 我想… | 看这里 |
| --- | --- |
| 先跑起来看看 | [快速开始](#快速开始) |
| 知道便签能做什么 | [功能](#功能) |
| 查快捷键 | [快捷键总表](#快捷键总表) |
| 改字体、尺寸、颜色 | [外观与设置](#外观与设置) |
| 换任务文件位置 | [工作目录](#工作目录) |
| 知道数据存在哪、怎么备份 | [数据与备份](#数据与备份) |
| 自己编译打包 | [构建与打包](#构建与打包) |
| 了解桌面层和输入法焦点 | [实现要点](#实现要点) |
| 发版 | [版本与发布](#版本与发布) |
| 确认某个行为是不是 bug | [已知限制](#已知限制) |

**全部章节**

使用向：[快速开始](#快速开始) · [这个项目改了什么](#这个项目改了什么) · [功能](#功能) · [安装](#安装) · [使用](#使用) · [外观与设置](#外观与设置) · [数据与备份](#数据与备份) · [已知限制](#已知限制)

开发向：[实现要点](#实现要点) · [构建与打包](#构建与打包) · [项目结构](#项目结构) · [版本与发布](#版本与发布)

其它：[致谢与许可](#致谢与许可)

## 快速开始

1. 从 [Releases](https://github.com/TangSong777/HanLin-ToDo/releases/latest) 下载 `HanLin-ToDo.exe`，放到任意目录双击运行。
2. 首次启动会在 EXE 同级创建 `Data` 文件夹和 `todo.md`，便签出现在主屏幕右上方。
3. 在便签顶部输入一行，按 **Enter** 添加。点击输入栏旁的日历（或在输入框按 **Tab**）可以先选截止日期。
4. 按 **Ctrl+Alt+T** 随时打开录入窗口，焦点直接落在任务名称输入框。

任务就是 Markdown 复选框，可以用任何编辑器打开 `todo.md`：

```markdown
- [ ] 提交实验报告 📅 2026-11-25
- [x] 整理实验数据 📅 2026-11-24 17:00
- [ ] 备份项目文件
```

## 这个项目改了什么

| 类别 | 内容 |
| --- | --- |
| 本项目独有 | 桌面层便签（Win + D 后仍可见、不占任务栏）、按显示器布局分别记忆位置和尺寸、Markdown 直接读写、全套自绘主题控件 |
| 高度定制 | 拖动排序、日历选日期、键盘录入、托盘菜单、撤销删除提示、字体选择 |
| 复用 | WPF、.NET 10、Windows Forms 的 `Screen` API、Markdown 文件格式 |

## 功能

### 桌面便签

便签是独立顶层工具窗口，Z 序位于桌面图标之上、普通窗口之下。按 **Win + D** 或点击「显示桌面」后依然可见，普通窗口重叠时覆盖便签。便签不显示任务栏按钮。

### 快速录入

在便签顶部输入一行，按 **Enter** 添加。输入栏旁的日历可选截止日期，也能在展开的一行填具体时间。保存后输入栏清空并保留焦点，可以连续输入下一条。

### 截止日期与提醒

选中日期后默认为「全天」，次日开始算逾期。可以选择或输入 24 小时制时间。右键已有待办并选择「编辑任务」，可在便签内修改日期。提醒由正在运行的 HanLin-ToDo 通过 Windows 通知区域发送。

### 任务管理

点击左下角的筛选入口查看全部、置顶、有日期或提醒、逾期、已完成或回收站。在同一菜单中可以更改排序、批量删除或导出当前任务。点击复选框完成或撤回完成，右键菜单可编辑、复制或删除任务。删除后左下角显示 5 秒的撤销提示，之后可在回收站恢复。「任务详情」可设置标签、备注、步骤、提醒、重复、本地文件和链接。

### 拖动排序与置顶

将鼠标移到待办左侧三点，光标会变成手形。拖动时整行半透明预览随鼠标移动，松手插入标线处，按 **Esc** 可取消。拖动后切换到手动顺序并保存。用右键菜单置顶或取消置顶，置顶项和普通项各自在组内排序。

### 搜索

点击左下角的放大镜或按 **Ctrl+F**，搜索任务标题、备注、标签、步骤、附件和附件文件名。

### 外观与设置

默认尺寸 236 × 411，界面比例 100%，正文字号 14，圆角 8，行间留白 6。解锁后可拖动便签边缘或右下角调整宽高，也可在设置里手动输入宽、高和界面比例。窗口较小时，任务列表在标题与底部之间滚动。

正文和标题都可分别选择微软雅黑、宋体或悦黑体。默认正文微软雅黑、标题悦黑体，本机缺少悦黑时自动回退到正文所用字体。

### 显示器布局

便签按当前显示器布局记住位置和尺寸。串流单屏和实验室双屏来回切换时，会自动回到该布局上次的位置和大小。

### 工作目录

在「设置 → 数据」选择存放 `todo.md` 的文件夹。所选目录已有 `todo.md` 时直接读取，否则复制当前任务文件。点「默认」可选回 EXE 同级的 `Data` 文件夹，保存后立即使用。

### 附件

把文件拖到某条任务上，文件会复制进工作目录的 `Attachments` 文件夹，原文件移动或删除都不影响便签里的副本。任务右侧出现回形针图标，点开即可打开；多个附件时弹出列表选择。右键图标可以显示所在文件夹、复制路径、改显示名或移除记录。详情页的「附件文件」也能添加和移除。附件文件名参与搜索，导出 Markdown 时写成相对链接。

### 托盘与自启

程序启动后恢复位置、尺寸、界面比例、锁定状态、外观和任务。首次运行默认开启登录自启，可在「设置 → 常规」关闭。通知区域右键菜单可显示、隐藏或退出。便签内按 `Ctrl+,` 打开设置。 [HanLin 系列设计规范](docs/HANLIN-SERIES.md)

## 安装

### 运行

运行 `HanLin-ToDo-Setup-<版本>.exe`，选择安装位置并确认。安装为当前用户添加开始菜单入口。已有便携版时，选其现有目录即可继续读取程序旁的任务。

也可双击单文件便携版 `HanLin-ToDo.exe`，它内含 .NET 运行时。

### 数据位置

| 内容 | 位置 |
| --- | --- |
| 任务文件 | 默认 EXE 同级 `Data\todo.md`，可在设置中改到任意目录 |
| 附件副本 | 工作目录下的 `Attachments`，拖进便签的文件都复制到这里 |
| 设置 | `%LOCALAPPDATA%\HanLin-ToDo\settings.json` |
| 旧版任务 | `%LOCALAPPDATA%\HanLin-ToDo\todo.md`，升级后首次启动时复制到缺少任务文件的目标 |

### 卸载

安装版可在 Windows「设置 → 应用」卸载，任务文件保留在数据文件夹中。便携版可退出后移除 EXE。登录自启项可在设置中关闭。

## 使用

### 快捷键总表

| 操作 | 效果 |
| --- | --- |
| `Enter`（输入框） | 添加任务；在任务名称输入框保存 |
| `Ctrl+Alt+T` | 打开录入窗口，焦点直接落在任务名称输入框 |
| `Tab` | 从任务名称进入日历 |
| `←` / `→` | 日历内按天移动 |
| `↑` / `↓` | 日历内按周移动 |
| `Enter`（日历内） | 确认日期，焦点回到输入框 |
| `Enter`（输入框，选日之后） | 保存任务 |
| `Esc` | 取消这次选日 / 取消拖动 |
| `Ctrl+F` | 搜索任务 |
| `Win+D` | 回到桌面，便签仍可见 |

### 键盘录入流程

按 **Ctrl+Alt+T** 打开自绘录入窗口，焦点直接落在任务名称输入框，可以马上输入。输入任务名称后按 **Tab** 打开日历。左右方向键按天移动，上下按周移动，按 **Enter** 确认日期，焦点回到输入框，再按 **Enter** 保存。在任务名称输入框直接按 **Enter** 可跳过选日期，日历内按 **Esc** 可取消这次选日。

### 任务文件格式

任务文件用 Markdown 复选框表示完成状态，本地附加数据存在 HTML 注释中。旧版任务统一出现在「全部」视图，原星标会作为置顶导入。任务详情和回收站使用同一 Markdown 文件内的 HTML 注释存储，旧清单和便笺的内容也保存在这些注释里。普通 Markdown 预览仍会显示任务列表。手动编辑时可保留这些注释，复制 Markdown 即可备份数据。

## 数据与备份

任务文件是纯 Markdown，复制 `todo.md` 即可备份。设置保存在 `%LOCALAPPDATA%\HanLin-ToDo\settings.json`，包含工作目录选择、位置、锁定状态、外观和显示器布局记录。开发验证可用 `--data-dir <目录>` 固定独立的任务和设置目录。

## 已知限制

| 现象 | 原因 |
| --- | --- |
| 便签被普通窗口覆盖 | 便签位于桌面层，这是有意设计，不是置顶窗口 |
| 移动程序后登录自启失效 | 启动项引用当时的 EXE 路径，在新位置运行一次并保存启动设置 |
| 悦黑体显示为雅黑 | 本机未安装造字工房悦黑常规体，自动回退到正文所用字体 |
| 手动编辑 `todo.md` 后任务详情丢失 | 任务详情、回收站和旧清单元数据存在 HTML 注释里，编辑时需保留 |

## 实现要点

### 桌面层窗口

`DesktopSurface` 使用独立顶层工具窗口承载便签。桌面窗口句柄用于定位 Z 序，窗口的位置和尺寸使用屏幕坐标，移到不同 DPI 的显示器后按新 DPI 校正。

### 输入法焦点

便签从挂靠 Progman 改为独立顶层窗口，并用 `SetWindowPos` 把窗口放到 `HWND_BOTTOM`，修复中文输入法焦点丢失。详见 [docs/IME-FOCUS-FIX.md](docs/IME-FOCUS-FIX.md)。

### 显示器布局签名

`DisplayLayout.Signature()` 读取 `Screen.AllScreens`，按 `Bounds.Left` / `Bounds.Top` 排序后拼接每屏的 `Bounds`、`WorkingArea` 和主屏标志，取 SHA256 前 16 位十六进制作为布局签名。`NotePreferences.Placements` 按签名保存 `NotePlacement`（Left / Top / Width / Height / Scale）。切换显示器时用 `preferences.Left` / `Top` 保存旧布局，而不是窗口实际矩形，避免 Windows 自动移动窗口污染记录。

### 数据存储

`Storage.cs` 负责读写 `todo.md` 和 `settings.json`。任务详情、回收站和旧清单元数据以 HTML 注释形式随 Markdown 保存，普通 Markdown 预览不受影响。附件由 `Attachments.cs` 复制到工作目录的 `Attachments`，任务里只记相对路径、原文件名和大小。

## 构建与打包

### 构建

需要 Windows 和 .NET 10 SDK（WPF 工作负载随 SDK 提供）。

```powershell
dotnet run --project .\src\HanLin.ToDo
dotnet build .\src\HanLin.ToDo\HanLin.ToDo.csproj -c Release -warnaserror
```

### 打包

```powershell
.\tools\Publish-Windows.ps1
```

脚本用 `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` 生成单文件，默认更新 `D:\桌面\HanLin-ToDo\HanLin-ToDo.exe`，可用 `-OutputDirectory` 指定其他输出目录。打包前会结束正在运行的便签，替换后自动启动新版本（`-SkipLaunch` 可跳过）。已有 EXE 无法替换时，脚本会在同一目录生成带时间戳的新版。打包只写入 EXE。

### 图标

图标用本机安装的造字工房悦黑常规体绘制「办」，沿用原输入法图标的珊瑚红外框。仓库包含生成好的多尺寸 ICO。需要重新绘制时，用 Python 和 Pillow 运行 `tools/New-TaskbarIcon.py`，传入本机字体文件和 ICO 输出路径。

用 PowerShell 7 和 Inno Setup 6.6 或更新版本构建系列安装向导。

```powershell
pwsh -File .\tools\Build-Installer.ps1
```

输出在 `installer/output`。脚本在暂存目录生成便携 EXE，再编译安装包。

## 项目结构

```text
HanLin-ToDo/
├── src/HanLin.ToDo/
│   ├── App.xaml.cs            应用启动、托盘、快捷键、显示器布局
│   ├── DesktopSurface.cs      桌面层窗口与 Z 序
│   ├── DisplayLayout.cs       显示器布局签名
│   ├── Storage.cs             任务与设置读写、字体常量
│   ├── Tasks.cs               任务模型与 Markdown 解析
│   ├── Assets/                应用图标
│   └── Views/                 便签、录入、设置、日历、菜单等界面
├── tools/
│   ├── Publish-Windows.ps1    单文件打包与部署
│   └── New-TaskbarIcon.py     图标生成
├── docs/IME-FOCUS-FIX.md      输入法焦点修复说明
├── CHANGELOG.md               版本历史
├── PENDING-CHANGES.md         待发布改动账本
├── HANDOFF.md                 交接文档
└── README.md
```

## 版本与发布

### 分支模型

```text
snapshot ──► main ──► tag + Release
```

| 分支 | 用途 | 更新方式 |
| --- | --- | --- |
| `snapshot` | 日常开发与快照 | 开工时 `git pull --ff-only`，每组可提交改动后推送 |
| `main` | 稳定版发布 | 验证通过后 `git merge --ff-only snapshot`，推送后打 tag |

`main` 永不直接提交，提交不等于发布。日常提交只进 `snapshot`；发布点是一次 `snapshot → main` 的合并。发布后保留 `snapshot`，继续日常开发。

`main` 的规则集 `Stable releases from snapshot` 要求通过 `snapshot-release-source` 检查并经由 Pull Request 合并，同时禁止直接推送、强制推送和删除 `main`。发布时临时禁用规则集，`--ff-only` 合并推送后立即恢复。

### 版本规则

| 位 | 何时升 |
| --- | --- |
| MAJOR | 不兼容的大改 |
| MINOR | 成体系的新能力 |
| PATCH | 修 bug、调参数、局部小功能 |

混合改动取最高位。

### 版本历史

| 版本 | 说明 |
| --- | --- |
| 1.1.0 | 设置四页、系列安装向导、C 案「办」字图标、Build-Installer 打包 |
| 1.0.3 | 便签改为 Explorer 桌面拥有，修 Win+D 覆盖 |
| 1.0.2 | 显示器布局记忆、正文与标题三字体、键盘两步 Enter、自绘字体下拉 |
| 1.0.1 | 自绘新建待办窗口、锁定尺寸、默认外观调整 |
| 1.0.0 | 首个正式版本 |

### 发布流程

```powershell
# 日常
git switch snapshot
git pull --ff-only
git add -A
git commit -m "..."
git push origin snapshot

# 发版
# 1. 读 PENDING-CHANGES.md，汇总进 CHANGELOG.md 与 Release 说明
# 2. 更新 src/HanLin.ToDo/HanLin.ToDo.csproj 的 Version
# 3. 清空 PENDING-CHANGES.md，标题改为「自 <新版本> 起」
# 4. 提交并推送 snapshot
# 5. .\tools\Build-Installer.ps1 打包（暂存目录同时产出自包含单文件 EXE），记录资产 SHA256
# 6. 临时禁用 main 规则集，git switch main && git merge --ff-only snapshot，推送后恢复
# 7. git tag -a v<版本> -m "..."，推送 tag
# 8. gh release create v<版本> --repo TangSong777/HanLin-ToDo --title "..." --notes-file ... --latest
# 9. 切回 snapshot，核验 release 与 tag
```

> 打包与验证在合并之前完成。`main` 只接受 `--ff-only` 合并，保证发布点与 `snapshot` 的提交完全一致。

## 致谢与许可

- [WPF](https://github.com/dotnet/wpf)：桌面界面框架
- [Windows Forms](https://github.com/dotnet/winforms)：`Screen` API 与通知区域
- [造字工房悦黑](https://www.zcool.com.cn/special/zcoolfonts/)：图标与标题字体

仓库暂无 LICENSE 文件。在补充许可证之前，保留所有权利。
