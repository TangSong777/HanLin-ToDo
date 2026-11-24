using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed class NoteView : Border
{
    public const int ShadowMargin = 9;
    private readonly Border card;
    private readonly Border titleBand;
    private readonly Grid layout;
    private readonly Button lockButton;
    private readonly Border lockGlyph;
    private readonly TextBlock lockText;
    private readonly FrameworkElement dragGrip;
    private readonly Button completedButton;
    private readonly StackPanel pending = new();
    private readonly StackPanel completed = new();
    private readonly TextBlock notice = Ui.Text("", 12, "OverdueBrush");
    private readonly TextBlock taskCount = Ui.Text("", 12, "MutedBrush");
    private TodoSnapshot snapshot = new("", []);
    private NotePreferences settings = new();
    private TodoItem? editTarget;
    private TaskEditor? editor;
    private DateTime lastRender;
    private bool initialized;

    public event Action? NewRequested;
    public event Action? SettingsRequested;
    public event Action? LockRequested;
    public event Action? CompletedRequested;
    public event Action<TodoItem, bool>? DoneChanged;
    public Func<TodoItem, string, DateTime?, string?>? SaveEdit { get; set; }
    public bool IsLocked => settings.Locked;

    public NoteView()
    {
        Padding = new Thickness(ShadowMargin);
        UseLayoutRounding = true;
        SetResourceReference(TextElement.ForegroundProperty, "InkBrush");

        card = new Border
        {
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.13, BlurRadius = 15, ShadowDepth = 2 }
        };
        card.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(BorderBrushProperty, "EdgeBrush");
        Child = card;
        layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        card.Child = layout;

        var header = new DockPanel { LastChildFill = true, Height = 46, Margin = new Thickness(13, 0, 8, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(Ui.Button("新建待办", () => NewRequested?.Invoke(), "plus"));
        lockButton = Ui.Button("锁定位置", () => LockRequested?.Invoke());
        lockButton.Height = 30;
        lockButton.Padding = new Thickness(6, 3, 6, 3);
        lockGlyph = new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        lockText = Ui.Text("锁定", 12);
        lockText.VerticalAlignment = VerticalAlignment.Center;
        var lockContent = new StackPanel { Orientation = Orientation.Horizontal };
        lockContent.Children.Add(lockGlyph);
        lockContent.Children.Add(lockText);
        lockButton.Content = lockContent;
        actions.Children.Add(lockButton);
        actions.Children.Add(Ui.Button("便签设置", () => SettingsRequested?.Invoke(), "settings"));
        header.Children.Add(actions);

        var title = Ui.Text("待办");
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        var caption = new StackPanel { Orientation = Orientation.Horizontal };
        dragGrip = Ui.Glyph("grip");
        dragGrip.VerticalAlignment = VerticalAlignment.Center;
        dragGrip.Margin = new Thickness(0, 0, 5, 0);
        caption.Children.Add(dragGrip);
        caption.Children.Add(title);
        header.Children.Add(caption);
        titleBand = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBand.SetResourceReference(BackgroundProperty, "HeaderBrush");
        titleBand.SetResourceReference(BorderBrushProperty, "LineBrush");
        layout.Children.Add(titleBand);
        Title = title;

        var lists = new StackPanel { Margin = new Thickness(11, 2, 11, 6) };
        pending.AutomationName("未完成任务");
        lists.Children.Add(pending);
        completedButton = Ui.Button("已完成", () => CompletedRequested?.Invoke());
        completedButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        completedButton.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
        completedButton.FontSize = 13;
        completedButton.Margin = new Thickness(0, 3, 0, 0);
        lists.Children.Add(completedButton);
        lists.Children.Add(completed);
        var scroll = new ScrollViewer
        {
            Style = (Style)Application.Current.FindResource("NoteScroll"),
            Content = lists,
            Margin = new Thickness(0, 0, 7, 0),
            Focusable = false,
            PanningMode = PanningMode.VerticalOnly,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        notice.Margin = new Thickness(0, 3, 6, 4);
        notice.TextWrapping = TextWrapping.Wrap;
        notice.Visibility = Visibility.Collapsed;
        var footer = new DockPanel { LastChildFill = true, MinHeight = 27, Margin = new Thickness(14, 0, 6, 0) };
        var sizeGrip = Ui.Glyph("resize");
        sizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
        sizeGrip.Margin = new Thickness(4, 0, 0, 3);
        sizeGrip.ToolTip = "拖动调整便签大小";
        DockPanel.SetDock(sizeGrip, Dock.Right);
        footer.Children.Add(sizeGrip);
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(taskCount);
        status.Children.Add(notice);
        footer.Children.Add(status);
        var footerBand = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBand.SetResourceReference(BorderBrushProperty, "LineBrush");
        Grid.SetRow(footerBand, 2);
        layout.Children.Add(footerBand);
        QueryCursor += (_, args) =>
        {
            if (!IsCardSurface(args.OriginalSource))
                return;
            var edges = ResizeEdgesAt(Mouse.GetPosition(this));
            var cursor = edges switch
            {
                ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
                ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
                ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
                ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
                _ => IsLocked ? null : Cursors.SizeAll
            };
            if (cursor is null)
                return;
            args.Cursor = cursor;
            args.Handled = true;
        };
    }

    private TextBlock Title { get; }

    public void Apply(NotePreferences preferences)
    {
        var previous = settings;
        settings = preferences;
        NoteTheme theme = preferences.Theme;
        if (!initialized || theme.Scale != previous.Theme.Scale || theme.Radius != previous.Theme.Radius)
        {
            double scale = theme.Scale / 100.0;
            layout.LayoutTransform = new ScaleTransform(scale, scale);
            card.CornerRadius = new CornerRadius(theme.Radius);
            double innerRadius = Math.Max(0, theme.Radius - 1) / scale;
            titleBand.CornerRadius = new CornerRadius(innerRadius, innerRadius, 0, 0);
        }
        if (!initialized || theme.Font != previous.Theme.Font)
            TextElement.SetFontFamily(this, new FontFamily(theme.Font));
        if (!initialized || theme.Size != previous.Theme.Size)
        {
            TextElement.SetFontSize(this, theme.Size);
            Title.FontSize = theme.Size + 2;
        }
        dragGrip.Visibility = settings.Locked ? Visibility.Hidden : Visibility.Visible;
        string label = settings.Locked ? "位置已锁定，点击解锁" : "锁定位置";
        lockButton.ToolTip = settings.Locked ? "点击解锁后可拖动便签。仍可从右侧或底部调整大小。" : "点击固定便签的位置。";
        AutomationProperties.SetName(lockButton, label);
        lockGlyph.Child = Ui.Glyph(settings.Locked ? "lock" : "unlock");
        lockText.Text = settings.Locked ? "解锁" : "锁定";
        if (settings.Locked)
            lockButton.SetResourceReference(Control.BackgroundProperty, "TintBrush");
        else
            lockButton.Background = Brushes.Transparent;
        if (!initialized || previous.ShowCompleted != settings.ShowCompleted
            || previous.Theme.Size != theme.Size || previous.Theme.Padding != theme.Padding)
            Render();
        initialized = true;
    }

    public ResizeEdge ResizeEdgesAt(Point point)
    {
        double left = ShadowMargin, top = ShadowMargin;
        double right = ActualWidth - ShadowMargin, bottom = ActualHeight - ShadowMargin;
        if (point.X < left || point.Y < top || point.X > right || point.Y > bottom)
            return ResizeEdge.None;

        double gripBand = 25 * settings.Theme.Scale / 100.0;
        if (point.X >= right - gripBand && point.Y >= bottom - gripBand)
            return ResizeEdge.BottomRight;

        const double edgeBand = 7;
        var edges = ResizeEdge.None;
        if (!IsLocked && point.X < left + edgeBand)
            edges |= ResizeEdge.Left;
        if (point.X > right - edgeBand)
            edges |= ResizeEdge.Right;
        if (!IsLocked && point.Y < top + edgeBand)
            edges |= ResizeEdge.Top;
        if (point.Y > bottom - edgeBand)
            edges |= ResizeEdge.Bottom;
        return edges;
    }

    public bool IsCardSurface(object? origin)
    {
        var element = origin as DependencyObject;
        while (element is not null)
        {
            if (element is ButtonBase or TextBoxBase or RangeBase or Thumb or TaskEditor)
                return false;
            if (element == card)
                return true;
            element = element switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                FrameworkContentElement content => content.Parent,
                _ => null
            };
        }
        return false;
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
        taskCount.Text = remaining.Length == 0 ? "全部完成" : $"{remaining.Length} 项待办";
        taskCount.Visibility = snapshot.Items.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        if (remaining.Length == 0 && (editor is null || ownRow is not null))
        {
            var empty = Ui.Text("暂无待办", 14, "MutedBrush");
            empty.Margin = new Thickness(6, 12, 6, 10);
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
        editor.BringIntoView();
    }

    private void CloseEditor()
    {
        editor = null;
        editTarget = null;
        Render();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => lockButton.Focus()));
    }
}

[Flags]
internal enum ResizeEdge
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    TopLeft = Top | Left,
    TopRight = Top | Right,
    BottomLeft = Bottom | Left,
    BottomRight = Bottom | Right
}

internal static class Accessibility
{
    public static void AutomationName(this DependencyObject element, string name) =>
        AutomationProperties.SetName(element, name);
}
