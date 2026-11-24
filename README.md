# HanLin-ToDo

HanLin-ToDo 是嵌在 Windows 桌面上的 Markdown 待办清单。

## 使用

- **直接添加**。在便签顶部输入一行，按 **Enter** 添加。点击输入栏旁的日历即可先选截止日期，也能在展开的一行填具体时间。保存后输入栏清空并保留焦点，可以继续输入下一条。
- **设置截止日期**。选中日期后默认为「全天」，次日开始算逾期。可以选择或输入 24 小时制时间。标题栏的加号、**Ctrl+Alt+T** 和托盘图标可打开完整录入框。右键已有待办并选择「编辑任务」，可在便签内修改日期。
- **键盘新建**。按 **Ctrl+Alt+T** 打开自绘录入窗口，焦点直接落在任务名称输入框，可以马上输入。输入任务名称后按 **Tab** 打开日历。左右方向键按天移动，上下按周移动，按 **Enter** 确认日期，焦点回到输入框，再按 **Enter** 保存。在任务名称输入框直接按 **Enter** 可跳过选日期，日历内按 **Esc** 可取消这次选日。
- **拖动与置顶**。将鼠标移到待办左侧三点，光标会变成手形。拖动时整行半透明预览随鼠标移动，松手插入标线处，按 Esc 可取消。拖动后切换到手动顺序并保存。用右键菜单置顶或取消置顶，置顶项和普通项各自在组内排序。
- **整理任务**。点击左下角的筛选入口查看全部、置顶、有日期或提醒、逾期、已完成或回收站。在同一菜单中可以更改排序、批量删除或导出当前任务。
- **完成和删除**。点击复选框完成或撤回完成，右键菜单可编辑、复制或删除任务。删除后左下角会显示 5 秒的撤销提示，之后可在回收站恢复。「任务详情」可设置标签、备注、步骤、提醒、重复、本地文件和链接。
- **搜索**。点击左下角的放大镜或按 **Ctrl+F**，搜索任务标题、备注、标签、步骤和附件。
- 按 **Win+D** 或用「显示桌面」回到桌面。便签位于桌面层，普通窗口重叠时覆盖便签。解锁后可从标题栏拖动便签。点按锁图标同时固定位置和尺寸。
- 解锁后可拖动便签边缘或右下角调整宽高，也可在设置里手动输入宽、高和界面比例。窗口较小时，任务列表在标题与底部之间滚动。
- 默认尺寸为 236 × 411，界面比例 100%，正文字号 14，圆角 8，行间留白 6。便签设置可手动调整外观。
- 正文和标题都可分别选择微软雅黑、宋体或悦黑体。默认正文微软雅黑、标题悦黑体，本机缺少悦黑时自动回退到正文所用字体。
- 在「便签设置 → 工作目录」选择存放 `todo.md` 的文件夹。所选目录已有 `todo.md` 时直接读取，否则复制当前任务文件。点「默认」可选回 EXE 同级的 `data` 文件夹，保存后立即使用。
- 程序启动后恢复位置、尺寸、界面比例、锁定状态、外观和任务。首次运行默认开启登录自启，可在便签设置中关闭。通知区域右键菜单可显示、隐藏或退出。
- 便签按当前显示器布局记住位置和尺寸。串流单屏和实验室双屏来回切换时，会自动回到该布局上次的位置和大小。

首次运行时便签在主屏幕右上方，程序会自动创建 EXE 同级的 `data` 文件夹。升级后首次启动会在目标缺少 `todo.md` 时复制旧版的 `%LOCALAPPDATA%\HanLin-ToDo\todo.md`。工作目录选择、位置和外观仍记录在 `%LOCALAPPDATA%\HanLin-ToDo\settings.json`。设置及托盘菜单都有「打开任务文件」入口。提醒由正在运行的 HanLin-ToDo 通过 Windows 通知区域发送。本地附件保存文件路径。

任务文件示例

```markdown
- [ ] 提交实验报告 📅 2026-11-25
- [x] 整理实验数据 📅 2026-11-24 17:00
- [ ] 备份项目文件
```

旧版任务统一出现在「全部」视图，原星标会作为置顶导入。任务详情和回收站使用同一 Markdown 文件内的 HTML 注释存储，旧清单和便笺的内容也保存在这些注释里。普通 Markdown 预览仍会显示任务列表。手动编辑时可保留这些注释，复制 Markdown 即可备份数据。

## 构建与打包

从 `snapshot` 开发分支打开项目，使用 Windows 上的 .NET 10 SDK。

```powershell
dotnet run --project .\src\HanLin.ToDo
.\tools\Publish-Windows.ps1
```

本地发布默认更新 `D:\桌面\HanLin-ToDo\HanLin-ToDo.exe`，可用 `-OutputDirectory` 指定其他输出目录。已有 EXE 无法替换时，脚本会在同一目录生成带时间戳的新版。打包只写入 EXE。启用登录自启后，Windows 启动项会引用当时的 EXE 路径。移动程序后，在新位置运行一次并保存启动设置。

图标用本机安装的造字工房悦黑常规体绘制「办」，沿用原输入法图标的珊瑚红外框。仓库包含生成好的多尺寸 ICO。需要重新绘制时，用 Python 和 Pillow 运行 `tools/New-TaskbarIcon.py`，传入本机字体文件和 ICO 输出路径。

## Git 工作流

本地工作区为 `D:\WorkSpace\HanLin-ToDo`，GitHub 仓库为 [TangSong777/HanLin-ToDo](https://github.com/TangSong777/HanLin-ToDo)。

| 分支 | 用途 | 更新方式 |
| --- | --- | --- |
| `snapshot` | 日常开发与快照 | 开工时执行 `git pull --ff-only`，每次完成一组可提交的改动后推送 |
| `main` | 稳定版发布 | 验证通过后发起 `snapshot → main` 的 Pull Request，并以合并提交发布 |

日常操作

```powershell
git switch snapshot
git pull --ff-only
# 完成一组修改后，暂存本次改动并提交
git push origin snapshot
```

启用 `main` 规则集后，GitHub 要求通过 `snapshot-release-source` 检查并经由 Pull Request 合并，同时禁止直接推送、强制推送和删除 `main`。发布后保留 `snapshot`，继续日常开发。

初始化配置将仓库设为公开。公开仓库可免费启用 GitHub 的分支规则集，Actions 也不占用私有仓库的分钟数配额。 [GitHub 规则集说明](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets)

## 发布

发布遵循 snapshot → main 的固定顺序，提交不等于发布。

1. 读 `PENDING-CHANGES.md` 全部条目，汇总进 `CHANGELOG.md` 与 GitHub Release 说明。
2. 按改动类型确定版本号（MAJOR / MINOR / PATCH），更新 `src/HanLin.ToDo/HanLin.ToDo.csproj` 的 `Version`。
3. 清空 `PENDING-CHANGES.md`，标题改为「自 <新版本> 起」。
4. 提交并推送 `snapshot`。
5. 运行 `tools/Publish-Windows.ps1` 打包，记录 EXE 的 SHA256。
6. 合并到 `main`：`git switch main && git merge --ff-only snapshot`。`main` 的规则集要求 PR，发布前需临时禁用规则集，推送后立即恢复。
7. 打 tag：`git tag -a v<版本> -m "..."`，推送 tag。
8. `gh release create v<版本> --repo TangSong777/HanLin-ToDo --title "..." --notes-file ... --latest`。
9. 切回 `snapshot`，核验 release 与 tag。

## 初始化

在配置好 Git 提交身份并以 `TangSong777` 登录 GitHub CLI 后，从项目目录运行 `Initialize-Repository.ps1`。脚本会建立本地分支、创建公开仓库、推送初始提交并应用分支规则集。`main` 的初始提交包含发布配置，日常工作从 `snapshot` 开始。
