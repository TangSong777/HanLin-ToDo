# 桌面便签的中文输入

早期版本把便签创建为桌面拥有的 `WS_POPUP` 窗口。该样式下 `GetParent` 会返回桌面所有者，寒淋输入法会把焦点归到桌面图标区域，快速录入或搜索时无法打开中文候选窗。 [GetParent 文档](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getparent)

## 当前实现

- `BuildHost` 创建 `WS_OVERLAPPED` 顶层工具窗口。`GetWindow(GW_OWNER)` 返回 Explorer 桌面所有者，`GetParent` 返回空。`WM_MOUSEACTIVATE` 让点击输入框时能取得便签自己的键盘焦点。 [桌面窗口实现](../src/HanLin.ToDo/DesktopSurface.cs)
- Windows 将便签保持在桌面所有者上方。`WM_WINDOWPOSCHANGING` 在窗口管理器应用请求前约束便签自身的层级，避免便签在退出「显示桌面」或获得焦点时盖住普通窗口。 [Win32 窗口所有权](https://learn.microsoft.com/windows/win32/winmsg/window-features#owned-windows)
- `PlaceWindow` 以屏幕坐标调整位置和尺寸。跨屏时，`PositionWindow` 按目标显示器的 DPI 和工作区校正位置与尺寸。 [桌面窗口实现](../src/HanLin.ToDo/DesktopSurface.cs)

在 Windows 11 24H2 上点击快速录入框，寒淋输入法显示候选窗。选中「你好」再按 Enter，测试任务写入独立目录的 `todo.md`。

项目使用 .NET 10 SDK 构建。 [构建与打包](../README.md#构建与打包)
