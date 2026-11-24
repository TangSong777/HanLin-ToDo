using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HanLin.ToDo;

internal sealed class FontPicker : Border
{
    private readonly TextBlock caption;
    private readonly Popup popup;
    private readonly Grid frame;
    private readonly List<Path> checks = new();
    private readonly string[] labels;
    private readonly FontFamily[] families;
    private int selectedIndex;
    private DateTime closedAt = DateTime.MinValue;

    public FontPicker(string[] labels, FontFamily[] families, int index)
    {
        this.labels = labels;
        this.families = families;
        selectedIndex = Math.Clamp(index, 0, labels.Length - 1);

        CornerRadius = new CornerRadius(5);
        BorderThickness = new Thickness(1);
        MinHeight = 28;
        Focusable = true;
        Cursor = Cursors.Hand;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(BorderBrushProperty, "EdgeBrush");

        caption = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            FontSize = 13
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");

        var arrow = Ui.Glyph("chevron");
        arrow.Width = 12;
        arrow.Height = 12;
        arrow.Margin = new Thickness(0, 0, 8, 0);
        arrow.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(arrow, Dock.Right);

        var row = new DockPanel();
        row.Children.Add(arrow);
        row.Children.Add(caption);
        Child = row;

        var list = new StackPanel();
        for (int i = 0; i < labels.Length; i++)
        {
            var check = new Path
            {
                Data = Geometry.Parse("M 1,5 L 4,8 L 9,2"),
                Width = 10,
                Height = 9,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = i == selectedIndex ? Visibility.Visible : Visibility.Collapsed
            };
            check.SetResourceReference(Shape.StrokeProperty, "OverdueBrush");
            checks.Add(check);

            var text = new TextBlock
            {
                Text = labels[i],
                FontFamily = families[i],
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(check);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var item = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 10, 4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            int choice = i;
            item.MouseEnter += (_, _) => item.SetResourceReference(BackgroundProperty, "HoverBrush");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonUp += (_, _) => Select(choice);
            list.Children.Add(item);
        }

        var shadow = new Border { CornerRadius = new CornerRadius(10) };
        shadow.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        shadow.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.19 };
        var plate = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Child = list
        };
        plate.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        plate.SetResourceReference(BorderBrushProperty, "EdgeBrush");
        frame = new Grid { Margin = new Thickness(6), MinWidth = 120 };
        frame.Children.Add(shadow);
        frame.Children.Add(plate);

        popup = new Popup
        {
            Child = frame,
            Placement = PlacementMode.Bottom,
            PlacementTarget = this,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None,
            Focusable = false
        };
        popup.Closed += (_, _) =>
        {
            closedAt = DateTime.UtcNow;
            Focus();
        };

        MouseLeftButtonUp += (_, _) =>
        {
            Focus();
            Toggle();
        };
        KeyDown += OnKey;
        UpdateCaption();
    }

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= labels.Length || value == selectedIndex)
                return;
            selectedIndex = value;
            UpdateCaption();
            for (int i = 0; i < checks.Count; i++)
                checks[i].Visibility = i == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        BorderBrush = (Brush)FindResource("AccentBrush");
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        SetResourceReference(BorderBrushProperty, "EdgeBrush");
    }

    private void Toggle()
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }
        // A click on the picker while the popup is open closes it first; ignore that same click.
        if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 250)
            return;
        frame.Width = ActualWidth;
        popup.IsOpen = true;
    }

    private void Select(int index)
    {
        SelectedIndex = index;
        popup.IsOpen = false;
    }

    private void UpdateCaption()
    {
        caption.Text = labels[selectedIndex];
        caption.FontFamily = families[selectedIndex];
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (args.Key is Key.Enter or Key.Space)
        {
            Toggle();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && popup.IsOpen)
        {
            popup.IsOpen = false;
            args.Handled = true;
        }
        else if (popup.IsOpen && args.Key is Key.Up or Key.Down)
        {
            int next = selectedIndex + (args.Key == Key.Up ? -1 : 1);
            SelectedIndex = Math.Clamp(next, 0, labels.Length - 1);
            args.Handled = true;
        }
    }
}
