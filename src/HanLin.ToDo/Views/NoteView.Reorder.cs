using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed partial class NoteView
{
    private sealed record TaskRow(Border Face, Thumb? Handle, Border Feedback, CheckBox Check);
    private readonly record struct DropLocation(TodoItem Item, bool After);
    private const int ReorderMotionMs = 160;
    private sealed record TaskDragPreview(
        Grid Ghost, TranslateTransform Position, TodoItem[] Group, int OriginalIndex, double RowHeight);

    private readonly Dictionary<TodoItem, TaskRow> taskRows = [];
    private readonly DispatcherTimer taskReorderTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Canvas taskDragLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private TodoItem? draggedTask;
    private TaskDragPreview? taskDragPreview;
    private DropLocation? dropLocation;
    private int dragDestination;
    private Point dragOrigin;
    private Vector dragGrabOffset;

    private bool CanReorder => !selecting && SearchQuery.Length == 0 && ActiveView != TodoViews.Trash && !IsEditingTask;

    private Thumb CreateTaskHandle(TodoItem item)
    {
        var handle = new Thumb
        {
            Style = (Style)Application.Current.FindResource("SoftTaskHandle"),
            ToolTip = "拖动排序，也可按上下键移动。"
        };
        handle.AutomationName("拖动排序 " + item.Title);
        handle.DragStarted += (_, _) =>
        {
            if (!CanReorder || !taskRows.TryGetValue(item, out var row))
                return;
            draggedTask = item;
            dragOrigin = Mouse.GetPosition(taskDragLayer);
            dragGrabOffset = dragOrigin - row.Face.TranslatePoint(new Point(), taskDragLayer);
            handle.Focus();
        };
        handle.DragDelta += (_, _) => ContinueTaskDrag();
        handle.DragCompleted += (_, args) =>
        {
            if (draggedTask != item)
                return;
            if (!args.Canceled && taskDragPreview is not null)
                ContinueTaskDrag();
            var destination = args.Canceled ? null : dropLocation;
            if (destination is { } target)
                MoveTask(item, target, preview: taskDragPreview);
            else
                ClearTaskDrag(animateReset: true);
        };
        handle.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && handle.IsDragging)
            {
                handle.CancelDrag();
                args.Handled = true;
            }
            else if (!handle.IsDragging && args.Key is Key.Up or Key.Down)
            {
                MoveTaskBy(item, args.Key == Key.Up ? -1 : 1);
                args.Handled = true;
            }
        };
        return handle;
    }

    private void ContinueTaskDrag()
    {
        if (draggedTask is not { } item || !taskRows.TryGetValue(item, out var source))
            return;
        Point cursor = Mouse.GetPosition(taskDragLayer);
        var preview = taskDragPreview;
        if (preview is null)
        {
            if (Math.Abs(cursor.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(cursor.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            preview = taskDragPreview = CreateTaskDragPreview(item, source.Face);
            dragDestination = preview.OriginalIndex;
            taskDragLayer.Children.Insert(0, preview.Ghost);
            Ui.Fade(source.Face, source.Face.Opacity, 0, Ui.FastMotion);
            taskReorderTimer.Start();
        }

        double left = TaskRowBounds(source.Face).Left;
        preview.Position.X = Math.Clamp(cursor.X - dragGrabOffset.X, left - 8, left + 8);
        preview.Position.Y = cursor.Y - dragGrabOffset.Y;
        ShowDropLocation(IsNearTaskViewport(cursor) ? FindDropLocation(preview, cursor.Y) : null);
    }

    private TaskDragPreview CreateTaskDragPreview(TodoItem item, Border row)
    {
        var position = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(position);
        var ghost = new Grid
        {
            Width = row.ActualWidth, Height = row.ActualHeight, Opacity = 0, RenderTransform = transform,
            RenderTransformOrigin = new Point(0.5, 0.5), UseLayoutRounding = false
        };
        var edge = new Border
        {
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = 0.22 }
        };
        edge.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        edge.SetResourceReference(BorderBrushProperty, "EdgeBrush");
        ghost.Children.Add(edge);
        // Copy the content inside the row so dimming the original does not also dim the preview.
        ghost.Children.Add(new Rectangle
        {
            Fill = new VisualBrush(row.Child)
            {
                AutoLayoutContent = false, Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(row.RenderSize)
            }
        });

        var group = taskRows.Where(pair => InSameOrderGroup(item, pair.Key))
            .OrderBy(pair => TaskRowBounds(pair.Value.Face).Top)
            .Select(pair => pair.Key).ToArray();
        // Lift the row into place instead of snapping it under the cursor.
        Ui.Fade(ghost, 0.7, 0.94, Ui.FastMotion);
        if (Ui.MotionEnabled)
        {
            var lift = new DoubleAnimation(1, 1.014, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, lift);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, lift.Clone());
        }
        return new(ghost, position, group, Array.IndexOf(group, item),
            row.ActualHeight + row.Margin.Top + row.Margin.Bottom);
    }

    private DropLocation? FindDropLocation(TaskDragPreview preview, double y)
    {
        // The displayed group is ordered top to bottom. Find the first midpoint below the cursor.
        var group = preview.Group;
        if (group.Length < 2)
            return null;
        int first = 0;
        int last = group.Length;
        while (first < last)
        {
            int middle = first + (last - first) / 2;
            Rect bounds = TaskRowBounds(taskRows[group[middle]].Face);
            if (y < bounds.Top + bounds.Height / 2)
                last = middle;
            else
                first = middle + 1;
        }

        if (first == preview.OriginalIndex || first == preview.OriginalIndex + 1)
            return null;
        return first < group.Length ? new(group[first], false) : new(group[^1], true);
    }

    private Rect TaskRowBounds(Border row)
    {
        // Use the arranged position so animated rows cannot move their own hit thresholds.
        var parent = (UIElement)VisualTreeHelper.GetParent(row);
        var offset = VisualTreeHelper.GetOffset(row);
        return new Rect(parent.TranslatePoint(new Point(offset.X, offset.Y), taskDragLayer), row.RenderSize);
    }

    private bool IsNearTaskViewport(Point cursor) =>
        cursor.X >= -16 && cursor.X <= taskDragLayer.ActualWidth + 16 &&
        cursor.Y >= -18 && cursor.Y <= taskDragLayer.ActualHeight + 18;

    private void ScrollTaskDrag()
    {
        Point cursor = Mouse.GetPosition(taskDragLayer);
        if (!IsNearTaskViewport(cursor))
            return;
        double viewport = taskDragLayer.ActualHeight;
        if (viewport <= 0)
            return;
        double edge = Math.Min(44, viewport / 3);
        double offset = taskScroll.VerticalOffset;
        double step = 0;
        if (cursor.Y < edge && offset > 0)
            step = -ScrollStep(cursor.Y, edge);
        else if (cursor.Y > viewport - edge && offset < taskScroll.ScrollableHeight)
            step = ScrollStep(viewport - cursor.Y, edge);
        if (Math.Abs(step) < 0.01)
            return;
        double next = Math.Clamp(offset + step, 0, taskScroll.ScrollableHeight);
        if (Math.Abs(next - offset) > 0.01)
            taskScroll.ScrollToVerticalOffset(next);
    }

    // Scrolling ramps up as the cursor approaches the edge, so long lists stay reachable.
    private static double ScrollStep(double distanceToEdge, double edge)
    {
        double closeness = Math.Clamp(1 - distanceToEdge / edge, 0, 1);
        return 2 + 9 * closeness;
    }

    private void ShowDropLocation(DropLocation? location)
    {
        if (dropLocation == location || taskDragPreview is not { } preview)
            return;
        dropLocation = location;
        int origin = preview.OriginalIndex;
        int destination = origin;
        if (location is { } target)
        {
            int slot = Array.IndexOf(preview.Group, target.Item) + (target.After ? 1 : 0);
            destination = slot - (slot > origin ? 1 : 0);
        }
        int previous = dragDestination;
        dragDestination = destination;
        if (previous == destination)
            return;
        bool animate = Ui.MotionEnabled;
        // Only the rows between the two destinations change their offsets.
        for (int i = Math.Min(previous, destination); i <= Math.Max(previous, destination); i++)
        {
            if (i == origin)
                continue;
            double shift = i >= destination && i < origin ? preview.RowHeight
                : i > origin && i <= destination ? -preview.RowHeight : 0;
            ShiftRow(taskRows[preview.Group[i]].Face, shift, animate);
        }
    }

    private static void ShiftRow(Border row, double to, bool animate, double? from = null)
    {
        if (row.RenderTransform is not TranslateTransform shift)
        {
            if (to == 0 && from is null)
                return;
            shift = new TranslateTransform();
            row.RenderTransform = shift;
        }
        double start = from ?? shift.Y;
        if (animate)
        {
            if (from is null && Math.Abs(((double)shift.GetAnimationBaseValue(TranslateTransform.YProperty)) - to) < 0.1)
                return;
            shift.Y = to;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(start, to, TimeSpan.FromMilliseconds(ReorderMotionMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
        else
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = to;
        }
    }

    private void ClearTaskDrag(bool animateReset = false)
    {
        taskReorderTimer.Stop();
        dropLocation = null;
        TaskRow? row = draggedTask is { } item ? taskRows.GetValueOrDefault(item) : null;
        draggedTask = null;
        if (taskDragPreview is { } preview)
        {
            taskDragLayer.Children.Remove(preview.Ghost);
            taskDragPreview = null;
            bool animate = animateReset && Ui.MotionEnabled;
            foreach (var peer in preview.Group)
                ShiftRow(taskRows[peer].Face, 0, animate);
            if (row is not null)
            {
                Ui.Fade(row.Face, row.Face.Opacity, 1, animateReset ? Ui.FastMotion : 0);
            }
        }
        if (row?.Handle is { IsDragging: true } handle)
            handle.CancelDrag();
    }

    private void MoveTaskBy(TodoItem item, int direction)
    {
        if (!CanReorder)
            return;
        var peers = VisibleTasks().Where(peer => InSameOrderGroup(item, peer)).ToArray();
        int index = Array.IndexOf(peers, item);
        int destination = index + direction;
        if (index >= 0 && destination >= 0 && destination < peers.Length)
            MoveTask(item, new(peers[destination], direction > 0));
    }

    private void MoveTask(TodoItem item, DropLocation location, bool showFeedback = true, TaskDragPreview? preview = null)
    {
        var order = VisibleTasks();
        var peers = order.Where(peer => InSameOrderGroup(item, peer)).ToList();
        int prior = peers.IndexOf(item);
        if (prior < 0)
        {
            ClearTaskDrag(animateReset: true);
            return;
        }
        peers.RemoveAt(prior);
        int target = peers.IndexOf(location.Item);
        int destination = target + (location.After ? 1 : 0);
        if (target < 0 || destination == prior)
        {
            ClearTaskDrag(animateReset: true);
            return;
        }
        peers.Insert(destination, item);
        int index = 0;
        for (int slot = 0; slot < order.Length; ++slot)
            if (InSameOrderGroup(item, order[slot]))
                order[slot] = peers[index++];

        bool keepFocus = taskRows.TryGetValue(item, out var source) && source.Handle?.IsKeyboardFocused == true;
        Point[]? previousPositions = Ui.MotionEnabled ? peers.Select(peer =>
            peer == item && preview is not null
                ? new Point(preview.Position.X, preview.Position.Y)
                : taskRows[peer].Face.TranslatePoint(new Point(), taskDragLayer)).ToArray() : null;
        ClearTaskDrag();
        ReorderRequested?.Invoke(order);
        var updated = VisibleTasks().Where(peer => InSameOrderGroup(item, peer)).ToArray();
        if (!updated.Select(peer => peer.Line).SequenceEqual(peers.Select(peer => peer.Line)) ||
            !taskRows.TryGetValue(updated[destination], out var row) || ReferenceEquals(row, source))
            return;

        if (previousPositions is not null)
        {
            taskScroll.UpdateLayout();
            for (int i = 0; i < updated.Length; i++)
            {
                var face = taskRows[updated[i]].Face;
                double delta = previousPositions[i].Y - TaskRowBounds(face).Y;
                if (Math.Abs(delta) > 0.5)
                    ShiftRow(face, 0, animate: true, from: delta);
            }
        }
        if (keepFocus)
            row.Handle?.Focus();
        if (showFeedback)
            Ui.FadeDecoration(row.Feedback, 1, 0);
    }

    private static bool InSameOrderGroup(TodoItem first, TodoItem second) =>
        first.Done == second.Done && first.Details.Pinned == second.Details.Pinned;
}
