using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace HanLin.ToDo;

internal sealed class TaskDetailsEditor : StackPanel
{
    private readonly TodoDetails original;
    private readonly CheckBox important;
    private readonly CheckBox today;
    private readonly ComboBox list;
    private readonly ComboBox repeat;
    private readonly TextBox repeatInterval;
    private readonly DatePicker reminder;
    private readonly TextBox reminderTime;
    private readonly TextBox memo;
    private readonly TextBox tags;
    private readonly TextBox stepInput;
    private readonly TextBox linkInput;
    private readonly StackPanel stepsPanel = new();
    private readonly StackPanel filesPanel = new();
    private readonly List<TodoStep> steps;
    private readonly List<string> attachments;
    private readonly TextBlock notice = Ui.Text("", 12, "OverdueBrush");
    private bool invalidReminder;

    private sealed record RepeatOption(TaskRepeat Kind, string Label);

    private static readonly RepeatOption[] Repeats =
    [
        new(TaskRepeat.None, "不重复"), new(TaskRepeat.Daily, "每天"), new(TaskRepeat.Weekdays, "工作日"),
        new(TaskRepeat.Weekly, "每周"), new(TaskRepeat.Monthly, "每月"), new(TaskRepeat.Yearly, "每年")
    ];

    public bool IsPickerOpen => reminder.IsDropDownOpen || list.IsDropDownOpen || repeat.IsDropDownOpen;

    public TaskDetailsEditor(TodoDetails details, TodoCatalog catalog)
    {
        original = details;
        steps = [..details.Steps ?? []];
        attachments = [..details.Attachments ?? []];
        Margin = new Thickness(0, 5, 1, 1);

        var flags = new WrapPanel();
        important = Flag("标为重要", details.Important);
        today = Flag("加到今天", details.MyDay == DateOnly.FromDateTime(DateTime.Now));
        flags.Children.Add(important);
        flags.Children.Add(today);
        Children.Add(flags);

        list = new ComboBox
        {
            ItemsSource = new[] { new TodoList("", "收件箱") }.Concat(catalog.Lists),
            DisplayMemberPath = nameof(TodoList.Name),
            SelectedValuePath = nameof(TodoList.Id),
            MinHeight = 27
        };
        list.SelectedValue = details.ListId ?? "";
        if (list.SelectedIndex < 0) list.SelectedIndex = 0;
        list.AutomationName("所属清单");
        Field("清单", list);

        repeat = new ComboBox
        {
            ItemsSource = Repeats, DisplayMemberPath = nameof(RepeatOption.Label),
            SelectedValuePath = nameof(RepeatOption.Kind), SelectedValue = details.Repeat, MinHeight = 27
        };
        repeat.AutomationName("重复");
        repeatInterval = Ui.Input("重复间隔", Math.Max(1, details.RepeatInterval).ToString(CultureInfo.InvariantCulture));
        repeatInterval.Width = 38;
        repeatInterval.MaxLength = 3;
        repeatInterval.Margin = new Thickness(5, 0, 0, 0);
        repeatInterval.ToolTip = "重复间隔。1 表示每个周期，2 表示每隔一个周期。";
        repeatInterval.IsEnabled = details.Repeat is not (TaskRepeat.None or TaskRepeat.Weekdays);
        repeat.SelectionChanged += (_, _) => repeatInterval.IsEnabled = repeat.SelectedValue is TaskRepeat mode
            && mode is not (TaskRepeat.None or TaskRepeat.Weekdays);
        var repeatRow = new DockPanel();
        DockPanel.SetDock(repeatInterval, Dock.Right);
        repeatRow.Children.Add(repeatInterval);
        repeatRow.Children.Add(repeat);
        Field("重复方式与间隔", repeatRow);

        reminder = new DatePicker
        {
            Style = (Style)Application.Current.FindResource("SoftDatePicker"),
            Language = XmlLanguage.GetLanguage("zh-CN"), Tag = "打开提醒日历",
            FirstDayOfWeek = DayOfWeek.Monday, SelectedDateFormat = DatePickerFormat.Short,
            SelectedDate = details.Reminder?.Date, DisplayDate = details.Reminder?.Date ?? DateTime.Today
        };
        reminder.AutomationName("提醒日期");
        reminderTime = Ui.Input("提醒时间", details.Reminder?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "09:00");
        reminderTime.Width = 62;
        reminderTime.IsEnabled = details.Reminder.HasValue;
        reminderTime.Margin = new Thickness(5, 0, 0, 0);
        reminder.DateValidationError += (_, e) =>
        {
            invalidReminder = true;
            e.ThrowException = false;
            ShowNotice("请选择有效的提醒日期。");
        };
        reminder.SelectedDateChanged += (_, _) =>
        {
            invalidReminder = false;
            reminderTime.IsEnabled = reminder.SelectedDate.HasValue;
            ShowNotice(null);
        };
        reminder.CalendarOpened += (_, _) => { invalidReminder = false; ShowNotice(null); };
        var clear = Ui.Button("清除提醒", () =>
        {
            reminder.SelectedDate = null;
            reminder.Text = "";
            reminderTime.Text = "09:00";
            invalidReminder = false;
            ShowNotice(null);
        });
        clear.Padding = new Thickness(4, 1, 4, 1);
        clear.FontSize = 11;
        var reminderRow = new DockPanel();
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(reminderTime, Dock.Right);
        reminderRow.Children.Add(clear);
        reminderRow.Children.Add(reminderTime);
        reminderRow.Children.Add(reminder);
        Field("提醒日期（可选）", reminderRow);

        tags = Ui.Input("标签", string.Join("、", details.Tags ?? []));
        tags.MaxLength = 250;
        Field("标签（逗号分隔）", tags);
        memo = Ui.Input("任务备注", details.Memo ?? "");
        memo.AcceptsReturn = true;
        memo.TextWrapping = TextWrapping.Wrap;
        memo.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        memo.Height = 64;
        memo.MaxLength = 12000;
        Field("备注", memo);

        Field("任务步骤", stepsPanel);
        stepInput = Ui.Input("添加步骤");
        stepInput.MaxLength = 300;
        Children.Add(AddLine(stepInput, "添加步骤", AddStep));
        RenderSteps();

        Field("附件和链接", filesPanel);
        var browse = Ui.Button("选择本地文件", Browse);
        browse.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(browse);
        linkInput = Ui.Input("链接地址");
        linkInput.MaxLength = 2048;
        Children.Add(AddLine(linkInput, "添加链接", AddLink));
        RenderAttachments();
        notice.TextWrapping = TextWrapping.Wrap;
        notice.Margin = new Thickness(0, 4, 0, 0);
        notice.Visibility = Visibility.Collapsed;
        Children.Add(notice);
    }

    public bool TryRead(TodoDeadline? due, out TodoDetails details, out string? error)
    {
        details = original;
        error = null;
        if (invalidReminder)
        {
            error = "请选择有效的提醒日期。";
            return false;
        }
        DateTime? remindAt = null;
        if (reminder.SelectedDate is { } day)
        {
            if (!TimeOnly.TryParseExact(reminderTime.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var clock))
            {
                error = "请输入 24 小时制提醒时间，例如 09:00。";
                return false;
            }
            remindAt = day.Date.Add(clock.ToTimeSpan());
        }
        var cadence = repeat.SelectedValue is TaskRepeat value ? value : TaskRepeat.None;
        if (cadence != TaskRepeat.None && due is null)
        {
            error = "请先设置截止日期，再设置重复。";
            return false;
        }
        int interval = 0;
        if (repeatInterval.IsEnabled && (!int.TryParse(repeatInterval.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out interval)
            || interval is < 1 or > 365))
        {
            error = "重复间隔请输入 1 到 365。";
            return false;
        }
        var stepText = stepInput.Text.Trim();
        var allSteps = steps.Where(step => !string.IsNullOrWhiteSpace(step.Title)).ToList();
        if (stepText.Length > 0)
            allSteps.Add(new(stepText, false));
        if (!string.IsNullOrWhiteSpace(linkInput.Text) && !TryMakeLink(linkInput.Text, out _))
        {
            error = "请输入完整的网页链接。";
            return false;
        }
        var allAttachments = attachments.ToList();
        if (TryMakeLink(linkInput.Text, out var link))
            allAttachments.Add(link);
        string[] labels = tags.Text.Split([',', '，', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(label => label.TrimStart('#')).Where(label => label.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        details = details with
        {
            Important = important.IsChecked == true,
            MyDay = today.IsChecked == true ? DateOnly.FromDateTime(DateTime.Now) : null,
            ListId = list.SelectedValue as string,
            Repeat = cadence,
            RepeatInterval = interval,
            Reminder = remindAt,
            Memo = string.IsNullOrWhiteSpace(memo.Text) ? null : memo.Text.Trim(),
            Tags = labels.Length == 0 ? null : labels,
            Steps = allSteps.Count == 0 ? null : allSteps.ToArray(),
            Attachments = allAttachments.Count == 0 ? null : allAttachments.Distinct(StringComparer.Ordinal).ToArray()
        };
        return true;
    }

    private void AddStep()
    {
        string name = stepInput.Text.Trim();
        if (name.Length == 0)
            return;
        steps.Add(new(name, false));
        stepInput.Clear();
        RenderSteps();
        stepInput.Focus();
    }

    private void RenderSteps()
    {
        stepsPanel.Children.Clear();
        for (int index = 0; index < steps.Count; ++index)
        {
            int slot = index;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var done = new CheckBox { IsChecked = steps[slot].Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            done.AutomationName($"完成步骤 {slot + 1}");
            done.Checked += (_, _) => steps[slot] = steps[slot] with { Done = true };
            done.Unchecked += (_, _) => steps[slot] = steps[slot] with { Done = false };
            DockPanel.SetDock(done, Dock.Left);
            row.Children.Add(done);
            var remove = Ui.Button("移除步骤", () => { steps.RemoveAt(slot); RenderSteps(); }, "close");
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var input = Ui.Input($"步骤 {slot + 1}", steps[slot].Title);
            input.MaxLength = 300;
            input.TextChanged += (_, _) => steps[slot] = steps[slot] with { Title = input.Text };
            row.Children.Add(input);
            stepsPanel.Children.Add(row);
        }
    }

    private void Browse()
    {
        var browser = new OpenFileDialog { Multiselect = true, Title = "选择任务附件", CheckFileExists = true };
        if (browser.ShowDialog() != true)
            return;
        foreach (string path in browser.FileNames)
            if (!attachments.Contains(path, StringComparer.OrdinalIgnoreCase))
                attachments.Add(path);
        RenderAttachments();
    }

    private void AddLink()
    {
        if (!TryMakeLink(linkInput.Text, out var link))
        {
            ShowNotice("请输入完整的网页链接。");
            return;
        }
        if (!attachments.Contains(link, StringComparer.Ordinal))
            attachments.Add(link);
        linkInput.Clear();
        ShowNotice(null);
        RenderAttachments();
    }

    private void RenderAttachments()
    {
        filesPanel.Children.Clear();
        foreach (string path in attachments)
        {
            var row = new DockPanel();
            var remove = Ui.Button("移除附件", () => { attachments.Remove(path); RenderAttachments(); }, "close");
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            string name = TryMakeLink(path, out _) ? path : Path.GetFileName(path);
            var open = Ui.Button("打开 " + name, () => OpenAttachment(path));
            var text = Ui.Text(name, 12);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            open.Content = text;
            open.ToolTip = path;
            open.HorizontalContentAlignment = HorizontalAlignment.Left;
            row.Children.Add(open);
            filesPanel.Children.Add(row);
        }
    }

    private void OpenAttachment(string path)
    {
        try
        {
            if (TryMakeLink(path, out var link))
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                ShowNotice("找不到这个本地文件。");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            ShowNotice("附件暂时无法打开。");
        }
    }

    private static bool TryMakeLink(string text, out string link)
    {
        link = "";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return false;
        link = uri.AbsoluteUri;
        return true;
    }

    private static CheckBox Flag(string label, bool enabled)
    {
        var box = new CheckBox { Content = label, IsChecked = enabled, Margin = new Thickness(0, 1, 9, 1) };
        box.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        return box;
    }

    private void Field(string label, UIElement field)
    {
        var title = Ui.Text(label, 12, "MutedBrush");
        title.Margin = new Thickness(0, 6, 0, 2);
        Children.Add(title);
        Children.Add(field);
    }

    private static DockPanel AddLine(TextBox input, string label, Action add)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 1) };
        var button = Ui.Button(label, add);
        button.Padding = new Thickness(5, 3, 5, 3);
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(input);
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            add();
            args.Handled = true;
        };
        return row;
    }

    private void ShowNotice(string? message)
    {
        notice.Text = message ?? "";
        notice.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
