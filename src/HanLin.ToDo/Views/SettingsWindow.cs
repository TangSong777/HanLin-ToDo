using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HanLin.ToDo;

internal sealed class SettingsWindow : Window
{
    private readonly StackPanel fields = SectionContent();
    private readonly TextBox accent;
    private readonly TextBox surface;
    private readonly FontPicker font;
    private readonly FontPicker titleFont;
    private readonly Slider size;
    private readonly Slider radius;
    private readonly Slider padding;
    private readonly Slider zoom;
    private readonly TextBox width;
    private readonly TextBox height;
    private readonly TextBox workingDirectory;
    private readonly CheckBox startup;
    private readonly TextBlock sizeLockHint = Ui.Text("解锁便签后可调整尺寸和界面比例。", 12, "MutedBrush");
    private readonly TextBlock message = Ui.Text("", 12, "OverdueBrush");
    private int displayedWidth;
    private int displayedHeight;
    private int displayedScale;

    public SettingsWindow(NotePreferences preferences, bool startWithWindows, string currentDirectory, string? defaultDirectory,
        Action openTasks, Action openFolder, Func<NoteTheme, bool, string, string?> save)
    {
        var theme = preferences.Theme;
        Ui.ConfigureText(this);
        Ui.SetTheme(new NoteTheme(), Resources);
        Title = "HanLin-ToDo · 设置";
        Icon = Ui.AppIcon();
        Width = Math.Min(600, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(454, SystemParameters.WorkArea.Height - 24);
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        SetResourceReference(TextElement.ForegroundProperty, "InkBrush");
        var frame = new DockPanel();
        var card = new Border { Child = frame, CornerRadius = new(10), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush");
        RenderOptions.SetClearTypeHint(card, ClearTypeHint.Enabled);
        Content = card;

        var header = CreateHeader();
        DockPanel.SetDock(header, Dock.Top);
        frame.Children.Add(header);
        var tabs = new TabControl { Margin = new Thickness(20, 12, 20, 0) };
        tabs.SetResourceReference(StyleProperty, "SettingsTabs");
        tabs.AutomationName("设置分类");
        var general = SectionContent();
        var data = SectionContent();
        var about = SectionContent();
        AddSection(tabs, "常规", general);
        AddSection(tabs, "外观", fields);
        AddSection(tabs, "数据", data);
        AddSection(tabs, "关于", about);
        var heading = Ui.Heading("尺寸和比例", 16);
        heading.Margin = new Thickness(0, 0, 0, 7);
        fields.Children.Add(heading);

        displayedWidth = theme.Width;
        displayedHeight = theme.Height;
        displayedScale = theme.Scale;
        var dimensions = TwoColumns();
        width = AddDimension(dimensions, "宽度", theme.Width, 0);
        height = AddDimension(dimensions, "高度", theme.Height, 2);
        fields.Children.Add(dimensions);
        zoom = Scale("界面比例", theme.Scale, NoteTheme.MinimumScale, NoteTheme.MaximumScale, "%", 5);
        sizeLockHint.Margin = new Thickness(0, 5, 0, 0);
        fields.Children.Add(sizeLockHint);
        FollowWindowGeometry(preferences);

        var appearance = Ui.Heading("颜色和文字", 16);
        appearance.Margin = new Thickness(0, 13, 0, 1);
        fields.Children.Add(appearance);
        accent = ColorField("主色", theme.Accent);
        surface = ColorField("底色", theme.Surface);
        var typefaces = TwoColumns();
        typefaces.Margin = new Thickness(0, 7, 0, 0);
        string yueHei = Ui.HasYueHei ? "悦黑体" : "悦黑体（本机未安装）";
        font = AddFont(typefaces, "正文字体", FontIndex(theme.Font), 0, "微软雅黑", "宋体", yueHei);
        titleFont = AddFont(typefaces, "标题字体", TitleIndex(theme.TitleFont), 2, "微软雅黑", "宋体", yueHei);
        fields.Children.Add(typefaces);
        size = Scale("正文字号", theme.Size, 13, 22);
        radius = Scale("圆角", theme.Radius, 0, 24);
        padding = Scale("行间留白", theme.Padding, 2, 14);

        startup = new CheckBox
        {
            Content = "登录 Windows 后自动显示便签",
            IsChecked = startWithWindows,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        startup.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        general.Children.Add(startup);

        workingDirectory = AddWorkingDirectory(data, currentDirectory, defaultDirectory);
        var dataActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        dataActions.Children.Add(Ui.Button("打开数据文件夹", openFolder));
        dataActions.Children.Add(Ui.Button("打开任务文件", openTasks));
        data.Children.Add(dataActions);
        about.Children.Add(Ui.Heading("HanLin-ToDo", 19));
        string? version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        about.Children.Add(Ui.Text($"版本 {version}", 13, "MutedBrush"));
        about.Children.Add(new TextBlock
        {
            Text = "Ctrl+Alt+T   新建待办\nCtrl+F   搜索待办\nCtrl+,   打开设置", Margin = new Thickness(0, 12, 0, 0)
        });

        message.TextWrapping = TextWrapping.Wrap;
        message.Visibility = Visibility.Collapsed;
        message.Margin = new Thickness(0, 0, 12, 0);
        message.VerticalAlignment = VerticalAlignment.Center;
        var buttons = new DockPanel { Margin = new Thickness(20, 12, 20, 14) };
        var saveButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", Close);
        cancel.IsCancel = true;
        cancel.MinWidth = 84;
        cancel.Height = 36;
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.SetResourceReference(Control.BorderBrushProperty, "EdgeBrush");
        saveButtons.Children.Add(cancel);
        var apply = Ui.Button("保存", () =>
        {
            if (!TryDimension(width.Text, out int newWidth) || !TryDimension(height.Text, out int newHeight))
            {
                ShowError("请输入有效的宽度和高度。");
                return;
            }
            var updated = new NoteTheme
            {
                Accent = accent.Text.Trim(),
                Surface = surface.Text.Trim(),
                Font = font.SelectedIndex switch
                {
                    1 => NoteFonts.SimSun,
                    2 => NoteFonts.YueHei,
                    _ => NoteFonts.YaHei
                },
                TitleFont = titleFont.SelectedIndex switch
                {
                    1 => NoteTitleFont.SimSun,
                    2 => NoteTitleFont.YueHei,
                    _ => NoteTitleFont.YaHei
                },
                Size = (int)size.Value,
                Radius = (int)radius.Value,
                Padding = (int)padding.Value,
                Width = newWidth,
                Height = newHeight,
                Scale = (int)zoom.Value
            };
            string? error = !NoteTheme.IsColor(updated.Accent) || !NoteTheme.IsColor(updated.Surface)
                ? "请输入 # 加六位颜色值，也可以点击色块选择。"
                : save(updated.Normalize(), startup.IsChecked == true, workingDirectory.Text);
            if (error is null)
                Close();
            else
                ShowError(error);
        }, primary: true);
        apply.IsDefault = true;
        apply.MinWidth = 84;
        apply.Height = 36;
        saveButtons.Children.Add(apply);
        DockPanel.SetDock(saveButtons, Dock.Right);
        buttons.Children.Add(saveButtons);
        buttons.Children.Add(message);
        DockPanel.SetDock(buttons, Dock.Bottom);
        frame.Children.Add(buttons);
        frame.Children.Add(tabs);
        Loaded += (_, _) => (tabs.SelectedItem as TabItem)?.Focus();
    }

    private Border CreateHeader()
    {
        var caption = new DockPanel { Height = 45, Margin = new Thickness(18, 0, 10, 0) };
        var close = Ui.Button("关闭", Close, "close");
        close.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);
        var icon = new Image { Source = Ui.AppIcon(32), Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0) };
        caption.Children.Add(icon);
        var title = Ui.Heading("设置", 18);
        title.VerticalAlignment = VerticalAlignment.Center;
        caption.Children.Add(title);
        var header = new Border { Child = caption, CornerRadius = new(9, 9, 0, 0), BorderThickness = new(0, 0, 0, 1) };
        header.SetResourceReference(Border.BackgroundProperty, "HeaderBrush");
        header.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                args.Handled = true;
                DragMove();
            }
        };
        return header;
    }

    private static StackPanel SectionContent() => new() { Margin = new Thickness(0, 16, 4, 8) };

    private static void AddSection(TabControl tabs, string name, StackPanel fields)
    {
        var item = new TabItem
        {
            Header = name,
            Content = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        item.SetResourceReference(StyleProperty, "SettingsTab");
        tabs.Items.Add(item);
    }

    private TextBox AddWorkingDirectory(StackPanel data, string currentDirectory, string? defaultDirectory)
    {
        data.Children.Add(Ui.Text("数据文件夹", 13, "MutedBrush"));
        var input = Ui.Input("数据文件夹", currentDirectory);
        input.IsReadOnly = defaultDirectory is null;
        input.ToolTip = "存放 todo.md 的文件夹";
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (defaultDirectory is not null)
        {
            var reset = Ui.Button("默认", () => input.Text = defaultDirectory);
            reset.ToolTip = "使用 EXE 旁的 data 文件夹";
            DockPanel.SetDock(reset, Dock.Right);
            row.Children.Add(reset);
            var choose = Ui.Button("选择…", () =>
            {
                var dialog = new OpenFolderDialog { Title = "选择工作目录" };
                if (Directory.Exists(input.Text.Trim()))
                    dialog.InitialDirectory = input.Text.Trim();
                if (dialog.ShowDialog(this) == true)
                    input.Text = dialog.FolderName;
            });
            DockPanel.SetDock(choose, Dock.Right);
            row.Children.Add(choose);
        }
        row.Children.Add(input);
        data.Children.Add(row);
        var help = Ui.Text(defaultDirectory is null ? "由启动参数指定。"
            : "已有 todo.md 时直接读取，否则复制当前任务文件。", 12, "MutedBrush");
        help.TextWrapping = TextWrapping.Wrap;
        help.Margin = new Thickness(0, 4, 0, 0);
        data.Children.Add(help);
        return input;
    }

    public void FollowWindowGeometry(NotePreferences preferences)
    {
        var theme = preferences.Theme;
        bool locked = preferences.Locked;
        if (locked || width.Text == DimensionText(displayedWidth))
            width.Text = DimensionText(theme.Width);
        if (locked || height.Text == DimensionText(displayedHeight))
            height.Text = DimensionText(theme.Height);
        if (locked || (int)zoom.Value == displayedScale)
            zoom.Value = theme.Scale;
        displayedWidth = theme.Width;
        displayedHeight = theme.Height;
        displayedScale = theme.Scale;
        width.IsEnabled = height.IsEnabled = zoom.IsEnabled = !locked;
        sizeLockHint.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid TwoColumns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static FontPicker AddFont(Grid group, string label, int index, int column, params string[] options)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(label, 13, "MutedBrush"));
        var families = new[]
        {
            new FontFamily(NoteFonts.YaHei),
            new FontFamily(NoteFonts.SimSun),
            Ui.Resolve(NoteFonts.YueHei)
        };
        var picker = new FontPicker(options, families, index) { Margin = new Thickness(0, 3, 0, 0) };
        picker.AutomationName(label);
        stack.Children.Add(picker);
        Grid.SetColumn(stack, column);
        group.Children.Add(stack);
        return picker;
    }

    private static TextBox AddDimension(Grid group, string label, int value, int column)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(label, 13, "MutedBrush"));
        var input = Ui.Input(label, DimensionText(value));
        input.MaxLength = 5;
        input.Margin = new Thickness(0, 3, 0, 0);
        stack.Children.Add(input);
        Grid.SetColumn(stack, column);
        group.Children.Add(stack);
        return input;
    }

    private static int FontIndex(string font) => font switch
    {
        NoteFonts.SimSun => 1,
        NoteFonts.YueHei => 2,
        _ => 0
    };

    private static int TitleIndex(NoteTitleFont font) => font switch
    {
        NoteTitleFont.SimSun => 1,
        NoteTitleFont.YueHei => 2,
        _ => 0
    };

    private static string DimensionText(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryDimension(string value, out int dimension) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dimension)
        && dimension is > 0 and <= NoteTheme.MaximumDimension;

    private void ShowError(string text)
    {
        message.Text = text;
        message.Visibility = Visibility.Visible;
        message.BringIntoView();
    }

    private TextBox ColorField(string label, string value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        var input = Ui.Input(label, value);
        input.MaxLength = 7;
        var chip = Ui.Button("选择" + label, () =>
        {
            string current = NoteTheme.IsColor(input.Text) ? input.Text : value;
            Color color = Ui.ColorFrom(current);
            using var picker = new Forms.ColorDialog
            {
                Color = Drawing.Color.FromArgb(color.R, color.G, color.B),
                FullOpen = true
            };
            if (picker.ShowDialog() == Forms.DialogResult.OK)
                input.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        });
        chip.Width = 54;
        chip.Content = new Border { Width = 20, Height = 11, CornerRadius = new CornerRadius(3) };
        chip.ToolTip = "选择" + label;
        void UpdateChip()
        {
            ((Border)chip.Content).Background = Ui.Brush(Ui.ColorFrom(NoteTheme.IsColor(input.Text) ? input.Text : value));
        }
        input.TextChanged += (_, _) => UpdateChip();
        UpdateChip();
        DockPanel.SetDock(chip, Dock.Right);
        row.Children.Add(chip);
        row.Children.Add(input);
        AddField(label, row);
        return input;
    }

    private Slider Scale(string label, int initial, int minimum, int maximum, string unit = "", int step = 1)
    {
        var row = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var title = Ui.Text(label, 13, "InkBrush");
        title.Width = 80;
        DockPanel.SetDock(title, Dock.Left);
        row.Children.Add(title);
        var amount = Ui.Text(initial.ToString(CultureInfo.InvariantCulture) + unit, 13, "MutedBrush");
        amount.Width = 43;
        amount.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(amount, Dock.Right);
        row.Children.Add(amount);
        var slider = new Slider
        {
            Minimum = minimum, Maximum = maximum, Value = initial,
            TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step
        };
        slider.ValueChanged += (_, _) => amount.Text = ((int)slider.Value).ToString(CultureInfo.InvariantCulture) + unit;
        slider.AutomationName(label);
        row.Children.Add(slider);
        fields.Children.Add(row);
        return slider;
    }

    private void AddField(string label, UIElement control)
    {
        var title = Ui.Text(label, 13, "InkBrush");
        title.Margin = new Thickness(0, 6, 0, 0);
        fields.Children.Add(title);
        fields.Children.Add(control);
    }
}
