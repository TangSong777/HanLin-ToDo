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
    private readonly Action<NotePreferences> savePlacement;
    private readonly DispatcherTimer dragTimer;
    private NotePreferences preferences;
    private HwndSource? source;
    private IntPtr parent;
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
            MoveFromCursor();
            if (!Native.PrimaryButtonDown())
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

    private void BuildHost(IntPtr host)
    {
        // A desktop-owned popup can still compose independently on Explorer's raised desktop.
        // Put it immediately above the icon host and below ordinary application windows.
        var parameters = new HwndSourceParameters("HanLin-ToDo Desktop",
            NoteTheme.DefaultWidth + 2 * NoteView.ShadowMargin, NoteTheme.DefaultHeight + 2 * NoteView.ShadowMargin)
        {
            ParentWindow = host,
            WindowStyle = Native.Popup,
            ExtendedWindowStyle = Native.ToolWindow,
            UsesPerPixelTransparency = true,
            HwndSourceHook = NoteMessage
        };
        source = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual };
        source.DpiChanged += (_, _) => view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!dragging)
                PositionWindow(position);
        }));
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
        double scale = WindowScale(source.Handle);
        int width = Math.Min(ToPixels(preferences.Theme.Width, scale), area.Width);
        int height = Math.Min(ToPixels(preferences.Theme.Height, scale), area.Height);
        var point = target ?? new Point(area.Right - width - 18, area.Top + 20);
        int x = Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - height));
        PlaceWindow(source.Handle, rect, new Native.Rect(x, y, x + width, y + height));
    }

    private void PlaceWindow(IntPtr handle, Native.Rect current, Native.Rect target)
    {
        IntPtr desktopPredecessor = Native.GetWindow(parent, Native.PreviousWindow);
        bool keepOrder = dragging || desktopPredecessor == handle;
        bool sameSize = current.Width == target.Width && current.Height == target.Height;
        if (keepOrder && sameSize && target.Left == current.Left && target.Top == current.Top)
            return;
        if ((Native.GetExtendedStyle(desktopPredecessor) & Native.Topmost) != 0)
            desktopPredecessor = Native.NotTopmost;
        Native.SetWindowPos(handle, desktopPredecessor, target.Left, target.Top, target.Width, target.Height,
            Native.NoActivate | Native.NoOwnerZOrder | (sameSize ? Native.NoSize : 0U) | (keepOrder ? Native.NoZOrder : 0U));
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
        var hit = args.GetPosition(view);
        if (dragging || !view.IsCardSurface(view.InputHitTest(hit)) || source is null || source.IsDisposed
            || !Native.GetCursorPos(out var cursor) || !Native.GetWindowRect(source.Handle, out var window))
            return;

        resizeEdges = view.ResizeEdgesAt(hit);
        if (resizeEdges == ResizeEdge.None && view.IsLocked)
            return;
        originalBounds = window;
        dragStart = new Point(cursor.X, cursor.Y);
        movedPastThreshold = false;
        dragging = true;
        view.CaptureMouse();
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
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr window, int id);
}
