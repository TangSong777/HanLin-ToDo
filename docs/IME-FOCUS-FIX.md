# 桌面便签的中文输入

早期版本将便签挂到桌面宿主后，快速录入和搜索等输入框曾出现键盘焦点丢失，表现为输入拼音时没有中文候选窗。

## 当前实现

- `BuildHost` 创建独立的顶层工具窗口，并通过 `WM_MOUSEACTIVATE` 让点击输入框时可获得键盘焦点。
- `PlaceOnDesktop` 维护 Z 序。便签位于桌面图标层上方，普通窗口重叠时覆盖便签。
- `PlaceWindow` 使用屏幕坐标移动和调整便签，并保留当前 Z 序。跨屏时，`PositionWindow` 会按目标显示器的 DPI 和工作区校正位置及尺寸。 [桌面窗口实现](../src/HanLin.ToDo/DesktopSurface.cs)

项目使用 .NET 10 SDK 构建。 [构建与打包](../README.md#构建与打包)
