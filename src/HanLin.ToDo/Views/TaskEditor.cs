using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Calendar = System.Windows.Controls.Calendar;
using Forms = System.Windows.Forms;

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
    private bool invalidDate;
    private string hintText = "";

    public TaskEditor(TodoDraft draft, Func<TodoDraft, string?> save, Action cancel,
        bool isCaptureWindow = false)
    {
        this.save = save;
        this.cancel = cancel;
        string name = draft.Title;
        TodoDeadline? due = draft.Due;
        if (!isCaptureWindow)
        {
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(9);
            Margin = new Thickness(0, 3, 0, 4);
            SetResourceReference(BackgroundProperty, "TintBrush");
        }
        TextElement.SetFontSize(this, 13);
        SetResourceReference(TextElement.ForegroundProperty, "InkBrush");

        var fields = new StackPanel();
        title = Ui.Input("任务", name);
        title.MaxLength = 300;
        var taskLabel = Ui.Heading("任务", 14);
        fields.Children.Add(taskLabel);
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
        void RefreshDateInput()
        {
            invalidDate = false;
            time.IsEnabled = date.SelectedDate.HasValue;
            ResetHint();
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
        var cancelButton = Ui.Button("取消", cancel);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(Ui.Button("保存", Commit, primary: true));
        if (isCaptureWindow)
        {
            taskLabel.Margin = new Thickness(0, 0, 0, 5);
            title.MinHeight = 36;
            scheduleLabel.Margin = new Thickness(0, 9, 0, 3);
            date.MinHeight = time.MinHeight = 34;
            details.Margin = new Thickness(0, 10, 0, 0);
            details.Style = (Style)Application.Current.FindResource("SoftDetailsExpander");
            cancelButton.Margin = new Thickness(0, 0, 6, 0);
            Child = CreateCaptureForm(fields, buttons);
            hintText = "Tab 选日期，Enter 确认，再按 Enter 保存";
            ResetHint();
            title.PreviewKeyDown += OpenKeyboardCalendar;
        }
        else
        {
            fields.Children.Add(error);
            fields.Children.Add(buttons);
            Child = fields;
        }
        KeyDown += OnKey;
    }

    private Grid CreateCaptureForm(StackPanel fields, StackPanel buttons)
    {
        fields.Margin = new Thickness(16, 13, 16, 13);
        var scroll = new ScrollViewer
        {
            Content = fields,
            Style = (Style)Application.Current.FindResource("NoteScroll"),
            Focusable = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        error.Margin = new Thickness(0, 0, 12, 0);
        error.FontSize = 11;
        error.VerticalAlignment = VerticalAlignment.Center;
        buttons.Margin = new Thickness(0);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        var footerRow = new Grid();
        footerRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footerRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footerRow.Children.Add(error);
        Grid.SetColumn(buttons, 1);
        footerRow.Children.Add(buttons);
        var footer = new Border
        {
            Child = footerRow,
            Padding = new Thickness(16, 9, 16, 11),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        footer.SetResourceReference(BorderBrushProperty, "LineBrush");

        var form = new Grid();
        form.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new() { Height = GridLength.Auto });
        form.Children.Add(scroll);
        Grid.SetRow(footer, 1);
        form.Children.Add(footer);
        return form;
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
        else if (args.Key != Key.Enter || keyboardCalendar?.DisplayMode != CalendarMode.Month)
            return;

        args.Handled = true;
        date.IsDropDownOpen = false;
        // 第一次 Enter 只确认日期，焦点回到输入框；再按一次 Enter 才保存。
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => title.Focus()));
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
        ResetHint();
    }

    private void ResetHint()
    {
        if (hintText.Length == 0)
        {
            error.Visibility = Visibility.Collapsed;
            return;
        }
        error.Text = hintText;
        error.Visibility = Visibility.Visible;
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
        FontFamily = Ui.Resolve(theme.Font);
        Icon = Ui.AppIcon();
        Width = 402;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        editor = new TaskEditor(new TodoDraft("", null, new()), task =>
        {
            string? error = save(task);
            if (error is null)
                Close();
            return error;
        }, Close, isCaptureWindow: true);
        Content = CreateFrame(theme);
        ContentRendered += (_, _) => FocusEditor();
        SizeChanged += (_, args) =>
        {
            if (args.HeightChanged)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToWorkArea));
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || editor.IsPickerOpen)
                return;
            args.Handled = true;
            Close();
        };
    }

    private Grid CreateFrame(NoteTheme theme)
    {
        var frame = new Grid { Margin = new Thickness(NoteView.ShadowMargin) };
        var corners = new CornerRadius(theme.Radius);
        // Keep the shadow outside the text subtree so it does not affect text rendering.
        var shadow = new Border
        {
            CornerRadius = corners,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.16, BlurRadius = 15, ShadowDepth = 2 }
        };
        shadow.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        frame.Children.Add(shadow);

        var layout = new DockPanel();
        var header = CreateHeader(theme);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(editor);
        var card = new Border { CornerRadius = corners, BorderThickness = new Thickness(1), Child = layout };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush");
        RenderOptions.SetClearTypeHint(card, ClearTypeHint.Enabled);
        frame.Children.Add(card);
        return frame;
    }

    private Border CreateHeader(NoteTheme theme)
    {
        var caption = new DockPanel { Height = 46, Margin = new Thickness(14, 0, 8, 0) };
        var close = Ui.Button("关闭新建窗口", Close, "close");
        close.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);

        var glyph = new Border
        {
            Child = Ui.Glyph("plus"),
            Width = 25, Height = 25, CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        glyph.SetResourceReference(Border.BackgroundProperty, "TintBrush");
        DockPanel.SetDock(glyph, Dock.Left);
        caption.Children.Add(glyph);
        var heading = Ui.Heading("新建待办", theme.Size + 3);
        heading.VerticalAlignment = VerticalAlignment.Center;
        caption.Children.Add(heading);

        var radius = Math.Max(0, theme.Radius - 1);
        var header = new Border
        {
            Child = caption,
            CornerRadius = new CornerRadius(radius, radius, 0, 0),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.SetResourceReference(Border.BackgroundProperty, "HeaderBrush");
        header.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                return;
            args.Handled = true;
            DragMove();
            FitToWorkArea();
        };
        return header;
    }

    private void FitToWorkArea()
    {
        if (!IsVisible)
            return;
        IntPtr window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero || !Native.GetWindowRect(window, out var bounds))
            return;

        var area = Forms.Screen.FromHandle(window).WorkingArea;
        uint dpi = Native.GetDpiForWindow(window);
        if (dpi > 0)
            MaxHeight = Math.Max(1, area.Height * 96.0 / dpi - 32);

        int left = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - bounds.Width));
        int top = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - bounds.Height));
        if (left != bounds.Left || top != bounds.Top)
            Native.SetWindowPos(window, IntPtr.Zero, left, top, 0, 0,
                Native.NoSize | Native.NoZOrder | Native.NoActivate);
    }

    public void FocusEditor()
    {
        Activate();
        editor.FocusTitle();
    }
}
