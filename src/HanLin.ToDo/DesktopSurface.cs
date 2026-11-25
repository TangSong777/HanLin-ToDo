using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace HanLin.ToDo;

internal sealed class DesktopSurface : IDisposable
{
    private readonly NoteView view;
    private readonly Action<NotePreferences> savePlacement;
    private readonly DispatcherTimer dragTimer;
    private NotePreferences preferences;
    private HwndSource? source;
    private Point? position;
    private Point dragStart;
    private Native.Rect originalBounds;
    private ResizeEdge resizeEdges;
    private bool dragging;
    private bool movedPastThreshold;
    private bool disposed;

    public DesktopSurface(NoteView view, NotePreferences preferences, Action<NotePreferences> savePlacement)
    {
        this.view = view;
        this.preferences = preferences;
        this.savePlacement = savePlacement;
        Apply(preferences);

        dragTimer = new DispatcherTimer(DispatcherPriority.Input, view.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        dragTimer.Tick += (_, _) =>
        {
            if (view.IsMouseCaptured)
                MoveFromCursor();
            if (!view.IsMouseCaptured || !Native.PrimaryButtonDown())
                EndDrag();
        };
        view.LostMouseCapture += (_, _) =>
        {
            if (!view.IsMouseCaptured)
                EndDrag();
        };
        view.PreviewMouseLeftButtonDown += BeginDrag;
        view.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!dragging)
                return;
            MoveFromCursor();
            EndDrag();
        };
    }

    public void Apply(NotePreferences next)
    {
        preferences = next;
        position = next.Left is int left && next.Top is int top ? new Point(left, top) : null;
        if (!dragging)
            PositionWindow(position);
    }

    public void SetVisible(bool visible)
    {
        if (disposed)
            return;
        if (!visible)
            EndDrag();

        if (visible)
        {
            IntPtr owner = Native.DesktopHost();
            if (owner == IntPtr.Zero)
                return;
            if (source is not { IsDisposed: false } active || Native.GetWindow(active.Handle, Native.OwnerWindow) != owner)
            {
                ResetHost();
                BuildHost(owner);
            }
        }

        if (source is { IsDisposed: false } host && Native.IsWindowVisible(host.Handle) != visible)
            Native.SetWindowPos(host.Handle, Native.BottomWindow, 0, 0, 0, 0,
                Native.NoMove | Native.NoSize | Native.NoActivate
                | (visible ? Native.ShowWindow : Native.HideWindow | Native.NoZOrder));
    }

    public void EndDrag()
    {
        if (!dragging)
            return;
        dragging = false;
        dragTimer.Stop();
        bool resized = resizeEdges != ResizeEdge.None;
        resizeEdges = ResizeEdge.None;
        if (view.IsMouseCaptured)
            view.ReleaseMouseCapture();

        if (source is not { IsDisposed: false } || !Native.GetWindowRect(source.Handle, out var window))
            return;
        if (window.Left == originalBounds.Left && window.Top == originalBounds.Top &&
            window.Width == originalBounds.Width && window.Height == originalBounds.Height)
            return;

        NoteTheme theme = preferences.Theme;
        if (resized)
        {
            double scale = WindowScale(source.Handle);
            theme = (theme with
            {
                Width = ToCardUnits(window.Width, scale),
                Height = ToCardUnits(window.Height, scale)
            }).Normalize();
        }
        savePlacement(preferences with { Left = window.Left, Top = window.Top, Theme = theme });
    }

    private void BuildHost(IntPtr owner)
    {
        // Explorer owns the note, so Windows keeps it above the desktop as a single group.
        // WS_OVERLAPPED preserves a top-level input HWND without exposing the owner via GetParent.
        var parameters = new HwndSourceParameters("HanLin-ToDo Desktop",
            NoteTheme.DefaultWidth + 2 * NoteView.ShadowMargin, NoteTheme.DefaultHeight + 2 * NoteView.ShadowMargin)
        {
            ParentWindow = owner,
            WindowStyle = Native.Overlapped,
            ExtendedWindowStyle = Native.ToolWindow,
            UsesPerPixelTransparency = true,
            HwndSourceHook = NoteMessage
        };
        source = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual };
        if (Native.GetWindow(source.Handle, Native.OwnerWindow) != owner)
        {
            ResetHost();
            return;
        }
        source.DpiChanged += (_, _) => view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!dragging)
                PositionWindow(position);
        }));
        source.RootVisual = view;
        PositionWindow(position);
    }

    private void PositionWindow(Point? target, Point? pointer = null)
    {
        if (disposed || source is null || source.IsDisposed || !Native.GetWindowRect(source.Handle, out var rect))
            return;

        var monitorPoint = pointer ?? target;
        var screen = monitorPoint is { } location ? Forms.Screen.FromPoint(location) : Forms.Screen.PrimaryScreen;
        var area = screen?.WorkingArea ?? Forms.SystemInformation.WorkingArea;
        IntPtr window = source.Handle;
        NoteTheme theme = preferences.Theme;
        double scale = WindowScale(window);
        PlaceWindow(window, rect, BoundsInWorkArea(theme, target, area, scale));

        // Crossing to another display can change the HWND DPI during the first move.
        double newScale = WindowScale(window);
        if (newScale != scale && Native.GetWindowRect(window, out var moved))
            PlaceWindow(window, moved, BoundsInWorkArea(theme, target, area, newScale));
    }

    private static Native.Rect BoundsInWorkArea(NoteTheme theme, Point? target, Rectangle area, double scale)
    {
        int width = Math.Min(ToPixels(theme.Width, scale), area.Width);
        int height = Math.Min(ToPixels(theme.Height, scale), area.Height);
        var point = target ?? new Point(area.Right - width - 18, area.Top + 20);
        int x = Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - height));
        return new Native.Rect(x, y, x + width, y + height);
    }

    private static void PlaceWindow(IntPtr window, Native.Rect current, Native.Rect target)
    {
        bool samePosition = target.Left == current.Left && target.Top == current.Top;
        bool sameSize = current.Width == target.Width && current.Height == target.Height;
        if (samePosition && sameSize)
            return;
        Native.SetWindowPos(window, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
            Native.NoActivate | Native.NoZOrder | (samePosition ? Native.NoMove : 0U) | (sameSize ? Native.NoSize : 0U));
    }

    private IntPtr NoteMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.NcCalcSize)
        {
            // The WPF card supplies its own title bar and resize handles.
            handled = true;
            return IntPtr.Zero;
        }
        if (message == Native.WindowPosChanging)
        {
            var change = Marshal.PtrToStructure<Native.WindowPos>(lParam);
            if ((change.Flags & Native.NoZOrder) == 0)
            {
                // Let focus change in place. For stacking requests, Windows clamps HWND_BOTTOM
                // just above the owner, including when Explorer restores ordinary windows.
                change.Flags |= Native.NoOwnerZOrder;
                if ((change.Flags & Native.NoActivate) == 0)
                    change.Flags |= Native.NoZOrder;
                else
                    change.InsertAfter = Native.BottomWindow;
                Marshal.StructureToPtr(change, lParam, false);
            }
        }
        if (message == Native.MouseActivate)
        {
            handled = true;
            return new IntPtr(Native.ActivateAndDeliver);
        }
        if (message == Native.SysCommand && (wParam.ToInt64() & 0xFFF0) == Native.MinimizeCommand)
        {
            handled = true;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private void BeginDrag(object sender, MouseButtonEventArgs args)
    {
        var hit = args.GetPosition(view);
        if (dragging || source is null || source.IsDisposed
            || !view.TryGetWindowDrag(args.OriginalSource, hit, out var edges)
            || !Native.GetCursorPos(out var cursor) || !Native.GetWindowRect(source.Handle, out var window))
            return;

        if (!view.CaptureMouse())
            return;
        resizeEdges = edges;
        originalBounds = window;
        dragStart = new Point(cursor.X, cursor.Y);
        movedPastThreshold = false;
        dragging = true;
        dragTimer.Start();
        args.Handled = true;
    }

    private void MoveFromCursor()
    {
        if (!Native.GetCursorPos(out var cursor))
            return;

        int dx = cursor.X - dragStart.X;
        int dy = cursor.Y - dragStart.Y;
        if (!movedPastThreshold)
        {
            var dpi = VisualTreeHelper.GetDpi(view);
            movedPastThreshold = Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX
                || Math.Abs(dy) >= SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY;
            if (!movedPastThreshold)
                return;
        }
        if (resizeEdges == ResizeEdge.None)
            PositionWindow(new Point(originalBounds.Left + dx, originalBounds.Top + dy), new Point(cursor.X, cursor.Y));
        else
            ResizeWindow(dx, dy);
    }

    private void ResizeWindow(int dx, int dy)
    {
        if (source is not { IsDisposed: false } || !Native.GetWindowRect(source.Handle, out var current))
            return;

        var target = originalBounds;
        var center = new Point(target.Left + target.Width / 2, target.Top + target.Height / 2);
        var area = Forms.Screen.FromPoint(center).WorkingArea;
        double scale = WindowScale(source.Handle);
        int minWidth = Math.Min(target.Width, ToPixels(preferences.Theme.MinimumWidth, scale));
        int minHeight = Math.Min(target.Height, ToPixels(preferences.Theme.MinimumHeight, scale));
        if ((resizeEdges & ResizeEdge.Left) != 0)
            target.Left = Math.Clamp(target.Left + dx, area.Left, Math.Max(area.Left, target.Right - minWidth));
        if ((resizeEdges & ResizeEdge.Right) != 0)
            target.Right = Math.Clamp(target.Right + dx, target.Left + minWidth, Math.Max(area.Right, target.Left + minWidth));
        if ((resizeEdges & ResizeEdge.Top) != 0)
            target.Top = Math.Clamp(target.Top + dy, area.Top, Math.Max(area.Top, target.Bottom - minHeight));
        if ((resizeEdges & ResizeEdge.Bottom) != 0)
            target.Bottom = Math.Clamp(target.Bottom + dy, target.Top + minHeight, Math.Max(area.Bottom, target.Top + minHeight));
        PlaceWindow(source.Handle, current, target);
    }

    private static double WindowScale(IntPtr window)
    {
        uint dpi = Native.GetDpiForWindow(window);
        return dpi > 0 ? dpi / 96.0 : 1;
    }

    private static int ToPixels(int cardUnits, double scale) =>
        (int)Math.Round((cardUnits + 2 * NoteView.ShadowMargin) * scale);

    private static int ToCardUnits(int pixels, double scale) =>
        (int)Math.Round(pixels / scale) - 2 * NoteView.ShadowMargin;

    private void ResetHost()
    {
        EndDrag();
        if (source is { IsDisposed: false })
        {
            source.RootVisual = null;
            source.Dispose();
        }
        source = null;
    }

    public void Dispose()
    {
        disposed = true;
        ResetHost();
    }
}

internal static class Native
{
    public const int Overlapped = 0;
    public const int Popup = unchecked((int)0x80000000);
    public const int ToolWindow = 0x00000080;
    public static readonly IntPtr BottomWindow = new(1);
    public const int OwnerWindow = 4;
    public const uint NoSize = 0x0001;
    public const uint NoMove = 0x0002;
    public const uint NoZOrder = 0x0004;
    public const uint NoActivate = 0x0010;
    public const uint ShowWindow = 0x0040;
    public const uint HideWindow = 0x0080;
    public const uint NoOwnerZOrder = 0x0200;
    public const int HotKeyMessage = 0x0312;
    public const int MouseActivate = 0x0021;
    public const int ActivateAndDeliver = 1;
    public const int WindowPosChanging = 0x0046;
    public const int NcCalcSize = 0x0083;
    public const int SysCommand = 0x0112;
    public const int MinimizeCommand = 0xF020;

    public static IntPtr DesktopHost()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (HasDesktopView(progman))
            return progman;
        for (IntPtr worker = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
             worker != IntPtr.Zero;
             worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null))
        {
            if (HasDesktopView(worker))
                return worker;
        }
        return GetShellWindow();
    }

    private static bool HasDesktopView(IntPtr window) => window != IntPtr.Zero
        && FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

    public static bool PrimaryButtonDown() => GetAsyncKeyState(Forms.SystemInformation.MouseButtonsSwapped ? 0x02 : 0x01) < 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPos
    {
        public IntPtr Window;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? caption);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? caption);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr window, int id);
}
