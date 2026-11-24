using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HanLin.ToDo;

internal enum NoteTitleFont { YueHei, Body, YaHei, SimSun }

internal static class NoteFonts
{
    public const string YaHei = "Microsoft YaHei UI";
    public const string SimSun = "SimSun";
    public const string YueHei = "YueHei";

    public static string Normalize(string? value) => value switch
    {
        SimSun => SimSun,
        YueHei => YueHei,
        _ => YaHei
    };

    public static NoteTitleFont TitleFrom(string font) => font switch
    {
        SimSun => NoteTitleFont.SimSun,
        YueHei => NoteTitleFont.YueHei,
        _ => NoteTitleFont.YaHei
    };
}

internal sealed record NoteTheme
{
    public const int DefaultWidth = 236;
    public const int DefaultHeight = 411;
    public const int DefaultTextSize = 14;
    public const int DefaultRowPadding = 6;
    public const int MinimumScale = 75;
    public const int MaximumScale = 160;
    public const int MaximumDimension = 16384;

    public string Accent { get; init; } = "#E48887";
    public string Surface { get; init; } = "#FFFCF9";
    public string Font { get; init; } = NoteFonts.YaHei;
    public NoteTitleFont TitleFont { get; init; } = NoteTitleFont.YueHei;
    public int Size { get; init; } = DefaultTextSize;
    public int Radius { get; init; } = 8;
    public int Padding { get; init; } = DefaultRowPadding;
    public int Width { get; init; } = DefaultWidth;
    public int Height { get; init; } = DefaultHeight;
    public int Scale { get; init; } = 100;

    internal int MinimumWidth => (int)Math.Ceiling(236 * Scale / 100.0);
    internal int MinimumHeight => (int)Math.Ceiling(185 * Scale / 100.0);

    public NoteTheme Normalize()
    {
        string font = NoteFonts.Normalize(Font);
        var normalized = this with
        {
            Accent = IsColor(Accent) ? Accent : "#E48887",
            Surface = IsColor(Surface) ? Surface : "#FFFCF9",
            Font = font,
            TitleFont = TitleFont switch
            {
                NoteTitleFont.Body => NoteFonts.TitleFrom(font),
                NoteTitleFont.YaHei => NoteTitleFont.YaHei,
                NoteTitleFont.SimSun => NoteTitleFont.SimSun,
                _ => NoteTitleFont.YueHei
            },
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

internal sealed record NotePlacement
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; } = NoteTheme.DefaultWidth;
    public int Height { get; init; } = NoteTheme.DefaultHeight;
    public int Scale { get; init; } = 100;

    public static NotePlacement From(NotePreferences preferences) => new()
    {
        Left = preferences.Left ?? 0,
        Top = preferences.Top ?? 0,
        Width = preferences.Theme.Width,
        Height = preferences.Theme.Height,
        Scale = preferences.Theme.Scale
    };
}

internal sealed record NotePreferences
{
    public int? Left { get; init; }
    public int? Top { get; init; }
    public bool Locked { get; init; }
    public bool ShowCompleted { get; init; }
    public string TaskView { get; init; } = TodoViews.All;
    public TaskSort Sort { get; init; } = TaskSort.Due;
    public string? WorkingDirectory { get; init; }
    public bool LegacyTasksHandled { get; init; }
    public NoteTheme Theme { get; init; } = new();
    public Dictionary<string, NotePlacement> Placements { get; init; } = new();

    public NotePreferences PreserveLockedGeometry(NotePreferences next)
    {
        if (!Locked || !next.Locked)
            return next;
        return next with
        {
            Left = Left,
            Top = Top,
            Theme = next.Theme with { Width = Theme.Width, Height = Theme.Height, Scale = Theme.Scale }
        };
    }
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
                Placements = preferences.Placements ?? new(),
                TaskView = TodoViews.Normalize(preferences.TaskView),
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

internal sealed class TaskLocation(string profileDirectory, bool commandLineOverride)
{
    private const string FileName = "todo.md";

    public bool CanChoose { get; } = !commandLineOverride;
    public string DefaultDirectory { get; } = commandLineOverride
        ? profileDirectory : Path.Combine(AppContext.BaseDirectory, "data");
    public string? LegacyFile { get; } = commandLineOverride ? null : Path.Combine(profileDirectory, FileName);

    public string Resolve(string? configured)
    {
        if (!CanChoose || string.IsNullOrWhiteSpace(configured))
            return DefaultDirectory;
        string path = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("请输入完整的工作目录路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static TaskFile FileIn(string directory) => new(Path.Combine(directory, FileName));

    public static TodoSnapshot Prepare(string directory, TaskFile file, string? seed)
    {
        Directory.CreateDirectory(directory);
        if (seed is not null)
            AtomicFile.CopyIfMissing(seed, file.Path);
        return file.Read();
    }
}

internal static class AtomicFile
{
    public static void Write(string path, string text, Action? beforeReplace = null) =>
        Write(path, stream => stream.Write(Encoding.UTF8.GetBytes(text)), overwrite: true, beforeReplace: beforeReplace);

    public static void CopyIfMissing(string source, string destination)
    {
        if (File.Exists(destination))
            return;
        FileStream input;
        try
        {
            input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }
        using (input)
        {
            try
            {
                Write(destination, stream => input.CopyTo(stream), overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // A task file appeared while the copy was prepared.
            }
        }
    }

    private static void Write(string path, Action<FileStream> fill, bool overwrite, Action? beforeReplace = null)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fill(stream);
                stream.Flush(flushToDisk: true);
            }
            beforeReplace?.Invoke();
            File.Move(temp, path, overwrite);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
