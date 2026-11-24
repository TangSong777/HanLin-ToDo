using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HanLin.ToDo;

internal sealed class ListsWindow : Window
{
    private readonly ComboBox selector;
    private readonly TextBox name;
    private readonly TextBox group;
    private readonly TextBox memo;
    private readonly Button remove;
    private readonly TextBlock notice = Ui.Text("", 12, "OverdueBrush");
    private readonly Func<string?, string, string?, string?, string?> save;
    private readonly Func<string, string?> removeList;

    private sealed record Choice(string? Id, string Name, string? Group, string? Memo);

    public ListsWindow(NoteTheme theme, TodoCatalog catalog, string currentView,
        Func<string?, string, string?, string?, string?> save, Func<string, string?> removeList)
    {
        Ui.ConfigureText(this);
        this.save = save;
        this.removeList = removeList;
        Title = "HanLin-ToDo · 清单和便笺";
        Icon = Ui.AppIcon();
        Width = 390;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily(theme.Font);
        FontSize = 14;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "InkBrush");

        var fields = new StackPanel { Margin = new Thickness(17, 12, 17, 12) };
        Content = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var options = new[] { new Choice("", "收件箱", null, catalog.InboxMemo) }
            .Concat(catalog.Lists.Select(list => new Choice(list.Id, list.Name, list.Group, list.Memo)))
            .Append(new Choice(null, "新建清单", null, null)).ToArray();
        selector = new ComboBox { ItemsSource = options, DisplayMemberPath = nameof(Choice.Name), MinHeight = 29 };
        selector.AutomationName("选择要管理的清单");
        var selectionRow = new DockPanel();
        var newList = Ui.Button("新建清单", () => selector.SelectedIndex = options.Length - 1);
        newList.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(newList, Dock.Right);
        selectionRow.Children.Add(newList);
        selectionRow.Children.Add(selector);
        fields.Children.Add(selectionRow);

        name = Ui.Input("清单名称");
        name.MaxLength = 50;
        group = Ui.Input("清单分组");
        group.MaxLength = 50;
        memo = Ui.Input("清单便笺");
        memo.AcceptsReturn = true;
        memo.TextWrapping = TextWrapping.Wrap;
        memo.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        memo.Height = 130;
        memo.MaxLength = 48000;
        AddField(fields, "清单名称", name);
        AddField(fields, "分组（可选）", group);
        AddField(fields, "清单便笺", memo);

        notice.TextWrapping = TextWrapping.Wrap;
        notice.Margin = new Thickness(0, 5, 0, 0);
        notice.Visibility = Visibility.Collapsed;
        fields.Children.Add(notice);

        var actions = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        remove = Ui.Button("移除清单", RemoveSelected);
        remove.ToolTip = "清单里的任务和便笺会移到收件箱。";
        DockPanel.SetDock(remove, Dock.Left);
        actions.Children.Add(remove);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", Close);
        cancel.IsCancel = true;
        right.Children.Add(cancel);
        var apply = Ui.Button("保存清单", SaveSelected, primary: true);
        apply.IsDefault = true;
        right.Children.Add(apply);
        actions.Children.Add(right);
        fields.Children.Add(actions);

        selector.SelectionChanged += (_, _) => LoadChoice();
        selector.SelectedItem = options.FirstOrDefault(choice => choice.Id == TodoViews.ListId(currentView)) ?? options[0];
        if (selector.SelectedItem is Choice { Id: null })
            selector.SelectedIndex = 0;
    }

    private void LoadChoice()
    {
        if (selector.SelectedItem is not Choice choice)
            return;
        bool inbox = choice.Id == "";
        name.Text = choice.Id is null ? "" : choice.Name;
        group.Text = choice.Group ?? "";
        memo.Text = choice.Memo ?? "";
        name.IsEnabled = !inbox;
        group.IsEnabled = !inbox;
        remove.IsEnabled = !string.IsNullOrEmpty(choice.Id);
        notice.Visibility = Visibility.Collapsed;
        if (choice.Id is null)
            name.Focus();
    }

    private void SaveSelected()
    {
        if (selector.SelectedItem is not Choice choice)
            return;
        string title = name.Text.Trim();
        if (choice.Id != "" && title.Length == 0)
        {
            ShowNotice("请输入清单名称。");
            name.Focus();
            return;
        }
        string? error = save(choice.Id, title, NonEmpty(group.Text), NonEmpty(memo.Text));
        if (error is null) Close();
        else ShowNotice(error);
    }

    private void RemoveSelected()
    {
        if (selector.SelectedItem is not Choice { Id.Length: > 0 } choice)
            return;
        string? error = removeList(choice.Id);
        if (error is null) Close();
        else ShowNotice(error);
    }

    private static string? NonEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static void AddField(Panel fields, string label, UIElement element)
    {
        var heading = Ui.Text(label, 13, "MutedBrush");
        heading.Margin = new Thickness(0, 8, 0, 3);
        fields.Children.Add(heading);
        fields.Children.Add(element);
    }

    private void ShowNotice(string message)
    {
        notice.Text = message;
        notice.Visibility = Visibility.Visible;
    }
}
