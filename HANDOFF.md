# HanLin-ToDo 交接文档

> 接手本项目时先读这一份。它记录项目定位、当前状态、GitHub 配置、开发约定与待办。
> 维护规则见第九节：每次改动后必须同步更新，保持「实时」。

## 一、项目定位

HanLin-ToDo 是嵌在 Windows 桌面上的 Markdown 待办清单，核心诉求是「不打断当前工作」。

- 便签顶部可连续输入待办，并用输入栏旁的日历选截止日期和时间。按 Enter 创建后，输入框清空并保留焦点。
- 便签常驻桌面，按 Win + D 或点击「显示桌面」后依然可见，任务可直接勾选。
- 便签隐藏任务栏按钮。
- 勾选/取消勾选即时写回本地 Markdown，重启后恢复。
- 默认按截止日期排列未完成任务。拖动任务前的三点可设置并保存手动顺序，置顶任务排在普通任务上面。
- 清单按组组织，可单独写文字便笺。任务可置顶、加入今天，设置步骤、备注、标签、附件、提醒和重复，可搜索、批量整理或移入回收站。
- 点击铅笔可在便签内修改任务与截止时间。便签首次打开即可拖动，锁定时仍可勾选和编辑。
- 登录自启可在设置中开启。程序恢复上次位置、锁定状态、外观和任务。
- 默认使用暖白底、低饱和珊瑚红和柔和圆角。便签有紧凑、标准和宽敞三种尺寸方案，标题默认使用本机悦黑常规体，正文和尺寸可继续独立调整。
- 任务栏通知区域与 EXE 使用同一款「办」字图标，珊瑚红外框搭配造字工房悦黑常规体。

任务文件仍用 Markdown 复选框表示，本地附加数据存在 HTML 注释中。

```markdown
- [ ] 提交实验报告 📅 2026-11-25
- [x] 整理实验数据 📅 2026-11-24 17:00
```

## 二、当前状态（2026-11-24）

| 维度 | 状态 |
| --- | --- |
| 代码 | Windows 桌面版使用 WPF 与 .NET 10，便签支持连续录入、自定义清单和任务详情 |
| 仓库 | 公开：https://github.com/TangSong777/HanLin-ToDo |
| 默认分支 | main |
| 分支 | main（稳定发布）、snapshot（日常开发） |
| 规则集 | Stable releases from snapshot，已激活 |
| Actions | Stable release 工作流已注册，state=active |
| 本地 | D:\WorkSpace\HanLin-ToDo，日常开发在 snapshot |
| 打包 | 支持 Windows x64 自包含单文件构建，命令见 README |
| 桌面验证 | 上一版在 Windows 11 24H2 验证了桌面层级、Win+D、位置锁定、内联编辑、快捷录入和托盘注册 |
| 启动验证 | 验证了当前用户 Run 启动项的写入与关闭，按该命令重开可恢复任务、位置、锁定和主题 |
| 位置操作 | 标题、任务文字和空白处支持拖动，标题栏锁图标用于切换锁定状态。任务三点手柄用于排序 |
| 尺寸和字体 | 边缘及右下角可调宽高。设置支持三档尺寸、独立界面比例以及标题和正文字体选项 |
| 截止日期 | 快捷录入、便签内编辑和右键日期默认按天。需要时可加具体时刻，旧版日期时间仍可读取 |
| 任务管理 | 支持置顶、手动拖动排序、自绘右键菜单、分组清单、搜索、批量操作、回收站及 Markdown 导出 |
| 本地数据 | 兼容旧版 Markdown 清单，清单和任务详情保存在 HTML 注释中，回收站记录随任务文件备份 |
| 任务管理验证 | 上一版实测连续录入、分组清单、日历、任务详情、循环、批量恢复、导出和重开 |
| 本次验证 | 按要求完成源码和 XAML 检查及 Release 编译打包，窗口交互由用户验证 |

## 三、分支模型

| 分支 | 用途 | 更新方式 |
| --- | --- | --- |
| snapshot | 日常开发与快照 | 开工 `git pull --ff-only`，每组可提交改动后推送 |
| main | 稳定版发布 | 验证通过后发 snapshot → main 的 PR，以合并提交发布 |

「提交不等于发布」：日常提交只进 snapshot；发布点是一次 PR 合并提交。发布后保留 snapshot，继续日常开发。

## 四、GitHub 配置清单

- 可见性：PUBLIC
- 默认分支：main
- 规则集 `Stable releases from snapshot`（id 27952820，enforcement=active）
  - `deletion`：禁止删除 main
  - `non_fast_forward`：禁止强推
  - `pull_request`：必须走 PR，仅允许 merge（禁用 squash / rebase）
  - `required_status_checks`：必须通过 `snapshot-release-source`
  - `bypass_actors` 为空：管理员也不能绕过
- Actions：enabled，allowed_actions=all
- 工作流 `.github/workflows/release-source.yml`：校验 PR head 必须是本仓库的 snapshot 分支，否则失败

## 五、本地环境

- Windows + MSYS Bash / PowerShell 5.1+
- .NET 10 SDK 与 WPF。打包产物为 Windows x64 自包含 EXE。
- git 2.56.0.windows.4.niugit
- gh 2.102.0，登录账号 TangSong777（scopes：repo、workflow、gist、read:org）
- git 提交身份：TangSong777 / 2797487804@qq.com

## 六、常用命令

```powershell
# 日常
git switch snapshot
git pull --ff-only
git add -A
git commit -m "..."
git push origin snapshot

# 发布
gh pr create --base main --head snapshot --title "..." --body "..."
# 等 snapshot-release-source 变绿后合并

# 查看配置
gh repo view TangSong777/HanLin-ToDo --json visibility,defaultBranchRef
gh api repos/TangSong777/HanLin-ToDo/rulesets
gh api repos/TangSong777/HanLin-ToDo/rules/branches/main
```

## 七、开发约定

- 运行时数据位于 `%LOCALAPPDATA%\HanLin-ToDo`。`todo.md` 保存任务、清单和回收站等内容，`settings.json` 保存位置、锁定、外观、当前清单和排序。开发验证可用 `--data-dir <目录>` 隔离用户数据。
- `DesktopSurface` 使用桌面拥有的工具窗口和窗口层级管理便签。调试时发现 Windows 11 24H2 的跨进程桌面子窗口存在画面合成问题，首版采用可独立合成的桌面工具窗口。
- 图标生成器是 `tools/New-TaskbarIcon.py`。应用构建使用已生成的 ICO。字体仅用于本地生成。
- 提交信息用英文祈使句（现有提交：`Initialize stable release rules`、`Record project goals and development workflow`）。
- snapshot 上日常开发，main 在首发 PR 合并后包含稳定版。
- 不直接推 main；规则集会拒绝。

## 八、待办

- [x] 实现全局快捷键、桌面便签、Markdown 读写、排序与逾期标记
- [x] 实现便签内编辑、位置锁定、可选登录自启、恢复与主题设置
- [x] 制作并接入珊瑚红细框与悦黑「办」字图标
- [x] 补充构建、运行和打包说明
- [x] 增加便签内回车添加、清单和智能视图、搜索、任务详情、提醒与循环任务
- [x] 支持批量整理、回收站恢复和当前任务导出
- [x] 增加尺寸方案与悦黑标题，支持按天保存截止日期
- [x] 增加三点拖动排序、置顶、输入栏日历和自绘菜单，调整文字渲染和控件动效
- [ ] 首次 snapshot → main 的 PR 演练，验证规则集与检查确实生效
- [ ] 视需要补充 LICENSE、CONTRIBUTING

## 九、文档维护（实时更新）

本文件必须随项目实时更新，规则：

1. 每次改动后更新第二节「当前状态」表格。
2. 完成或新增待办时更新第八节。
3. 在文末「变更记录」追加一行，写清日期、改了什么、为什么。
4. 涉及 GitHub 配置（可见性、规则集、Actions）的改动，同步更新第四节。

## 变更记录

| 日期 | 变更 | 说明 |
| --- | --- | --- |
| 2026-11-24 | 创建交接文档 | 仓库初始化完成，可见性改为公开 |
| 2026-11-24 | 实现并验证 Windows 首版 | 补齐桌面便签的日常功能、柔和圆角主题与通知区域图标 |
| 2026-11-24 | 修正便签拖动 | 首次启动可直接拖动，扩大拖动区域，并按鼠标所在屏幕限制位置 |
| 2026-11-24 | 调整便签布局和截止输入 | 支持自由调宽高、独立界面比例、内部滚动，以及日历选日期和清除截止 |
| 2026-11-24 | 增加便签内录入和本地任务整理 | 输入后按回车连续添加，加入分组清单、文字便笺、智能视图、任务详情及批量管理 |
| 2026-11-24 | 调整尺寸、标题和截止日期 | 新增三档尺寸和悦黑标题，松开鼠标即结束尺寸调整，日历选择默认按天保存 |
| 2026-11-24 | 优化便签交互与文字渲染 | 任务可拖动和置顶，输入栏可选截止日期，统一自绘菜单和日历，阴影与文字分层 |
