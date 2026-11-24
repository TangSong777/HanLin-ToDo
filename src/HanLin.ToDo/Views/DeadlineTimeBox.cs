using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HanLin.ToDo;

internal sealed class DeadlineTimeBox : ComboBox
{
    private const string AllDay = "全天";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] Formats = ["H:mm", "HH:mm"];
    private static readonly string[] Choices = [AllDay,
        ..Enumerable.Range(0, 48).Select(step => TimeOnly.MinValue.AddMinutes(step * 30).ToString("HH:mm", Invariant)),
        "23:59"];

    public const string InvalidTime = "请选择全天，或输入 24 小时制时间，例如 18:30。";

    public DeadlineTimeBox(TimeOnly? time = null)
    {
        Style = (Style)Application.Current.FindResource("SoftTimeSelect");
        IsEditable = true;
        IsTextSearchEnabled = false;
        VerticalContentAlignment = VerticalAlignment.Center;
        MinHeight = 30;
        MaxDropDownHeight = 200;
        ItemsSource = Choices;
        ToolTip = "默认按天截止。也可选择或输入时间。";
        Reset(time);
    }

    public void Reset(TimeOnly? time = null) => SetChoice(time?.ToString("HH:mm", Invariant) ?? AllDay);

    protected override void OnDropDownOpened(EventArgs e)
    {
        SetChoice(Text);
        base.OnDropDownOpened(e);
    }

    private void SetChoice(string text)
    {
        SelectedIndex = Array.IndexOf(Choices, text.Trim());
        Text = text;
    }

    public bool TryRead(out TimeOnly? time)
    {
        string text = Text.Trim();
        time = null;
        if (text.Length == 0 || text == AllDay)
            return true;
        if (!TimeOnly.TryParseExact(text, Formats, Invariant, DateTimeStyles.None, out var parsed))
            return false;
        time = parsed;
        return true;
    }
}
