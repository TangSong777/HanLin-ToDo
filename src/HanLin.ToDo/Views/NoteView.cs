using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed class NoteView : Border
{
    private readonly Border card;
    private readonly Border dragArea;
    private readonly Button lockButton;
    private readonly Button completedButton;
    private readonly StackPanel pending = new();
    private readonly StackPanel completed = new();
    private readonly TextBlock notice = Ui.Text("", 12, "OverdueBrush");
    private TodoSnapshot snapshot = new("", []);
    private NotePreferences settings = new();
    private TodoItem? editTarget;
    private TaskEditor? editor;
    private DateTime lastRender;

    public event Action? NewRequested;
    public event Action? SettingsRequested;
    public event Action? LockRequested;
    public event Action? CompletedRequested;
    public event Action<TodoItem, bool>? DoneChanged;
    public Func<TodoItem, string, DateTime?, string?>? SaveEdit { get; set; }

    public FrameworkElement DragArea => dragArea;
    public bool IsLocked => settings.Locked;

    public NoteView()
    {
        Padding = new Thickness(9);
        UseLayoutRounding = true;
        SetResourceReference(TextElement.ForegroundProperty, "InkBrush");

        card = new Border
        {
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.13, BlurRadius = 15, ShadowDepth = 2 }
        };
        card.SetResourceReference(BackgroundProperty, "HeaderBrush");
        card.SetResourceReference(BorderBrushProperty, "EdgeBrush");
        Child = card;
        var layout = new StackPanel();
        card.Child = layout;

        var header = new DockPanel { LastChildFill = true, Height = 46, Margin = new Thickness(16, 0, 9, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(Ui.Button("新建待办", () => NewRequested?.Invoke(), "plus"));
        lockButton = Ui.Button("解锁位置", () => LockRequested?.Invoke(), "lock");
        actions.Children.Add(lockButton);
        actions.Children.Add(Ui.Button("便签设置", () => SettingsRequested?.Invoke(), "settings"));
        header.Children.Add(actions);

        var title = Ui.Text("待办");
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        dragArea = new Border { Background = Brushes.Transparent, Child = title };
        header.Children.Add(dragArea);
        layout.Children.Add(header);
        Title = title;

        var lists = new StackPanel { Margin = new Thickness(11, 0, 11, 5) };
        pending.AutomationName("未完成任务");
        lists.Children.Add(pending);
        completedButton = Ui.Button("已完成", () => CompletedRequested?.Invoke());
        completedButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        completedButton.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
        completedButton.FontSize = 13;
        completedButton.Margin = new Thickness(0, 3, 0, 0);
        lists.Children.Add(completedButton);
        lists.Children.Add(completed);
        layout.Children.Add(new ScrollViewer
        {
            Content = lists,
            MaxHeight = Math.Max(175, Math.Min(455, SystemParameters.WorkArea.Height - 150)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        notice.Margin = new Thickness(16, 0, 16, 10);
        notice.TextWrapping = TextWrapping.Wrap;
        notice.Visibility = Visibility.Collapsed;
        layout.Children.Add(notice);
    }

    private TextBlock Title { get; }

    public void Apply(NotePreferences preferences)
    {
        settings = preferences;
        NoteTheme theme = preferences.Theme;
        Width = theme.Width + 2 * Padding.Left;
        card.CornerRadius = new CornerRadius(theme.Radius);
        TextElement.SetFontFamily(this, new FontFamily(theme.Font));
        TextElement.SetFontSize(this, theme.Size);
        Title.FontSize = theme.Size + 2;
        dragArea.Cursor = settings.Locked ? Cursors.Arrow : Cursors.SizeAll;
        string label = settings.Locked ? "位置已锁定，点击解锁" : "锁定位置";
        lockButton.ToolTip = label;
        AutomationProperties.SetName(lockButton, label);
        lockButton.Content = Ui.Glyph(settings.Locked ? "lock" : "unlock");
        if (settings.Locked)
            lockButton.SetResourceReference(Control.BackgroundProperty, "TintBrush");
        else
            lockButton.Background = Brushes.Transparent;
        Render();
    }

    public void UpdateTasks(TodoSnapshot next)
    {
        snapshot = next;
        Render();
    }

    public void RefreshTime()
    {
        DateTime now = DateTime.Now;
        if (now.Date != lastRender.Date ||
            snapshot.Items.Any(item => !item.Done && item.Due is { } due && due > lastRender && due <= now))
            Render();
    }

    public void ShowNotice(string? message)
    {
        notice.Text = message ?? "";
        notice.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Render()
    {
        lastRender = DateTime.Now;
        pending.Children.Clear();
        completed.Children.Clear();

        TodoItem? ownRow = null;
        if (editTarget is { } target)
        {
            var matches = snapshot.Items.Where(item => item.Line == target.Line).ToArray();
            ownRow = matches.FirstOrDefault(item => item.Offset == target.Offset) ??
                (matches.Length == 1 ? matches[0] : null);
            if (ownRow is null && editor is not null)
                pending.Children.Add(editor);
        }

        var remaining = snapshot.Items.Where(item => !item.Done).OrderBy(item => item.Due ?? DateTime.MaxValue).ToArray();
        var finished = snapshot.Items.Where(item => item.Done).ToArray();
        if (remaining.Length == 0 && (editor is null || ownRow is not null))
        {
            var empty = Ui.Text("暂无待办", 14, "MutedBrush");
            empty.Margin = new Thickness(6, 7, 6, 11);
            pending.Children.Add(empty);
        }
        foreach (var item in remaining)
            pending.Children.Add(item == ownRow && editor is not null ? editor : MakeRow(item));

        completedButton.Visibility = finished.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        completedButton.Content = (settings.ShowCompleted ? "⌄" : "›") + "  已完成 " + finished.Length;
        AutomationProperties.SetName(completedButton, (settings.ShowCompleted ? "折叠" : "展开") + "已完成 " + finished.Length);
        if (settings.ShowCompleted)
        {
            foreach (var item in finished)
                completed.Children.Add(item == ownRow && editor is not null ? editor : MakeRow(item));
        }
    }

    private FrameworkElement MakeRow(TodoItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, settings.Theme.Padding, 0, settings.Theme.Padding) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(30) });

        var check = new CheckBox { Style = (Style)Application.Current.FindResource("SoftCheck"), IsChecked = item.Done };
        AutomationProperties.SetName(check, (item.Done ? "取消完成 " : "完成 ") + item.Title);
        check.Checked += (_, _) => DoneChanged?.Invoke(item, true);
        check.Unchecked += (_, _) => DoneChanged?.Invoke(item, false);
        grid.Children.Add(check);

        var text = new StackPanel { Margin = new Thickness(4, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var title = Ui.Text(item.Title, settings.Theme.Size, item.Done ? "MutedBrush" : "InkBrush");
        title.TextWrapping = TextWrapping.Wrap;
        if (item.Done)
            title.TextDecorations = TextDecorations.Strikethrough;
        text.Children.Add(title);

        if (!item.Done && item.Due is { } due)
        {
            bool overdue = due <= lastRender;
            string date = due.Date == lastRender.Date ? "今天 " + due.ToString("HH:mm", CultureInfo.InvariantCulture) :
                due.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture);
            var time = Ui.Text(overdue ? "逾期 · " + date : date, Math.Max(12, settings.Theme.Size - 3),
                overdue ? "OverdueBrush" : "MutedBrush");
            if (overdue)
            {
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(5, 0, 5, 1),
                    Margin = new Thickness(-1, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = time
                };
                chip.SetResourceReference(BackgroundProperty, "TintBrush");
                text.Children.Add(chip);
            }
            else
                text.Children.Add(time);
        }

        var edit = Ui.Button("编辑 " + item.Title, () => BeginEdit(item), "pencil");
        edit.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(edit, 2);
        grid.Children.Add(edit);

        var row = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(BorderBrushProperty, "LineBrush");
        return row;
    }

    private void BeginEdit(TodoItem item)
    {
        editTarget = item;
        editor = new TaskEditor(item.Title, item.Due, (title, due) =>
        {
            string? error = SaveEdit?.Invoke(item, title, due);
            if (error is null)
                CloseEditor();
            return error;
        }, CloseEditor);
        Render();
        editor.FocusTitle();
    }

    private void CloseEditor()
    {
        editor = null;
        editTarget = null;
        Render();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => lockButton.Focus()));
    }
}

internal static class Accessibility
{
    public static void AutomationName(this DependencyObject element, string name) =>
        AutomationProperties.SetName(element, name);
}
