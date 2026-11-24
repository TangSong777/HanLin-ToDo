using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed class TaskEditor : Border
{
    private static readonly string[] DateFormats = ["yyyy-M-d H:mm", "yyyy-M-d'T'H:mm"];
    private readonly TextBox title;
    private readonly TextBox deadline;
    private readonly TextBlock error;
    private readonly Func<string, DateTime?, string?> save;
    private readonly Action cancel;

    public TaskEditor(string name, DateTime? due, Func<string, DateTime?, string?> save, Action cancel)
    {
        this.save = save;
        this.cancel = cancel;
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
        deadline = Ui.Input("截止时间", due?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "");
        deadline.MaxLength = 25;
        fields.Children.Add(Ui.Text("任务"));
        fields.Children.Add(title);
        var label = Ui.Text("截止时间（可空）", 12, "MutedBrush");
        label.Margin = new Thickness(0, 7, 0, 2);
        fields.Children.Add(label);
        fields.Children.Add(deadline);
        deadline.ToolTip = "例如 2026-11-25 18:00";

        error = Ui.Text("", 12, "OverdueBrush");
        error.TextWrapping = TextWrapping.Wrap;
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 5, 0, 0);
        fields.Children.Add(error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        buttons.Children.Add(Ui.Button("取消", cancel));
        buttons.Children.Add(Ui.Button("保存", Commit, primary: true));
        fields.Children.Add(buttons);
        KeyDown += OnKey;
    }

    public void FocusTitle() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        Keyboard.Focus(title);
        title.SelectAll();
    }));

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            cancel();
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && (title.IsKeyboardFocused || deadline.IsKeyboardFocused))
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

        DateTime? due = null;
        string value = deadline.Text.Trim();
        if (value.Length > 0)
        {
            if (!DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                ShowError("请用年-月-日 时:分，例如 2026-11-25 18:00。");
                deadline.Focus();
                return;
            }
            due = date;
        }

        string? message = save(name, due);
        if (message is not null)
            ShowError(message);
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

    public CaptureWindow(NoteTheme theme, Func<string, DateTime?, string?> save)
    {
        Title = "HanLin-ToDo · 新建待办";
        FontFamily = new System.Windows.Media.FontFamily(theme.Font);
        Icon = Ui.AppIcon();
        Width = 350;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        editor = new TaskEditor("", null, (title, due) =>
        {
            string? error = save(title, due);
            if (error is null)
                Close();
            return error;
        }, Close);
        editor.Margin = new Thickness(10);
        Content = editor;
        ContentRendered += (_, _) => editor.FocusTitle();
    }

    public void FocusEditor()
    {
        Activate();
        editor.FocusTitle();
    }
}
