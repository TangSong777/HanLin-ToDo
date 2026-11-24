using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HanLin.ToDo;

internal static class Ui
{
    public static void SetTheme(NoteTheme theme)
    {
        Color surface = ColorFrom(theme.Surface);
        Color accent = ColorFrom(theme.Accent);
        Color ink = ContrastInk(surface);
        bool pale = Luminance(surface) > 0.65;
        var resources = Application.Current.Resources;
        resources["SurfaceBrush"] = Brush(surface);
        resources["HeaderBrush"] = new LinearGradientBrush(
            [new(Mix(surface, accent, 0.17), 0), new(surface, 0.33), new(surface, 1)],
            new Point(0, 0), new Point(0.18, 1));
        resources["AccentBrush"] = Brush(accent);
        resources["AccentInkBrush"] = Brush(ContrastInk(accent));
        resources["InkBrush"] = Brush(ink);
        resources["MutedBrush"] = Brush(pale ? ColorFrom("#736460") : ink);
        resources["CheckBrush"] = Brush(pale ? ColorFrom("#8F7C76") : ink);
        resources["OverdueBrush"] = Brush(pale ? ColorFrom("#93444D") : ink);
        resources["TintBrush"] = Brush(Mix(surface, accent, 0.12));
        resources["HoverBrush"] = Brush(Mix(surface, accent, 0.24));
        resources["EdgeBrush"] = Brush(Mix(surface, ink, 0.15));
        resources["LineBrush"] = Brush(Mix(surface, ink, 0.07));
    }

    public static Color ColorFrom(string value) => (Color)ColorConverter.ConvertFromString(value)!;

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static TextBlock Text(string text, double? size = null, string? brush = null)
    {
        var block = new TextBlock { Text = text };
        if (size is double points)
            block.FontSize = points;
        if (brush is not null)
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static Button Button(string label, Action pressed, string? icon = null, bool primary = false)
    {
        var button = new Button { Style = (Style)Application.Current.FindResource("SoftButton") };
        button.SetResourceReference(Control.ForegroundProperty, primary ? "AccentInkBrush" : "InkBrush");
        if (primary)
        {
            button.Tag = "Primary";
            button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        }
        if (icon is null)
            button.Content = label;
        else
        {
            button.Content = Glyph(icon);
            button.ToolTip = label;
            button.Width = 30;
            button.Height = 30;
            button.Padding = new Thickness(0);
        }
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => pressed();
        return button;
    }

    public static FrameworkElement Glyph(string icon)
    {
        string data = icon switch
        {
            "plus" => "M 2,7 H 12 M 7,2 V 12",
            "lock" => "M 4,6 V 4 A 3,3 0 0 1 10,4 V 6 M 2.5,6 H 11.5 V 12.5 H 2.5 Z",
            "unlock" => "M 4,6 V 3.7 A 3,3 0 0 1 9.7,2.6 M 2.5,6 H 11.5 V 12.5 H 2.5 Z",
            "settings" => "M 1,3.5 H 4 M 7,3.5 H 13 M 4,2 H 7 V 5 H 4 Z M 1,10.5 H 7 M 10,10.5 H 13 M 7,9 H 10 V 12 H 7 Z",
            "pencil" => "M 2,9.4 L 9.7,1.7 L 12.3,4.3 L 4.5,12 L 1.4,12.6 Z M 8.1,3.3 L 10.7,5.9",
            "grip" => "M 5,3 h 0.25 M 9,3 h 0.25 M 5,7 h 0.25 M 9,7 h 0.25 M 5,11 h 0.25 M 9,11 h 0.25",
            "resize" => "M 5,11 L 11,5 M 9,11 L 11,9",
            "close" => "M 4,4 L 10,10 M 10,4 L 4,10",
            "search" => "M 9,6 A 3.5,3.5 0 1 1 2,6 A 3.5,3.5 0 1 1 9,6 M 8,9 L 12,13",
            "star" => "M 7,1.3 L 8.7,4.8 L 12.6,5.4 L 9.8,8.2 L 10.5,12.1 L 7,10.2 L 3.5,12.1 L 4.2,8.2 L 1.4,5.4 L 5.3,4.8 Z",
            "chevron" => "M 3,5 L 7,9 L 11,5",
            _ => throw new ArgumentOutOfRangeException(nameof(icon))
        };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 14,
            Height = 14
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        return path;
    }

    public static TextBox Input(string label, string text = "")
    {
        var box = new TextBox { Text = text, Style = (Style)Application.Current.FindResource("SoftInput") };
        AutomationProperties.SetName(box, label);
        return box;
    }

    public static BitmapSource AppIcon() =>
        BitmapFrame.Create(new Uri("pack://application:,,,/Assets/HanLin-ToDo.ico"));

    private static Color Mix(Color baseColor, Color other, double weight)
    {
        byte blend(byte first, byte second) => (byte)Math.Round(first * (1 - weight) + second * weight);
        return Color.FromRgb(blend(baseColor.R, other.R), blend(baseColor.G, other.G), blend(baseColor.B, other.B));
    }

    private static Color ContrastInk(Color color)
    {
        double luminance = Luminance(color);
        return luminance > 0.4 ? ColorFrom("#3C3C3C") : luminance >= 0.18 ? Colors.Black : Colors.White;
    }

    private static double Luminance(Color color)
    {
        static double channel(byte component)
        {
            double value = component / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return channel(color.R) * 0.2126 + channel(color.G) * 0.7152 + channel(color.B) * 0.0722;
    }
}
