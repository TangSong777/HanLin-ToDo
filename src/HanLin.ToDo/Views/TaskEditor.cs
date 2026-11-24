using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Calendar = System.Windows.Controls.Calendar;

namespace HanLin.ToDo;

internal sealed class TaskEditor : Border
{
    private static readonly CultureInfo DateCulture = CultureInfo.GetCultureInfo("zh-CN");
    private readonly TextBox title;
    private readonly DatePicker date;
    private readonly DeadlineTimeBox time;
    private readonly TextBlock error;
    private readonly TaskDetailsEditor detailsEditor;
    private readonly Func<TodoDraft, string?> save;
    private readonly Action cancel;
    private Calendar? keyboardCalendar;
    private DateTime? dateBeforeKeyboardSelection;
    private bool saveAfterCalendarCloses;
    private bool invalidDate;

    public TaskEditor(TodoDraft draft, Func<TodoDraft, string?> save, Action cancel,
        bool keyboardDateShortcut = false)
    {
        this.save = save;
        this.cancel = cancel;
        string name = draft.Title;
        TodoDeadline? due = draft.Due;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(9);
        Margin = new Thickness(0, 3, 0, 4);
        SetResourceReference(BackgroundProperty, "TintBrush");
        TextElement.SetFontSize(this, 13);
        SetResourceReference(TextElement.ForegroundProperty, "InkBrush");

        var fields = new StackPanel();
        Child = fields;
        title = Ui.Input("任务", name);
        title.MaxLength = 300;
        fields.Children.Add(Ui.Heading("任务", 14));
        fields.Children.Add(title);

        date = new DatePicker
        {
            Style = (Style)Application.Current.FindResource("SoftDatePicker"),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            FirstDayOfWeek = DayOfWeek.Monday,
            Tag = "打开日历",
            SelectedDateFormat = DatePickerFormat.Short,
            DisplayDate = due?.Date.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today,
            SelectedDate = due?.Date.ToDateTime(TimeOnly.MinValue)
        };
        date.AutomationName("截止日期");
        time = new DeadlineTimeBox(due?.Time) { IsEnabled = due.HasValue };
        time.AutomationName("截止时间");

        var scheduleLabel = new DockPanel { Margin = new Thickness(0, 5, 0, 1) };
        var clear = Ui.Button("清除日期", ClearDeadline);
        clear.FontSize = 11;
        clear.MinHeight = 22;
        clear.Padding = new Thickness(5, 0, 5, 0);
        DockPanel.SetDock(clear, Dock.Right);
        scheduleLabel.Children.Add(clear);
        var label = Ui.Text("截止日期（可选）", 12, "MutedBrush");
        label.VerticalAlignment = VerticalAlignment.Center;
        scheduleLabel.Children.Add(label);
        fields.Children.Add(scheduleLabel);

        var schedule = new Grid();
        schedule.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        schedule.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
        schedule.Children.Add(date);
        time.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(time, 1);
        schedule.Children.Add(time);
        fields.Children.Add(schedule);

        detailsEditor = new TaskDetailsEditor(draft.Details);
        var details = new Expander
        {
            Header = Ui.Heading("任务详情", 13),
            Content = detailsEditor,
            Margin = new Thickness(0, 6, 0, 0)
        };
        details.AutomationName("任务详情");
        fields.Children.Add(details);

        error = Ui.Text("", 12, "OverdueBrush");
        error.TextWrapping = TextWrapping.Wrap;
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 5, 0, 0);
        fields.Children.Add(error);
        void RefreshDateInput()
        {
            invalidDate = false;
            time.IsEnabled = date.SelectedDate.HasValue;
            error.Visibility = Visibility.Collapsed;
        }
        date.SelectedDateChanged += (_, _) =>
        {
            if (!date.IsDropDownOpen)
                RefreshDateInput();
        };
        date.CalendarClosed += (_, _) =>
        {
            if (keyboardCalendar is { } calendar)
            {
                calendar.PreviewKeyDown -= OnKeyboardCalendarKey;
                keyboardCalendar = null;
            }
            RefreshDateInput();
            if (saveAfterCalendarCloses)
            {
                saveAfterCalendarCloses = false;
                Commit();
            }
        };
        date.DateValidationError += (_, args) =>
        {
            args.ThrowException = false;
            invalidDate = true;
            ShowError("日期格式无效，可以打开日历选择。");
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        buttons.Children.Add(Ui.Button("取消", cancel));
        buttons.Children.Add(Ui.Button("保存", Commit, primary: true));
        fields.Children.Add(buttons);
        if (keyboardDateShortcut)
        {
            title.ToolTip = "Tab 打开日历，方向键选日期，Enter 保存。";
            title.PreviewKeyDown += OpenKeyboardCalendar;
        }
        KeyDown += OnKey;
    }

    public bool IsPickerOpen => date.IsDropDownOpen || time.IsDropDownOpen || detailsEditor.IsPickerOpen;

    public void FocusTitle() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        if (title.IsVisible && !IsPickerOpen && title.Focus())
            title.SelectAll();
    }));

    private void OpenKeyboardCalendar(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Tab || args.KeyboardDevice.Modifiers != ModifierKeys.None || IsPickerOpen)
            return;

        date.ApplyTemplate();
        if (date.Template.FindName("PART_Popup", date) is not Popup { Child: Calendar calendar })
            return;

        dateBeforeKeyboardSelection = date.SelectedDate;
        DateTime start = dateBeforeKeyboardSelection ?? DateTime.Today;
        date.SelectedDate = start;
        date.DisplayDate = start;
        keyboardCalendar = calendar;
        calendar.PreviewKeyDown += OnKeyboardCalendarKey;
        args.Handled = true;
        date.IsDropDownOpen = true;
    }

    private void OnKeyboardCalendarKey(object sender, KeyEventArgs args)
    {
        if (!date.IsDropDownOpen || args.KeyboardDevice.Modifiers != ModifierKeys.None)
            return;

        if (args.Key == Key.Escape)
            date.SelectedDate = dateBeforeKeyboardSelection;
        else if (args.Key == Key.Enter && keyboardCalendar?.DisplayMode == CalendarMode.Month)
            saveAfterCalendarCloses = true;
        else
            return;

        args.Handled = true;
        date.IsDropDownOpen = false;
        if (args.Key == Key.Escape)
            title.Focus();
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (IsPickerOpen)
            return;
        if (args.Key == Key.Escape)
        {
            cancel();
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && (title.IsKeyboardFocused || date.IsKeyboardFocusWithin || time.IsKeyboardFocusWithin))
        {
            Commit();
            args.Handled = true;
        }
    }

    private void Commit()
    {
        string name = title.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(['\r', '\n']) >= 0)
        {
            ShowError("请输入一行任务名称。");
            title.Focus();
            return;
        }

        if (invalidDate)
        {
            ShowError("日期格式无效，可以打开日历选择。");
            date.Focus();
            return;
        }
        TodoDeadline? due = null;
        string dateText = date.Text.Trim();
        if (dateText.Length > 0)
        {
            if (!DateTime.TryParse(dateText, DateCulture, DateTimeStyles.AllowWhiteSpaces, out var day))
            {
                ShowError("请选择日期，或输入年-月-日。");
                date.Focus();
                return;
            }
            if (!time.TryRead(out var clock))
            {
                ShowError(DeadlineTimeBox.InvalidTime);
                time.FocusInput();
                return;
            }
            due = new TodoDeadline(DateOnly.FromDateTime(day), clock);
        }

        if (!detailsEditor.TryRead(due, out var details, out string? detailError))
        {
            ShowError(detailError!);
            error.BringIntoView();
            return;
        }
        string? message = save(new TodoDraft(name, due, details));
        if (message is not null)
            ShowError(message);
    }

    private void ClearDeadline()
    {
        date.SelectedDate = null;
        date.Text = "";
        time.Reset();
        time.IsEnabled = false;
        invalidDate = false;
        error.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        error.Text = message;
        error.Visibility = Visibility.Visible;
    }
}

internal sealed class CaptureWindow : Window
{
    private readonly TaskEditor editor;

    public CaptureWindow(NoteTheme theme, Func<TodoDraft, string?> save)
    {
        Ui.ConfigureText(this);
        Title = "HanLin-ToDo · 新建待办";
        FontFamily = new System.Windows.Media.FontFamily(theme.Font);
        Icon = Ui.AppIcon();
        Width = 384;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        editor = new TaskEditor(new TodoDraft("", null, new()), task =>
        {
            string? error = save(task);
            if (error is null)
                Close();
            return error;
        }, Close, keyboardDateShortcut: true);
        editor.Margin = new Thickness(10);
        Content = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ContentRendered += (_, _) => editor.FocusTitle();
    }

    public void FocusEditor()
    {
        Activate();
        editor.FocusTitle();
    }
}
