using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace HanLin.ToDo;

internal sealed class TrayMenu : Forms.ContextMenuStrip
{
    private const float OuterRadius = 8;
    private const float ItemRadius = 5;
    private const int DpiChanged = 0x02E0;
    private const int MouseLowLevelHook = 14;
    private readonly Drawing.Font menuFont = new("Microsoft YaHei UI", 9f);
    private readonly Drawing.Bitmap imageSlot = new(16, 16);

    private readonly Forms.Timer fade = new() { Interval = 15 };
    private readonly Stopwatch opening = new();
    private readonly MouseHook mouseCallback;
    private IntPtr mouseHook;
    private int openingId;
    private bool dismissalPending;

    public TrayMenu()
    {
        mouseCallback = ObserveOutsideClick;
        Font = menuFont;
        Renderer = new TrayRenderer();
        Padding = new Forms.Padding(3, 4, 3, 4);
        Cursor = Forms.Cursors.Arrow;
        ShowItemToolTips = false;
        fade.Tick += (_, _) =>
        {
            double t = Math.Min(1, opening.Elapsed.TotalMilliseconds / 110);
            Opacity = 1 - 0.15 * Math.Pow(1 - t, 3);
            if (t >= 1) fade.Stop();
        };
    }

    public Forms.ToolStripMenuItem AddAction(string text, string icon, Action action, string? shortcut = null)
    {
        var item = new Forms.ToolStripMenuItem(text, imageSlot)
        {
            Tag = icon,
            ShowShortcutKeys = shortcut is not null,
            ShortcutKeyDisplayString = shortcut ?? ""
        };
        item.Click += (_, _) => action();
        Items.Add(item);
        return item;
    }

    protected override void OnOpened(EventArgs e)
    {
        openingId++;
        dismissalPending = false;
        // The IME can open its own popup without taking foreground focus.
        mouseHook = SetWindowsHookExW(MouseLowLevelHook, mouseCallback, GetModuleHandleW(null), 0);
        UpdateOutline();
        base.OnOpened(e);
        if (!Forms.SystemInformation.HighContrast && Forms.SystemInformation.IsMenuAnimationEnabled && Ui.MotionEnabled)
        {
            Opacity = 0.85;
            opening.Restart();
            fade.Start();
        }
    }

    protected override void OnClosed(Forms.ToolStripDropDownClosedEventArgs e)
    {
        StopWatchingMouse();
        fade.Stop();
        Opacity = 1;
        base.OnClosed(e);
    }

    private IntPtr ObserveOutsideClick(int code, IntPtr message, IntPtr data)
    {
        if (code == 0 && Visible && !dismissalPending &&
            message.ToInt32() is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            // MSLLHOOKSTRUCT starts with the cursor position in screen coordinates.
            var point = new Drawing.Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, sizeof(int)));
            if (!Bounds.Contains(point))
            {
                dismissalPending = true;
                int observedOpening = openingId;
                BeginInvoke(new Forms.MethodInvoker(() =>
                {
                    if (Visible && openingId == observedOpening)
                        Close(Forms.ToolStripDropDownCloseReason.AppClicked);
                }));
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void StopWatchingMouse()
    {
        if (mouseHook == IntPtr.Zero)
            return;
        UnhookWindowsHookEx(mouseHook);
        mouseHook = IntPtr.Zero;
    }

    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetWindowsHookExW(int kind, MouseHook callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateOutline();
    }

    protected override void WndProc(ref Forms.Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == DpiChanged) UpdateOutline();
    }

    private void UpdateOutline()
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
            return;
        using var outline = Rounded(ClientRectangle, OuterRadius * DpiScale(this));
        var previous = Region;
        Region = new Drawing.Region(outline);
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopWatchingMouse();
        base.Dispose(disposing);
        if (disposing)
        {
            fade.Dispose();
            imageSlot.Dispose();
            menuFont.Dispose();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    private static float DpiScale(Forms.ToolStrip? strip)
    {
        uint dpi = strip is { IsHandleCreated: true } ? GetDpiForWindow(strip.Handle) : 0;
        return dpi > 0 ? dpi / 96f : 1f;
    }

    private static Drawing2D.GraphicsPath Rounded(Drawing.RectangleF bounds, float radius)
    {
        var path = new Drawing2D.GraphicsPath();
        float diameter = Math.Min(2 * radius, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class TrayRenderer : Forms.ToolStripRenderer
    {
        private static Drawing.Color Color(string resource, Drawing.Color system)
        {
            if (Forms.SystemInformation.HighContrast)
                return system;
            var color = ((SolidColorBrush)Application.Current.FindResource(resource)).Color;
            return Drawing.Color.FromArgb(color.R, color.G, color.B);
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var fill = new Drawing.SolidBrush(Color("SurfaceBrush", Drawing.SystemColors.Menu));
            e.Graphics.FillRectangle(fill, e.ToolStrip.ClientRectangle);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            var bounds = e.ToolStrip.ClientRectangle;
            if (bounds.Width < 2 || bounds.Height < 2)
                return;
            float scale = DpiScale(e.ToolStrip);
            float inset = scale / 2;
            using var outline = Rounded(new Drawing.RectangleF(inset, inset, bounds.Width - scale, bounds.Height - scale),
                OuterRadius * scale - inset);
            using var pen = new Drawing.Pen(Color("EdgeBrush", Drawing.SystemColors.WindowFrame), scale);
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, outline);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed))
                return;
            float scale = DpiScale(e.ToolStrip);
            var bounds = new Drawing.RectangleF(2 * scale, scale, e.Item.Width - 4 * scale, e.Item.Height - 2 * scale);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            using var highlight = Rounded(bounds, ItemRadius * scale);
            using var fill = new Drawing.SolidBrush(Color(e.Item.Pressed ? "PressBrush" : "HoverBrush",
                Drawing.SystemColors.Highlight));
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(fill, highlight);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            bool shortcut = e.Item is Forms.ToolStripMenuItem item && e.Text == item.ShortcutKeyDisplayString;
            string color = shortcut || !e.Item.Enabled ? "MutedBrush" : e.Item.Tag is "exit" ? "OverdueBrush" : "InkBrush";
            e.TextColor = Color(color, ItemSystemColor(e.Item));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var bounds = e.ImageRectangle;
            if (e.Item.Tag is not string icon || bounds.Width <= 0 || bounds.Height <= 0)
                return;
            float scale = Math.Min(bounds.Width, bounds.Height) / 16f;
            float left = bounds.Left + bounds.Width / 2f - 7 * scale;
            float top = bounds.Top + bounds.Height / 2f - 7 * scale;
            Drawing.PointF Place(Point point) => new(left + (float)point.X * scale, top + (float)point.Y * scale);

            using var path = new Drawing2D.GraphicsPath();
            foreach (var figure in Ui.GlyphGeometry(icon).GetFlattenedPathGeometry().Figures)
            {
                var points = new List<Drawing.PointF> { Place(figure.StartPoint) };
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line)
                        points.Add(Place(line.Point));
                    else if (segment is PolyLineSegment polyline)
                        foreach (var point in polyline.Points)
                            points.Add(Place(point));
                }
                if (points.Count < 2)
                    continue;
                path.StartFigure();
                path.AddLines(points.ToArray());
                if (figure.IsClosed)
                    path.CloseFigure();
            }
            using var pen = new Drawing.Pen(Color(e.Item.Tag is "exit" ? "OverdueBrush" : "MutedBrush",
                ItemSystemColor(e.Item)), 1.25f * scale)
            {
                StartCap = Drawing2D.LineCap.Round,
                EndCap = Drawing2D.LineCap.Round,
                LineJoin = Drawing2D.LineJoin.Round
            };
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int inset = (int)Math.Round(7 * DpiScale(e.ToolStrip));
            int y = e.Item.Height / 2;
            if (e.Item.Width <= 2 * inset)
                return;
            using var pen = new Drawing.Pen(Color("LineBrush", Drawing.SystemColors.GrayText));
            e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
        }

        private static Drawing.Color ItemSystemColor(Forms.ToolStripItem item) => !item.Enabled
            ? Drawing.SystemColors.GrayText
            : item.Selected || item.Pressed ? Drawing.SystemColors.HighlightText : Drawing.SystemColors.MenuText;
    }
}
