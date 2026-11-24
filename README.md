# HanLin-ToDo

HanLin-ToDo 是嵌在 Windows 桌面上的 Markdown 待办清单。

## 使用

- **直接添加**。在便签顶部输入一行，按 **Enter** 添加。点击输入栏旁的日历即可先选截止日期，也能在展开的一行填具体时间。保存后输入栏清空并保留焦点，可以继续输入下一条。
- **设置截止日期**。选中日期后默认为「全天」，次日开始算逾期。可以选择或输入 24 小时制时间。标题栏的加号、**Ctrl+Alt+T** 和托盘图标可打开完整录入框，点击已有任务的铅笔可在便签内修改。
- **拖动与置顶**。将鼠标移到待办左侧三点，光标会变成手形。拖动时整行半透明预览随鼠标移动，松手插入标线处，按 Esc 可取消。拖动后切换到手动顺序并保存。用图钉将任务置顶，置顶项和普通项各自在组内排序。
- **整理任务**。点击左下角的清单名称，切换全部、收件箱、今天、置顶、计划、逾期、已完成或自定义清单。在同一菜单中可管理清单、换排序、批量移动或删除，以及导出当前任务。
- **勾选和编辑**。点击复选框完成或撤回完成。右键任务可安排到今天、调整截止日期、移动、复制或删除。删除后可撤销，也可在回收站恢复。「任务详情」可设置标签、备注、步骤、提醒、重复、本地文件和链接。
- **搜索**。点击左下角的放大镜或按 **Ctrl+F**，搜索所有清单中的任务标题、备注、标签、步骤、附件以及清单便笺。
- 按 **Win+D** 或用「显示桌面」回到桌面。解锁后只能从标题栏拖动便签。点击锁图标固定或解除固定，锁定位置后仍可操作和排序任务。
- 拖动便签边缘或右下角调整宽高。锁定位置时仍可从右侧、底部或右下角调整。窗口较小时，任务列表在标题与底部之间滚动。
- 便签设置提供紧凑（300 × 258）、标准（370 × 348）和宽敞（462 × 428）三档。选好后可继续单独调宽、高、界面比例、正文字号和行间留白。界面比例会一起缩放文字、控件与间距。
- 标题默认用本机安装的造字工房悦黑常规体，正文默认用微软雅黑。标题字体可在设置中改为跟随正文，本机缺少悦黑时会自动跟随正文。
- 在「便签设置 → 工作目录」选择存放 `todo.md` 的文件夹。所选目录已有 `todo.md` 时直接读取，否则复制当前任务文件。点「默认」可选回 EXE 同级的 `data` 文件夹，保存后立即使用。
- 程序启动后恢复位置、尺寸、界面比例、锁定状态、外观和任务。便签设置可开启登录自启，通知区域右键菜单可显示、隐藏或退出。

首次运行时便签在主屏幕右上方，程序会自动创建 EXE 同级的 `data` 文件夹。升级后首次启动会在目标缺少 `todo.md` 时复制旧版的 `%LOCALAPPDATA%\HanLin-ToDo\todo.md`。工作目录选择、位置和外观仍记录在 `%LOCALAPPDATA%\HanLin-ToDo\settings.json`。设置及托盘菜单都有「打开任务文件」入口。提醒由正在运行的 HanLin-ToDo 通过 Windows 通知区域发送。本地附件保存文件路径。

任务文件示例

```markdown
- [ ] 提交实验报告 📅 2026-11-25
- [x] 整理实验数据 📅 2026-11-24 17:00
- [ ] 备份项目文件
```

旧版任务可以直接读取，原星标会作为置顶导入。任务详情、清单、便笺和回收站用 HTML 注释保存在同一个 Markdown 文件内。普通 Markdown 预览仍会显示任务列表。手动编辑时可保留这些注释，复制 Markdown 即可备份数据。

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

## 初始化

在配置好 Git 提交身份并以 `TangSong777` 登录 GitHub CLI 后，从项目目录运行 `Initialize-Repository.ps1`。脚本会建立本地分支、创建公开仓库、推送初始提交并应用分支规则集。`main` 的初始提交包含发布配置，日常工作从 `snapshot` 开始。
