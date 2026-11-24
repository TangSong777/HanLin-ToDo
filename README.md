# HanLin-ToDo

HanLin-ToDo 是嵌在 Windows 桌面上的 Markdown 待办清单。

## 第一版目标

- 用全局快捷键打开输入框，填写任务和截止时间，按 Enter 保存。
- 按 Win + D 或点击「显示桌面」后，便签依然可见，任务仍可直接勾选。便签隐藏任务栏按钮。
- 在桌面勾选或取消勾选任务，完成状态立即写回本地 Markdown 文件，重启后恢复。
- 默认按截止时间排列未完成任务，标出逾期项。

任务文件示例

```markdown
- [ ] 提交实验报告 📅 2026-11-25 18:00
- [x] 整理实验数据 📅 2026-11-24 17:00
```

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
