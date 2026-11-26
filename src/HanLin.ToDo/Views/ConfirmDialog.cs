using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HanLin.ToDo;

// 自绘的二次确认框：圆角卡片、暖白表面，Enter 确认、Esc 取消。
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmLabel)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.ConfigureText(this);
        Title = title;

        var heading = Ui.Heading(title, 15);
        var body = Ui.Text(message, 13, "MutedBrush");
        body.TextWrapping = TextWrapping.Wrap;
        body.MaxWidth = 340;
        body.Margin = new Thickness(0, 8, 0, 0);

        var cancel = Ui.Button("取消", () => DialogResult = false);
        cancel.MinWidth = 76;
        var confirm = Ui.Button(confirmLabel, () => DialogResult = true);
        confirm.MinWidth = 76;
        confirm.Margin = new Thickness(8, 0, 0, 0);
        confirm.SetResourceReference(Control.ForegroundProperty, "OverdueBrush");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);

        var stack = new StackPanel();
        stack.Children.Add(heading);
        stack.Children.Add(body);
        stack.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 20, 22, 20),
            Child = stack,
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 6,
                Direction = 270,
                Opacity = 0.22,
                Color = Colors.Black
            }
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush");

        var outer = new Grid { Margin = new Thickness(24) };
        outer.Children.Add(card);
        Content = outer;

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                DialogResult = false;
            }
            else if (args.Key == Key.Enter)
            {
                args.Handled = true;
                DialogResult = true;
            }
        };
        Loaded += (_, _) =>
        {
            Ui.Fade(this, 0, 1, 120);
            cancel.Focus();
        };
    }

    // 返回 true 表示用户确认。owner 不可见时退回屏幕居中。
    public static bool Ask(Window? owner, string title, string message, string confirmLabel = "移除")
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel);
        if (owner is { IsVisible: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
}
