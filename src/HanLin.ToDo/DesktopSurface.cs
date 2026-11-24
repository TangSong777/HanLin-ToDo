using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;

namespace HanLin.ToDo;

internal sealed class DesktopSurface : IDisposable
{
    private readonly NoteView view;
    private readonly Action<Point> positionSaved;
    private HwndSource? source;
    private IntPtr parent;
    private Point? position;
    private Point dragStart;
    private Point originalPosition;
    private bool dragging;
    private bool movedPastThreshold;
    private bool disposed;

    public DesktopSurface(NoteView view, NotePreferences preferences, Action<Point> positionSaved)
    {
        this.view = view;
        this.positionSaved = positionSaved;
        if (preferences.Left is int left && preferences.Top is int top)
            position = new Point(left, top);

        view.PreviewMouseLeftButtonDown += BeginDrag;
        view.PreviewMouseMove += ContinueDrag;
        view.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!dragging)
                return;
            MoveFromCursor();
            EndDrag();
        };
        view.LostMouseCapture += (_, _) => EndDrag();
        view.SizeChanged += (_, _) => view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (!dragging)
                    PositionWindow(position);
            }));
    }

    public void Refresh(bool visible)
    {
        if (disposed)
            return;
        if (!visible)
            EndDrag();

        IntPtr host = Native.DesktopHost();
        if (source is { IsDisposed: false } && (parent != host || !Native.IsWindow(source.Handle)))
            ResetHost();
        if (host == IntPtr.Zero)
            return;

        if (source is null || source.IsDisposed)
        {
            if (!visible)
                return;
            BuildHost(host);
        }
        if (source is null || source.IsDisposed)
            return;

        if (visible && Native.IsIconic(source.Handle))
            Native.RestoreWindow(source.Handle, Native.ShowNoActivate);
        if (Native.IsWindowVisible(source.Handle) != visible)
            Native.SetWindowPos(source.Handle, IntPtr.Zero, 0, 0, 0, 0,
                Native.NoMove | Native.NoSize | Native.NoZOrder | Native.NoActivate
                | (visible ? Native.ShowWindow : Native.HideWindow));
        if (visible && !dragging)
            PositionWindow(position);
    }

    public void EndDrag()
    {
        if (!dragging)
            return;
        dragging = false;
        if (view.IsMouseCaptured)
            view.ReleaseMouseCapture();

        if (source is not { IsDisposed: false } || !Native.GetWindowRect(source.Handle, out var window))
            return;
        var current = new Point(window.Left, window.Top);
        if (current == originalPosition)
            return;
        position = current;
        positionSaved(current);
    }

    private void BuildHost(IntPtr host)
    {
        // A desktop-owned popup can still compose independently on Explorer's raised desktop.
        // Put it immediately above the icon host and below ordinary application windows.
        var parameters = new HwndSourceParameters("HanLin-ToDo Desktop", 356, 240)
        {
            ParentWindow = host,
            WindowStyle = Native.Popup,
            ExtendedWindowStyle = Native.ToolWindow,
            UsesPerPixelTransparency = true,
            HwndSourceHook = NoteMessage
        };
        source = new HwndSource(parameters) { SizeToContent = SizeToContent.WidthAndHeight };
        parent = host;
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
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        var point = target ?? new Point(area.Right - width - 18, area.Top + 20);
        int x = Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - height));

        IntPtr desktopPredecessor = Native.GetWindow(parent, Native.PreviousWindow);
        bool keepOrder = desktopPredecessor == source.Handle;
        if (keepOrder && x == rect.Left && y == rect.Top)
            return;
        if ((Native.GetExtendedStyle(desktopPredecessor) & Native.Topmost) != 0)
            desktopPredecessor = Native.NotTopmost;
        Native.SetWindowPos(source.Handle, desktopPredecessor, x, y, 0, 0,
            Native.NoSize | Native.NoActivate | Native.NoOwnerZOrder | (keepOrder ? Native.NoZOrder : 0U));
    }

    private static IntPtr NoteMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != Native.MouseActivate)
            return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.ActivateAndDeliver);
    }

    private void BeginDrag(object sender, MouseButtonEventArgs args)
    {
        var target = view.InputHitTest(args.GetPosition(view));
        if (!view.CanDragFrom(target) || source is null || source.IsDisposed
            || !Native.GetCursorPos(out var cursor) || !Native.GetWindowRect(source.Handle, out var window))
            return;

        originalPosition = new Point(window.Left, window.Top);
        dragStart = new Point(cursor.X, cursor.Y);
        movedPastThreshold = false;
        dragging = view.CaptureMouse();
        args.Handled = dragging;
    }

    private void ContinueDrag(object sender, MouseEventArgs args)
    {
        if (!dragging)
            return;
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }
        MoveFromCursor();
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
        PositionWindow(new Point(originalPosition.X + dx, originalPosition.Y + dy), new Point(cursor.X, cursor.Y));
    }

    private void ResetHost()
    {
        EndDrag();
        if (source is { IsDisposed: false })
        {
            source.RootVisual = null;
            source.Dispose();
        }
        source = null;
        parent = IntPtr.Zero;
    }

    public void Dispose()
    {
        disposed = true;
        ResetHost();
    }
}

internal static class Native
{
    public const int Popup = unchecked((int)0x80000000);
    public const int ToolWindow = 0x00000080;
    public const int Topmost = 0x00000008;
    public static readonly IntPtr NotTopmost = new(-2);
    public const int PreviousWindow = 3;
    public const uint NoSize = 0x0001;
    public const uint NoMove = 0x0002;
    public const uint NoZOrder = 0x0004;
    public const uint NoActivate = 0x0010;
    public const uint ShowWindow = 0x0040;
    public const uint HideWindow = 0x0080;
    public const uint NoOwnerZOrder = 0x0200;
    public const int ShowNoActivate = 4;
    public const int HotKeyMessage = 0x0312;
    public const int MouseActivate = 0x0021;
    public const int ActivateAndDeliver = 1;

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
        return IntPtr.Zero;
    }

    private static bool HasDesktopView(IntPtr window) => window != IntPtr.Zero
        && FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

    public static int GetExtendedStyle(IntPtr window) => window == IntPtr.Zero ? 0 : GetWindowLong(window, -20);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? caption);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? caption);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    public static extern bool RestoreWindow(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr window, int id);
}
