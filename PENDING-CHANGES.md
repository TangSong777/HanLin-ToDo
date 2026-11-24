# 待发布改动（自 1.0.1 起）

> 每次改动追加一行：日期 · 类型 · 改了什么 · 为什么 · 影响范围。
> 发版时读完整本账本，汇总进 `CHANGELOG.md` 与 GitHub Release 说明，然后清空本文件。

## 2026-11-24

- 2026-11-24 · 变更 · 交接文档记录 1.0.1 发布状态 · 为什么：保持文档实时 · 影响：维护流程
- 2026-11-24 · 新增 · 便签按显示器布局分别记住位置和尺寸 · 为什么：串流单屏与实验室双屏来回切换时便签位置会变，需要手动调整 · 影响：新增 DisplayLayout.cs，Storage.cs 增加 Placements，App.xaml.cs 监听 DisplaySettingsChanged
- 2026-11-24 · 新增 · 正文和标题都可选择微软雅黑、宋体或悦黑体 · 为什么：两种文字都需要自由换字体 · 影响：NoteTheme 字体值域扩展，标题字体枚举扩展并迁移旧「跟随正文」，设置界面两个下拉框改为三项
- 2026-11-24 · 变更 · 键盘新建改为两次 Enter：第一次确认日期并回到输入框，第二次才保存 · 为什么：一次 Enter 同时确认日期和保存容易误提交 · 影响：TaskEditor 日历键盘处理
- 2026-11-24 · 变更 · 打开录入窗口后焦点直接落在任务名称输入框 · 为什么：按下快捷键即可开始输入 · 影响：CaptureWindow 与 App.ShowCapture
- 2026-11-24 · 变更 · 打包脚本先结束正在运行的便签，替换后自动启动新版本 · 为什么：部署时不必手动关闭旧进程 · 影响：tools/Publish-Windows.ps1 新增 -SkipLaunch 开关
- 2026-11-24 · 修复 · 打包脚本丢失 UTF-8 BOM 后把「桌面」按 GBK 解码，EXE 被部署到乱码目录，程序默认工作目录随之显示为 D:\妗岄潰\HanLin-ToDo\data · 为什么：PowerShell 5.1 对无 BOM 脚本按系统 ANSI 解码 · 影响：默认输出目录改用 [Environment]::GetFolderPath('Desktop') 拼接，脚本恢复 BOM，删除乱码目录并重新部署
- 2026-11-24 · 变更 · 设置窗口的正文字体和标题字体下拉框改为自绘控件 · 为什么：系统 ComboBox 与便签整体风格不一致 · 影响：新增 Views/FontPicker.cs，SettingsWindow 改用 FontPicker
