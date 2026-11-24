# HanLin-ToDo 交接文档

> 接手本项目时先读这一份。它记录项目定位、当前状态、GitHub 配置、开发约定与待办。
> 维护规则见第九节：每次改动后必须同步更新，保持「实时」。

## 一、项目定位

HanLin-ToDo 是嵌在 Windows 桌面上的 Markdown 待办清单，核心诉求是「不打断当前工作」：

- 全局快捷键唤出输入框，填任务与截止时间，Enter 保存。
- 便签常驻桌面，按 Win + D 或点击「显示桌面」后依然可见，任务可直接勾选。
- 便签隐藏任务栏按钮。
- 勾选/取消勾选即时写回本地 Markdown，重启后恢复。
- 默认按截止时间排列未完成任务，标出逾期项。

任务文件是纯 Markdown，格式：

```markdown
- [ ] 提交实验报告 📅 2026-11-25 18:00
- [x] 整理实验数据 📅 2026-11-24 17:00
```

## 二、当前状态（2026-11-24）

| 维度 | 状态 |
| --- | --- |
| 代码 | 尚未开始，仓库只有文档与发布配置 |
| 仓库 | 公开：https://github.com/TangSong777/HanLin-ToDo |
| 默认分支 | main |
| 分支 | main（稳定发布）、snapshot（日常开发） |
| 规则集 | Stable releases from snapshot，已激活 |
| Actions | Stable release 工作流已注册，state=active |
| 本地 | D:\WorkSpace\HanLin-ToDo，当前在 snapshot，工作区干净 |

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

- 任务文件 `todo.md` 是运行时数据，已在 `.gitignore` 中排除，不入库。
- 提交信息用英文祈使句（现有提交：`Initialize stable release rules`、`Record project goals and development workflow`）。
- main 只放发布配置，项目文件放 snapshot。
- 不直接推 main；规则集会拒绝。

## 八、待办

- [ ] 实现第一版：全局快捷键、桌面便签、Markdown 读写、排序与逾期标记
- [ ] 补充构建与运行说明（技术栈未定）
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
