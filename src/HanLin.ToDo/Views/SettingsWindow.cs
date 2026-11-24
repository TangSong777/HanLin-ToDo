using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HanLin.ToDo;

internal sealed class SettingsWindow : Window
{
    private readonly StackPanel fields = new() { Margin = new Thickness(20, 16, 20, 14) };
    private readonly TextBox accent;
    private readonly TextBox surface;
    private readonly ComboBox font;
    private readonly Slider size;
    private readonly Slider radius;
    private readonly Slider padding;
    private readonly Slider width;
    private readonly CheckBox startup;
    private readonly TextBlock message = Ui.Text("", 12, "OverdueBrush");

    public SettingsWindow(NoteTheme theme, bool startWithWindows, Action openTasks,
        Func<NoteTheme, bool, string?> save)
    {
        Title = "HanLin-ToDo · 便签设置";
        Icon = Ui.AppIcon();
        Width = 382;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 14;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(TextElement.ForegroundProperty, "InkBrush");
        Content = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var heading = Ui.Text("便签外观", 20, "InkBrush");
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 7);
        fields.Children.Add(heading);

        accent = ColorField("主色", theme.Accent);
        surface = ColorField("底色", theme.Surface);
        font = new ComboBox { Margin = new Thickness(0, 3, 0, 0), MinHeight = 27 };
        font.Items.Add("微软雅黑");
        font.Items.Add("宋体");
        font.SelectedIndex = theme.Font == "SimSun" ? 1 : 0;
        font.AutomationName("字体");
        AddField("字体", font);
        size = Scale("字号", theme.Size, 13, 22);
        radius = Scale("圆角", theme.Radius, 0, 24);
        padding = Scale("行间留白", theme.Padding, 2, 14);
        width = Scale("宽度", theme.Width, 270, 460);

        startup = new CheckBox
        {
            Content = "登录 Windows 后自动显示便签",
            IsChecked = startWithWindows,
            Margin = new Thickness(0, 13, 0, 9),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        startup.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        fields.Children.Add(startup);

        message.TextWrapping = TextWrapping.Wrap;
        message.Visibility = Visibility.Collapsed;
        message.Margin = new Thickness(0, 0, 0, 7);
        fields.Children.Add(message);

        var buttons = new DockPanel();
        var open = Ui.Button("打开任务文件", openTasks);
        DockPanel.SetDock(open, Dock.Left);
        buttons.Children.Add(open);
        var saveButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", Close);
        cancel.IsCancel = true;
        saveButtons.Children.Add(cancel);
        var apply = Ui.Button("保存", () =>
        {
            var updated = new NoteTheme
            {
                Accent = accent.Text.Trim(),
                Surface = surface.Text.Trim(),
                Font = font.SelectedIndex == 1 ? "SimSun" : "Microsoft YaHei UI",
                Size = (int)size.Value,
                Radius = (int)radius.Value,
                Padding = (int)padding.Value,
                Width = (int)width.Value
            };
            string? error = !NoteTheme.IsColor(updated.Accent) || !NoteTheme.IsColor(updated.Surface)
                ? "请输入 # 加六位颜色值，也可以点击色块选择。"
                : save(updated.Normalize(), startup.IsChecked == true);
            if (error is null)
                Close();
            else
            {
                message.Text = error;
                message.Visibility = Visibility.Visible;
            }
        }, primary: true);
        apply.IsDefault = true;
        saveButtons.Children.Add(apply);
        buttons.Children.Add(saveButtons);
        fields.Children.Add(buttons);
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

    private Slider Scale(string label, int initial, int minimum, int maximum)
    {
        var row = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var title = Ui.Text(label, 13, "InkBrush");
        title.Width = 80;
        DockPanel.SetDock(title, Dock.Left);
        row.Children.Add(title);
        var amount = Ui.Text(initial.ToString(), 13, "MutedBrush");
        amount.Width = 36;
        amount.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(amount, Dock.Right);
        row.Children.Add(amount);
        var slider = new Slider
        {
            Minimum = minimum, Maximum = maximum, Value = initial,
            TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1
        };
        slider.ValueChanged += (_, _) => amount.Text = ((int)slider.Value).ToString();
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
