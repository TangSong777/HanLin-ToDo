# 待发布改动（自 1.1.0 起）

> 每次改动追加一行：日期 · 类型 · 改了什么 · 为什么 · 影响范围。
> 发版时读完整本账本，汇总进 `CHANGELOG.md` 与 GitHub Release 说明，然后清空本文件。

- 2026-11-26 · 功能 · 任务附件：文件拖到任务行即复制进工作目录 `Attachments`，行尾回形针徽标（多附件显示数量、缺失变灰），点击打开或列表选择；右键菜单支持在文件夹中显示、复制路径、改显示名、重新定位、添加与移除；详情页新增「附件文件」区 · 用户要求把文件副本留在便签里，原文件移动或删除不影响显示 · 新增 `Attachments.cs`、`NoteView.Attachments.cs`，`Tasks.cs` 的 `TodoDetails` 增加 `Files` 字段，旧 `Attachments` 保持只读兼容
- 2026-11-26 · 功能 · 搜索命中附件文件名，导出 Markdown 把附件写成相对链接 · 让附件和任务一起被检索与备份 · `Tasks.cs`、`App.xaml.cs`
- 2026-11-26 · 重构 · 默认数据目录由 `data` 改名 `Data`，旧目录在首次启动时自动迁移 · 统一目录大小写 · `Storage.cs` 新增 `MigrateLegacyDirectory`，`--data-dir` 参数名不变
- 2026-11-26 · 功能 · 快速录入框按 Tab 直接展开日历，方向键选日期，Enter 确认后回到输入框 · 与 Ctrl+Alt+T 录入窗口的 Tab 行为对齐 · `NoteView.Tasks.cs`
- 2026-11-26 · 体验 · 统一动效：任务行入场、完成与置顶反馈、通知与撤销条滑入滑出、附件徽标弹入、附件拖放高亮渐显、右键菜单淡入；拖拽预览加阴影与轻微抬升，插入线淡入，靠近边缘时自动滚动加速 · 让状态变化可感知、拖动落点更清楚 · `Ui.cs` 新增动效工具，`NoteView.cs`、`NoteView.Tasks.cs`、`NoteView.Attachments.cs`、`NoteView.Reorder.cs`

- 2026-11-26 · 界面 · 附件右键菜单每个附件行右侧加「✕」移除按钮（自绘，悬停变珊瑚红），只移除单个附件并弹自绘确认框；「移除全部附件…」也先确认；副本仍保留在 Attachments 文件夹 · 误点不再直接清空全部附件，避免误删 · `NoteView.Attachments.cs`、新增 `ConfirmDialog.cs`
- 2026-11-26 · 界面 · 附件删除按钮贴住菜单右缘、三个文件动作收进「文件操作」子菜单、确认框增加「同时删除复制过来的副本文件（移到回收站）」勾选 · 菜单更短更紧凑，勾选后副本进回收站可找回 · `NoteView.Attachments.cs`、`ConfirmDialog.cs`、新增 `RecycleBin.cs`
