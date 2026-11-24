# HanLin-ToDo 输入框无法输入中文 — 修复说明

## 现象

在 HanLin-ToDo 的输入框（`quickInput`、`searchInput` 等 WPF `TextBox`）里打字，只能出英文字母，中文输入法的候选窗不出现。

## 根因

`src/HanLin.ToDo/DesktopSurface.cs` 的 `BuildHost` 把窗口挂到了桌面（`Progman`）下面：

```csharp
var parameters = new HwndSourceParameters("HanLin-ToDo Desktop", ...)
{
    ParentWindow = host,          // host = Progman 或 WorkerW
    WindowStyle = Native.Popup,   // WS_POPUP
    ExtendedWindowStyle = Native.ToolWindow,
    ...
};
```

`Progman` 拥有的 `WS_POPUP` 子窗口**拿不到键盘焦点**。实测（窗口在前台时）：

```
GetForegroundWindow() = 2166530   ← 窗口是前台
GetFocus()            = 0         ← 焦点为空
GetActiveWindow()     = 0         ← 活动窗口为空
```

TSF 输入法靠焦点关联文档管理器（`ITfDocumentMgr`）。焦点为空 → TSF 不激活 → 只能出英文，中文候选窗不出现。

## 修复

`BuildHost` 不再把窗口挂到 `Progman` 下，改成独立顶层窗口，用 Z 序保持在桌面层：

```csharp
var parameters = new HwndSourceParameters("HanLin-ToDo Desktop", ...)
{
    ParentWindow = IntPtr.Zero,   // 不再挂到 Progman 下
    WindowStyle = Native.Popup,
    ExtendedWindowStyle = Native.ToolWindow,
    UsesPerPixelTransparency = true,
    HwndSourceHook = NoteMessage
};
source = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual };
// 用 Z 序保持在桌面层，而不是用父子关系
Native.SetWindowPos(source.Handle, new IntPtr(1), 0, 0, 0, 0,
    Native.NoMove | Native.NoSize | Native.NoActivate);
```

`HWND_BOTTOM`（`new IntPtr(1)`）让窗口仍然贴在桌面之上、普通窗口之下，视觉上还是「嵌在桌面」，但它是独立顶层窗口，能正常拿焦点，TSF 就能激活。

## 需要验证的点

1. 窗口视觉位置是否仍然贴在桌面上（不被普通窗口遮挡）
2. 点击输入框后中文候选窗是否出现
3. 拖动/缩放窗口是否正常（`PlaceWindow` 里用了 `parent` 变量，需要确认 `parent = host` 这行是否还要保留）
4. 多显示器下 `PositionWindow` 是否正常

## 编译注意

本机只有 .NET 9.0.200 SDK，项目是 `net10.0-windows`，`dotnet build` 会报 `NETSDK1045`。需要 .NET 10 SDK 才能编译。

## 相关文件

- `src/HanLin.ToDo/DesktopSurface.cs` — `BuildHost`（约 L130）、`PlaceWindow`（约 L175）、`Native` 嵌套类（约 L290）
- `src/HanLin.ToDo/NoteView.Tasks.cs` — `quickInput`（L18）、`searchInput`（L23）
