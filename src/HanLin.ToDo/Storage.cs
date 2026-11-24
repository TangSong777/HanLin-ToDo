using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HanLin.ToDo;

internal sealed record TodoItem(int Offset, string Line, int Mark, int Body, string Title, DateTime? Due, bool Done);

internal sealed record TodoSnapshot(string Text, IReadOnlyList<TodoItem> Items);

internal sealed partial class TaskFile(string path)
{
    private const string DeadlineFormat = "yyyy-MM-dd HH:mm";
    private const string DeadlineSeparator = " 📅 ";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public string Path { get; } = path;

    public TodoSnapshot Read() => Parse(ReadText());

    public TodoSnapshot Add(string title, DateTime? due)
    {
        string text = ReadText();
        string newline = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
        string gap = text.Length == 0 || text.EndsWith('\n') ? "" : newline;
        return Write(text, text + gap + "- [ ] " + FormatBody(title, due) + newline);
    }

    public TodoSnapshot SetDone(TodoItem item, bool done) => Change(item,
        current => current.Line[..current.Mark] + (done ? "x" : " ") + current.Line[(current.Mark + 1)..]);

    public TodoSnapshot Edit(TodoItem item, string title, DateTime? due) =>
        Change(item, current => current.Line[..current.Body] + FormatBody(title, due));

    public void EnsureExists()
    {
        using var stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private TodoSnapshot Change(TodoItem expected, Func<TodoItem, string> update)
    {
        var snapshot = Read();
        var matches = snapshot.Items.Where(item => item.Line == expected.Line).ToArray();
        TodoItem? item = matches.FirstOrDefault(item => item.Offset == expected.Offset);
        if (item is null && matches.Length == 1)
            item = matches[0];
        if (item is null)
            throw new IOException("任务在文件中已被修改。请刷新便签后重试。");

        string text = snapshot.Text;
        return Write(text, text[..item.Offset] + update(item) + text[(item.Offset + item.Line.Length)..]);
    }

    private TodoSnapshot Write(string before, string after)
    {
        AtomicFile.Write(Path, after, () =>
        {
            if (ReadText() != before)
                throw new IOException("任务文件刚刚被外部编辑，请重试。");
        });
        return Parse(after);
    }

    private string ReadText() => File.Exists(Path) ? File.ReadAllText(Path, Utf8) : "";

    private static TodoSnapshot Parse(string text)
    {
        var items = new List<TodoItem>();
        foreach (Match match in MarkdownTask().Matches(text))
        {
            Group mark = match.Groups["mark"];
            Group body = match.Groups["body"];
            string title = body.Value.Trim();
            DateTime? due = null;
            int delimiter = title.LastIndexOf(DeadlineSeparator, StringComparison.Ordinal);
            if (delimiter > 0 && DateTime.TryParseExact(title[(delimiter + DeadlineSeparator.Length)..],
                DeadlineFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline))
            {
                due = deadline;
                title = title[..delimiter].TrimEnd();
            }
            if (title.Length > 0)
                items.Add(new(match.Index, match.Value, mark.Index - match.Index,
                    body.Index - match.Index, title, due, mark.Value[0] != ' '));
        }
        return new(text, items);
    }

    private static string FormatBody(string title, DateTime? due) =>
        due is { } date ? title + DeadlineSeparator + date.ToString(DeadlineFormat, CultureInfo.InvariantCulture) : title;

    [GeneratedRegex(@"^[ \t]*[-+*][ \t]+\[(?<mark>[ xX])\][ \t]+(?<body>[^\r\n]*)(?=\r?$)", RegexOptions.Multiline)]
    private static partial Regex MarkdownTask();
}

internal sealed record NoteTheme
{
    public const int DefaultWidth = 336;
    public const int DefaultHeight = 308;
    public const int MinimumScale = 75;
    public const int MaximumScale = 160;
    public const int MaximumDimension = 16384;

    public string Accent { get; init; } = "#E48887";
    public string Surface { get; init; } = "#FFFCF9";
    public string Font { get; init; } = "Microsoft YaHei UI";
    public int Size { get; init; } = 16;
    public int Radius { get; init; } = 16;
    public int Padding { get; init; } = 8;
    public int Width { get; init; } = DefaultWidth;
    public int Height { get; init; } = DefaultHeight;
    public int Scale { get; init; } = 100;

    internal int MinimumWidth => (int)Math.Ceiling(236 * Scale / 100.0);
    internal int MinimumHeight => (int)Math.Ceiling(150 * Scale / 100.0);

    public NoteTheme Normalize()
    {
        var normalized = this with
        {
            Accent = IsColor(Accent) ? Accent : "#E48887",
            Surface = IsColor(Surface) ? Surface : "#FFFCF9",
            Font = Font is "SimSun" ? Font : "Microsoft YaHei UI",
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
            return preferences with { Theme = (preferences.Theme ?? new()).Normalize() };
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
