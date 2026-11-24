using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed partial class NoteView
{
    private TextBox quickInput = null!;
    private DatePicker quickDate = null!;
    private DeadlineTimeBox quickTime = null!;
    private Grid quickDeadlineRow = null!;
    private TextBlock quickDeadlineLabel = null!;
    private TextBox searchInput = null!;
    private DockPanel searchRow = null!;
    private DockPanel undoRow = null!;
    private Button viewButton = null!;
    private TextBlock viewLabel = null!;
    private readonly DispatcherTimer undoTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private string? undoId;
    private bool selecting;
    private readonly HashSet<TodoItem> selected = [];

    public Func<TodoDraft, string?>? AddTask { get; set; }
    public event Action<string>? ViewRequested;
    public event Action<TaskSort>? SortRequested;
    public event Action<string>? ListsRequested;
    public event Action<TodoItem, Func<TodoDetails, TodoDetails>>? DetailsRequested;
    public event Action<TodoItem>? DuplicateRequested;
    public event Action<IReadOnlyList<TodoItem>>? ReorderRequested;
    public event Action<TodoItem>? DeleteRequested;
    public event Action<string>? RestoreRequested;
    public event Action<IReadOnlyList<TodoItem>, string?>? BatchMoveRequested;
    public event Action<IReadOnlyList<TodoItem>>? BatchDeleteRequested;
    public event Action<string, IReadOnlyList<TodoItem>>? ExportRequested;

    private static readonly (string Key, string Label)[] BuiltInViews =
    [
        (TodoViews.All, "全部"), (TodoViews.Inbox, "收件箱"), (TodoViews.Today, "今天"),
        (TodoViews.Pinned, "置顶"), (TodoViews.Planned, "计划"), (TodoViews.Overdue, "逾期"), (TodoViews.Completed, "已完成")
    ];

    private static readonly (TaskSort Mode, string Label)[] SortChoices =
    [
        (TaskSort.Due, "截止日期"), (TaskSort.Title, "任务名称"),
        (TaskSort.Newest, "最新创建"), (TaskSort.Today, "今天优先"), (TaskSort.Manual, "手动顺序")
    ];

    public string ActiveView => BuiltInViews.Any(view => view.Key == settings.TaskView) ||
        settings.TaskView == TodoViews.Trash || snapshot.Catalog.Lists.Any(item => item.Id == settings.TaskView)
        ? settings.TaskView : TodoViews.All;
    private string SearchQuery => searchInput.Text.Trim();

    private StackPanel CreateTaskInput()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 6, 12, 2) };
        quickInput = Ui.Input("添加待办");
        quickInput.MinHeight = 32;
        quickInput.MaxLength = 300;
        var quickRow = new DockPanel();
        var add = Ui.Button("添加", SubmitQuickTask, "plus");
        add.Margin = new Thickness(4, 0, 0, 0);
        add.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(add, Dock.Right);
        quickRow.Children.Add(add);
        quickDate = new DatePicker
        {
            Style = (Style)Application.Current.FindResource("QuickDatePicker"),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            FirstDayOfWeek = DayOfWeek.Monday,
            Tag = "快速选择截止日期",
            Width = 30, Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        quickDate.AutomationName("快速添加截止日期");
        DockPanel.SetDock(quickDate, Dock.Right);
        quickRow.Children.Add(quickDate);
        quickRow.Children.Add(Watermark(quickInput, "输入待办，回车添加"));
        panel.Children.Add(quickRow);
        panel.Children.Add(CreateQuickDeadlineRow());
        quickInput.KeyDown += ConfirmQuickTask;
        quickTime.PreviewKeyDown += ConfirmQuickTask;
        DateTime? dateAtOpen = null;
        quickDate.CalendarOpened += (_, _) => dateAtOpen = quickDate.SelectedDate;
        quickDate.SelectedDateChanged += (_, _) => UpdateQuickDeadline();
        quickDate.CalendarClosed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (quickDate.SelectedDate != dateAtOpen || quickDate.IsKeyboardFocusWithin)
                quickInput.Focus();
        }));

        searchInput = Ui.Input("搜索待办");
        searchInput.MaxLength = 300;
        searchInput.TextChanged += (_, _) => Render();
        searchRow = new DockPanel { Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
        var closeSearch = Ui.Button("关闭搜索", CloseSearch, "close");
        closeSearch.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(closeSearch, Dock.Right);
        searchRow.Children.Add(closeSearch);
        searchRow.Children.Add(Watermark(searchInput, "搜索全部任务"));
        panel.Children.Add(searchRow);
        searchInput.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            CloseSearch();
            args.Handled = true;
        };

        undoRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        var undo = Ui.Button("撤销删除", UndoDelete);
        undo.Padding = new Thickness(4, 0, 4, 0);
        DockPanel.SetDock(undo, Dock.Right);
        undoRow.Children.Add(undo);
        var deleted = Ui.Text("任务已移入回收站", 12, "MutedBrush");
        deleted.VerticalAlignment = VerticalAlignment.Center;
        undoRow.Children.Add(deleted);
        panel.Children.Add(undoRow);
        undoTimer.Tick += (_, _) => ShowUndo(null);
        PreviewKeyDown += TaskShortcuts;
        return panel;
    }

    private Grid CreateQuickDeadlineRow()
    {
        quickDeadlineRow = new Grid { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        quickDeadlineRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        quickDeadlineRow.ColumnDefinitions.Add(new() { Width = new GridLength(75) });
        quickDeadlineRow.ColumnDefinitions.Add(new() { Width = new GridLength(30) });
        quickDeadlineLabel = Ui.Text("", 12, "MutedBrush");
        quickDeadlineLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        quickDeadlineLabel.VerticalAlignment = VerticalAlignment.Center;
        quickDeadlineLabel.Margin = new Thickness(8, 0, 4, 0);
        quickDeadlineRow.Children.Add(quickDeadlineLabel);
        quickTime = new DeadlineTimeBox { FontSize = 12 };
        quickTime.AutomationName("快速截止时间");
        Grid.SetColumn(quickTime, 1);
        quickDeadlineRow.Children.Add(quickTime);
        var clearDate = Ui.Button("清除快速截止日期", () =>
        {
            ClearQuickDeadline();
            ShowNotice(null);
            quickInput.Focus();
        }, "close");
        Grid.SetColumn(clearDate, 2);
        quickDeadlineRow.Children.Add(clearDate);
        return quickDeadlineRow;
    }

    private void UpdateQuickDeadline()
    {
        if (quickDate.SelectedDate is not { } day)
        {
            quickDeadlineRow.Visibility = Visibility.Collapsed;
            quickDate.Background = Brushes.Transparent;
            return;
        }
        quickDeadlineLabel.Text = "截止 · " + day.ToString(day.Year == DateTime.Today.Year ? "M月d日" : "yyyy/M/d",
            CultureInfo.InvariantCulture);
        quickDate.SetResourceReference(Control.BackgroundProperty, "TintBrush");
        bool reveal = quickDeadlineRow.Visibility != Visibility.Visible;
        quickDeadlineRow.Visibility = Visibility.Visible;
        if (reveal) Ui.Reveal(quickDeadlineRow);
    }

    private void ClearQuickDeadline()
    {
        quickDate.SelectedDate = null;
        quickDate.DisplayDate = DateTime.Today;
        quickTime.Reset();
    }

    private void ConfirmQuickTask(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || quickDate.IsDropDownOpen || quickTime.IsDropDownOpen)
            return;
        SubmitQuickTask();
        args.Handled = true;
    }

    private StackPanel CreateTaskNavigation()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        viewButton = Ui.Button("切换清单", ShowViews);
        viewButton.Padding = new Thickness(4, 1, 4, 1);
        viewButton.MinHeight = 25;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        viewLabel = Ui.Text("全部", 12, "MutedBrush");
        viewLabel.VerticalAlignment = VerticalAlignment.Center;
        viewLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        viewLabel.MaxWidth = 92;
        content.Children.Add(viewLabel);
        var chevron = Ui.Glyph("chevron");
        chevron.Width = 10;
        chevron.Height = 10;
        chevron.Margin = new Thickness(3, 0, 1, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(chevron);
        viewButton.Content = content;
        bar.Children.Add(viewButton);
        var search = Ui.Button("搜索待办", ShowSearch, "search");
        search.Height = 26;
        search.Width = 26;
        search.MinHeight = 26;
        search.Margin = new Thickness(0, 0, 5, 0);
        bar.Children.Add(search);
        return bar;
    }

    private void RefreshTaskNavigation()
    {
        string label = selecting ? $"已选 {selected.Count} 项" : TodoViews.Label(ActiveView, snapshot.Catalog);
        viewLabel.Text = label;
        viewButton.ToolTip = selecting ? "批量操作" : "当前清单 · " + label;
        viewButton.AutomationName(selecting ? "批量操作" : "切换清单");
        if (undoId is not null && !snapshot.Catalog.Deleted.Any(task => task.Id == undoId || task.BatchId == undoId))
            ShowUndo(null);
    }

    private void SubmitQuickTask()
    {
        string title = quickInput.Text.Trim();
        if (title.Length == 0)
            return;
        if (title.IndexOfAny(['\r', '\n']) >= 0)
        {
            ShowNotice("请在输入栏输入一行任务名称。");
            return;
        }
        TodoDeadline? due = null;
        if (quickDate.SelectedDate is { } day)
        {
            if (!quickTime.TryRead(out var time))
            {
                ShowNotice(DeadlineTimeBox.InvalidTime);
                quickTime.Focus();
                return;
            }
            due = new TodoDeadline(DateOnly.FromDateTime(day), time);
        }
        string context = ActiveView;
        var now = DateTime.Now;
        if (AddTask?.Invoke(new TodoDraft(title, due, TodoViews.NewDetails(context, now))) is not null)
            return;
        quickInput.Clear();
        ClearQuickDeadline();
        if (SearchQuery.Length > 0)
            CloseSearch();
        if (context is TodoViews.Completed or TodoViews.Trash || context == TodoViews.Planned && due is null ||
            context == TodoViews.Overdue && due?.IsOverdue(now) != true)
            ViewRequested?.Invoke(TodoViews.All);
        quickInput.Focus();
    }

    private void ShowSearch()
    {
        bool reveal = searchRow.Visibility != Visibility.Visible;
        searchRow.Visibility = Visibility.Visible;
        if (reveal) Ui.Reveal(searchRow);
        searchInput.Focus();
        searchInput.SelectAll();
    }

    private void CloseSearch()
    {
        searchRow.Visibility = Visibility.Collapsed;
        searchInput.Clear();
        quickInput.Focus();
    }

    private void TaskShortcuts(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        if (args.Key == Key.F)
            ShowSearch();
        else if (args.Key == Key.N)
            quickInput.Focus();
        else if (args.Key == Key.Z && args.OriginalSource is not TextBoxBase && undoId is not null)
            UndoDelete();
        else
            return;
        args.Handled = true;
    }

    private void ShowViews()
    {
        var menu = CreateMenu();
        menu.PlacementTarget = viewButton;
        menu.Placement = PlacementMode.Top;
        if (selecting)
        {
            FillBatchMenu(menu);
            menu.IsOpen = true;
            return;
        }
        void AddView(ItemCollection collection, string key, string label) => collection.Add(Menu(label, () =>
        {
            if (SearchQuery.Length > 0) CloseSearch();
            ViewRequested?.Invoke(key);
        }, ActiveView == key));
        foreach (var (key, label) in BuiltInViews)
            AddView(menu.Items, key, label);
        menu.Items.Add(MenuDivider());
        foreach (var group in snapshot.Catalog.Lists.GroupBy(item => item.Group))
        {
            var section = menu.Items;
            if (!string.IsNullOrEmpty(group.Key))
            {
                var branch = MenuBranch(group.Key, "folder");
                menu.Items.Add(branch);
                section = branch.Items;
            }
            foreach (var item in group)
                AddView(section, item.Id, item.Name);
        }
        menu.Items.Add(Menu("管理清单和便笺", () => ListsRequested?.Invoke(ActiveView), icon: "folder"));
        if (ActiveView == TodoViews.Today)
        {
            var suggestions = MenuBranch("今日建议", "calendar");
            foreach (var item in snapshot.Items.Where(item => !item.Done && !TodoViews.IsToday(item, DateTime.Now))
                .OrderByDescending(item => item.Details.Pinned).ThenBy(item => item.Due?.SortAt ?? DateTime.MaxValue).Take(8))
                suggestions.Items.Add(Menu(item.Title, () => DetailsRequested?.Invoke(item,
                    details => details with { MyDay = DateOnly.FromDateTime(DateTime.Now) })));
            suggestions.IsEnabled = suggestions.Items.Count > 0;
            menu.Items.Add(suggestions);
        }
        var sorting = MenuBranch("排序", "dots");
        foreach (var (kind, label) in SortChoices)
            sorting.Items.Add(Menu(label, () => SortRequested?.Invoke(kind), settings.Sort == kind));
        menu.Items.Add(sorting);
        if (ActiveView != TodoViews.Trash || SearchQuery.Length > 0)
        {
            menu.Items.Add(Menu("批量选择", () => { selecting = true; Render(); }));
            menu.Items.Add(Menu("导出当前任务", () => ExportRequested?.Invoke(
                SearchQuery.Length > 0 ? "搜索结果" : TodoViews.Label(ActiveView, snapshot.Catalog), VisibleTasks())));
        }
        menu.Items.Add(MenuDivider());
        AddView(menu.Items, TodoViews.Trash, "回收站");
        menu.IsOpen = true;
    }

    private void PopulateItemMenu(ContextMenu menu, TodoItem item)
    {
        menu.Items.Clear();
        menu.Items.Add(Menu("编辑任务", () => BeginEdit(item), icon: "pencil"));
        menu.Items.Add(Menu(item.Details.Pinned ? "取消置顶" : "置顶任务", () => TogglePin(item), icon: "pin"));
        var today = DateOnly.FromDateTime(DateTime.Now);
        menu.Items.Add(Menu(item.Details.MyDay == today ? "移出今日计划" : "加入今日计划", () =>
            DetailsRequested?.Invoke(item, details => details with { MyDay = details.MyDay == today ? null : today }), icon: "calendar"));
        var move = MenuBranch("移到清单", "folder");
        move.Items.Add(Menu("收件箱", () => MoveToList(item, null), item.Details.ListId is null));
        foreach (var list in snapshot.Catalog.Lists)
            move.Items.Add(Menu(list.Name, () => MoveToList(item, list.Id), item.Details.ListId == list.Id));
        menu.Items.Add(move);
        menu.Items.Add(Menu("今天截止", () => ChangeDue(item, new TodoDeadline(DateOnly.FromDateTime(DateTime.Today)))));
        menu.Items.Add(Menu("明天截止", () => ChangeDue(item, new TodoDeadline(DateOnly.FromDateTime(DateTime.Today.AddDays(1))))));
        if (item.Due is not null)
            menu.Items.Add(Menu("清除截止日期", () => ChangeDue(item, null)));
        menu.Items.Add(Menu("复制任务", () => DuplicateRequested?.Invoke(item), icon: "copy"));
        if (CanReorder)
        {
            menu.Items.Add(Menu("上移", () => MoveTaskBy(item, -1)));
            menu.Items.Add(Menu("下移", () => MoveTaskBy(item, 1)));
        }
        menu.Items.Add(MenuDivider());
        var remove = Menu("移到回收站", () => DeleteRequested?.Invoke(item), icon: "trash");
        remove.SetResourceReference(Control.ForegroundProperty, "OverdueBrush");
        menu.Items.Add(remove);
    }

    private void ChangeDue(TodoItem item, TodoDeadline? due) => SaveEdit?.Invoke(item, new TodoDraft(item.Title, due, item.Details));
    private void TogglePin(TodoItem item) => DetailsRequested?.Invoke(item, details => details with { Pinned = !details.Pinned });
    private void MoveToList(TodoItem item, string? id) => DetailsRequested?.Invoke(item, details => details with { ListId = id });

    public void ShowUndo(string? id)
    {
        undoTimer.Stop();
        undoId = id;
        undoRow.Visibility = id is null ? Visibility.Collapsed : Visibility.Visible;
        if (id is not null)
        {
            Ui.Reveal(undoRow);
            undoTimer.Start();
        }
    }

    private void UndoDelete()
    {
        if (undoId is { } id) RestoreRequested?.Invoke(id);
    }

    private void RenderTrash()
    {
        completedButton.Visibility = Visibility.Collapsed;
        taskCount.Text = $"{snapshot.Catalog.Deleted.Length} 项";
        foreach (var item in snapshot.Catalog.Deleted)
        {
            var row = new DockPanel { Margin = new Thickness(4, 3, 2, 1) };
            var restore = Ui.Button("恢复", () => RestoreRequested?.Invoke(item.Id));
            restore.AutomationName("恢复 " + item.Title);
            DockPanel.SetDock(restore, Dock.Right);
            row.Children.Add(restore);
            var title = Ui.Text(item.Title, settings.Theme.Size, "MutedBrush");
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(title);
            pending.Children.Add(row);
        }
        if (snapshot.Catalog.Deleted.Length == 0)
        {
            var empty = Ui.Text("回收站是空的", 14, "MutedBrush");
            empty.Margin = new Thickness(6, 12, 6, 10);
            pending.Children.Add(empty);
        }
    }

    private TodoItem[] VisibleTasks()
    {
        string query = SearchQuery;
        string view = ActiveView;
        return TodoViews.Sort(snapshot.Items.Where(item => query.Length > 0 ? TodoViews.Matches(item, query)
            : TodoViews.Includes(item, view, lastRender)), settings.Sort).ToArray();
    }

    private int RenderMatchingMemos(string query)
    {
        int count = 0;
        foreach (var entry in snapshot.Catalog.Lists.Prepend(new TodoList(TodoViews.Inbox, "收件箱", Memo: snapshot.Catalog.InboxMemo)))
        {
            if (entry.Memo?.Contains(query, StringComparison.CurrentCultureIgnoreCase) != true)
                continue;
            var open = Ui.Button("便笺 · " + entry.Name, () => ListsRequested?.Invoke(entry.Id));
            open.HorizontalContentAlignment = HorizontalAlignment.Left;
            pending.Children.Add(open);
            ++count;
        }
        return count;
    }

    private void SetRowSelectionOrDone(TodoItem item, bool enabled)
    {
        if (!selecting)
        {
            DoneChanged?.Invoke(item, enabled);
            return;
        }
        if (enabled) selected.Add(item);
        else selected.Remove(item);
        RefreshTaskNavigation();
    }

    public void EndSelection()
    {
        selecting = false;
        selected.Clear();
        Render();
    }

    private void FillBatchMenu(ContextMenu menu)
    {
        menu.Items.Add(Menu("选择当前可见的任务", () =>
        {
            foreach (var item in VisibleTasks())
                if (!item.Done || settings.ShowCompleted || ActiveView == TodoViews.Completed || SearchQuery.Length > 0)
                    selected.Add(item);
            Render();
        }));
        if (selected.Count > 0)
        {
            var move = MenuBranch("移动选中的任务", "folder");
            move.Items.Add(Menu("收件箱", () => BatchMoveRequested?.Invoke(selected.ToArray(), null)));
            foreach (var list in snapshot.Catalog.Lists)
                move.Items.Add(Menu(list.Name, () => BatchMoveRequested?.Invoke(selected.ToArray(), list.Id)));
            menu.Items.Add(move);
            menu.Items.Add(Menu($"将 {selected.Count} 项移到回收站", () => BatchDeleteRequested?.Invoke(selected.ToArray())));
        }
        menu.Items.Add(Menu("结束选择", EndSelection));
    }

    private string TaskSummary(TodoItem item)
    {
        var parts = new List<string>();
        if (item.Details.ListId is { } id && id != ActiveView &&
            snapshot.Catalog.Lists.FirstOrDefault(entry => entry.Id == id) is { } list) parts.Add(list.Name);
        if (item.Details.MyDay == DateOnly.FromDateTime(lastRender)) parts.Add("今日");
        if (item.Details.Steps is { Length: > 0 } steps) parts.Add($"{steps.Count(step => step.Done)}/{steps.Length} 步");
        if (item.Details.Reminder is { } reminder) parts.Add("提醒 " + reminder.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture));
        if (item.Details.Repeat != TaskRepeat.None) parts.Add("重复");
        if (item.Details.Memo is not null) parts.Add("备注");
        if (item.Details.Attachments is { Length: > 0 } files) parts.Add($"{files.Length} 附件");
        if (item.Details.Tags is { Length: > 0 } tags) parts.AddRange(tags.Select(tag => "#" + tag));
        return string.Join(" · ", parts);
    }

    private static Grid Watermark(TextBox input, string display)
    {
        var grid = new Grid();
        grid.Children.Add(input);
        var hint = Ui.Text(display, 13, "MutedBrush");
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(8, 0, 4, 0);
        hint.IsHitTestVisible = false;
        grid.Children.Add(hint);
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return grid;
    }

    private static ContextMenu CreateMenu()
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("SoftMenu") };
        menu.Opened += (_, _) => Ui.Reveal(menu);
        return menu;
    }

    private static Separator MenuDivider() => new() { Style = (Style)Application.Current.FindResource("SoftMenuDivider") };

    private static MenuItem MenuBranch(string label, string? icon)
    {
        var header = Ui.Text(label);
        header.TextTrimming = TextTrimming.CharacterEllipsis;
        var item = new MenuItem { Header = header, Style = (Style)Application.Current.FindResource("SoftMenuItem") };
        item.AutomationName(label);
        if (icon is not null) item.Icon = Ui.Glyph(icon);
        return item;
    }

    private static MenuItem Menu(string label, Action action, bool check = false, string? icon = null)
    {
        var item = MenuBranch(label, icon);
        item.IsCheckable = check;
        item.IsChecked = check;
        item.Click += (_, _) => action();
        return item;
    }
}
