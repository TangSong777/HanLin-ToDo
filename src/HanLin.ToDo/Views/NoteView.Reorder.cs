using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed partial class NoteView
{
    private sealed record TaskRow(Border Face, Border Insertion, Thumb? Handle, Border Feedback);
    private sealed record DropLocation(TodoItem Item, bool After);

    private readonly Dictionary<TodoItem, TaskRow> taskRows = [];
    private readonly DispatcherTimer taskReorderTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private TodoItem? draggedTask;
    private DropLocation? dropLocation;
    private Point dragOrigin;
    private bool draggingTask;

    private bool CanReorder => !selecting && SearchQuery.Length == 0 && ActiveView != TodoViews.Trash && editor is null;

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
            if (!CanReorder)
                return;
            draggedTask = item;
            dragOrigin = Mouse.GetPosition(taskScroll);
            handle.Focus();
        };
        handle.DragDelta += (_, _) => ContinueTaskDrag(scroll: false);
        handle.DragCompleted += (_, args) =>
        {
            if (draggedTask != item)
                return;
            if (!args.Canceled && draggingTask)
                ContinueTaskDrag(scroll: false);
            var destination = args.Canceled ? null : dropLocation;
            ClearTaskDrag();
            if (destination is not null)
                MoveTask(item, destination);
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

    private void ContinueTaskDrag(bool scroll)
    {
        if (draggedTask is not { } item || !taskRows.TryGetValue(item, out var source))
            return;
        var cursor = Mouse.GetPosition(taskScroll);
        if (!draggingTask)
        {
            if (Math.Abs(cursor.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            draggingTask = true;
            source.Face.SetResourceReference(Border.BackgroundProperty, "TintBrush");
            taskReorderTimer.Start();
        }

        double viewport = taskScroll.ActualHeight;
        if (cursor.X < -16 || cursor.X > taskScroll.ActualWidth + 16 || cursor.Y < -18 || cursor.Y > viewport + 18)
        {
            ShowDropLocation(null);
            return;
        }

        if (scroll)
        {
            double edge = Math.Min(25, viewport / 4);
            if (cursor.Y < edge)
                taskScroll.ScrollToVerticalOffset(taskScroll.VerticalOffset - 14);
            else if (cursor.Y > viewport - edge)
                taskScroll.ScrollToVerticalOffset(taskScroll.VerticalOffset + 14);
        }

        var targets = taskRows.Where(pair => pair.Key != item && InSameOrderGroup(item, pair.Key))
            .Select(pair => (pair.Key, Bounds: pair.Value.Face.TransformToAncestor(taskScroll)
                .TransformBounds(new Rect(pair.Value.Face.RenderSize))))
            .OrderBy(pair => pair.Bounds.Top).ToArray();
        var before = targets.FirstOrDefault(pair => cursor.Y < pair.Bounds.Top + pair.Bounds.Height / 2).Key;
        var last = targets.LastOrDefault().Key;
        ShowDropLocation(before is not null ? new(before, false) : last is not null ? new(last, true) : null);
    }

    private void ShowDropLocation(DropLocation? location)
    {
        if (location == dropLocation)
            return;
        if (dropLocation is { } previous && taskRows.TryGetValue(previous.Item, out var previousRow))
            previousRow.Insertion.Visibility = Visibility.Collapsed;
        dropLocation = location;
        if (location is null || !taskRows.TryGetValue(location.Item, out var row))
            return;
        row.Insertion.VerticalAlignment = location.After ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        row.Insertion.Visibility = Visibility.Visible;
    }

    private void ClearTaskDrag()
    {
        taskReorderTimer.Stop();
        ShowDropLocation(null);
        if (draggedTask is { } item && taskRows.TryGetValue(item, out var row))
            row.Face.ClearValue(Border.BackgroundProperty);
        draggedTask = null;
        draggingTask = false;
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

    private void MoveTask(TodoItem item, DropLocation location)
    {
        var order = VisibleTasks();
        var peers = order.Where(peer => InSameOrderGroup(item, peer)).ToList();
        int prior = peers.IndexOf(item);
        if (prior < 0)
            return;
        peers.RemoveAt(prior);
        int target = peers.IndexOf(location.Item);
        int destination = target + (location.After ? 1 : 0);
        if (target < 0 || destination == prior)
            return;
        peers.Insert(destination, item);
        int index = 0;
        for (int slot = 0; slot < order.Length; ++slot)
            if (InSameOrderGroup(item, order[slot]))
                order[slot] = peers[index++];

        bool keepFocus = taskRows.TryGetValue(item, out var source) && source.Handle?.IsKeyboardFocused == true;
        ReorderRequested?.Invoke(order);
        var moved = taskRows.FirstOrDefault(pair => pair.Key.Line == item.Line);
        if (moved.Value is { } row)
        {
            if (keepFocus) row.Handle?.Focus();
            Ui.FadeDecoration(row.Feedback, 1, 0);
        }
    }

    private static bool InSameOrderGroup(TodoItem first, TodoItem second) =>
        first.Done == second.Done && first.Details.Pinned == second.Details.Pinned;
}
