using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HanLin.ToDo;

internal enum NoteTitleFont { YueHei, Body }

internal sealed record NoteTheme
{
    public const int DefaultWidth = 370;
    public const int DefaultHeight = 348;
    public const int DefaultTextSize = 16;
    public const int DefaultRowPadding = 6;
    public const int MinimumScale = 75;
    public const int MaximumScale = 160;
    public const int MaximumDimension = 16384;

    public string Accent { get; init; } = "#E48887";
    public string Surface { get; init; } = "#FFFCF9";
    public string Font { get; init; } = "Microsoft YaHei UI";
    public NoteTitleFont TitleFont { get; init; } = NoteTitleFont.YueHei;
    public int Size { get; init; } = DefaultTextSize;
    public int Radius { get; init; } = 16;
    public int Padding { get; init; } = DefaultRowPadding;
    public int Width { get; init; } = DefaultWidth;
    public int Height { get; init; } = DefaultHeight;
    public int Scale { get; init; } = 100;

    internal int MinimumWidth => (int)Math.Ceiling(236 * Scale / 100.0);
    internal int MinimumHeight => (int)Math.Ceiling(185 * Scale / 100.0);

    public NoteTheme Normalize()
    {
        var normalized = this with
        {
            Accent = IsColor(Accent) ? Accent : "#E48887",
            Surface = IsColor(Surface) ? Surface : "#FFFCF9",
            Font = Font is "SimSun" ? Font : "Microsoft YaHei UI",
            TitleFont = Enum.IsDefined(TitleFont) ? TitleFont : NoteTitleFont.YueHei,
            Size = Math.Clamp(Size, 13, 22),
            Radius = Math.Clamp(Radius, 0, 24),
            Padding = Math.Clamp(Padding, 2, 14),
            Scale = Math.Clamp(Scale, MinimumScale, MaximumScale)
        };
        return normalized with
        {
            Width = Math.Clamp(Width, normalized.MinimumWidth, MaximumDimension),
            Height = Math.Clamp(Height, normalized.MinimumHeight, MaximumDimension)
        };
    }

    public static bool IsColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}

internal sealed record NotePreferences
{
    public int? Left { get; init; }
    public int? Top { get; init; }
    public bool Locked { get; init; }
    public bool ShowCompleted { get; init; }
    public string TaskView { get; init; } = TodoViews.All;
    public TaskSort Sort { get; init; } = TaskSort.Due;
    public NoteTheme Theme { get; init; } = new();
}

internal sealed class PreferencesFile(string path)
{
    public NotePreferences Read(out string? warning)
    {
        warning = null;
        if (!File.Exists(path))
            return new();
        try
        {
            var preferences = JsonSerializer.Deserialize<NotePreferences>(File.ReadAllText(path)) ?? new();
            return preferences with
            {
                Theme = (preferences.Theme ?? new()).Normalize(),
                TaskView = preferences.TaskView == "important" ? TodoViews.Pinned : preferences.TaskView,
                Sort = Enum.IsDefined(preferences.Sort) ? preferences.Sort : TaskSort.Due
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = "读取设置失败。便签以默认外观打开。";
            return new();
        }
    }

    public void Save(NotePreferences settings) =>
        AtomicFile.Write(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
}

internal static class AtomicFile
{
    public static void Write(string path, string text, Action? beforeReplace = null)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            beforeReplace?.Invoke();
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
