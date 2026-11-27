using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HanLin.ToDo;

internal enum TaskRepeat { None, Daily, Weekdays, Weekly, Monthly, Yearly }
internal enum TaskSort { Due = 0, Title = 2, Newest = 3, Manual = 4 }

internal readonly record struct TodoDeadline(DateOnly Date, TimeOnly? Time = null)
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";

    public DateTime At => Date.ToDateTime(Time ?? TimeOnly.MinValue);
    public DateTime SortAt => Date.ToDateTime(Time ?? TimeOnly.MaxValue);
    public bool IsOverdue(DateTime now) => Time is null ? Date < DateOnly.FromDateTime(now) : At <= now;
    public string ToFileText() => Time is null ? Date.ToString(DateFormat, CultureInfo.InvariantCulture)
        : At.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    public static bool TryParse(string value, out TodoDeadline deadline)
    {
        var culture = CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(value, DateFormat, culture, DateTimeStyles.None, out var date))
        {
            deadline = new(date);
            return true;
        }
        if (DateTime.TryParseExact(value, DateTimeFormat, culture, DateTimeStyles.None, out var exact))
        {
            deadline = new(DateOnly.FromDateTime(exact), TimeOnly.FromDateTime(exact));
            return true;
        }
        deadline = default;
        return false;
    }
}

internal sealed record TodoStep(string Title, bool Done);
// Older Markdown files store list definitions and their notes in the workspace comment.
internal sealed record TodoList(string Id, string Name, string? Group = null, string? Memo = null);
internal sealed record DeletedTodo(string Id, string Title, string Line, string? BatchId = null);

internal sealed record TodoDetails
{
    public string? Id { get; init; }
    public string? ListId { get; init; }
    public bool Pinned { get; init; }
    [JsonPropertyName("Important")]
    public bool LegacyImportant { get; init; }
    public DateOnly? MyDay { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Reminder { get; init; }
    public bool ReminderSent { get; init; }
    public TaskRepeat Repeat { get; init; }
    public int RepeatInterval { get; init; }
    public int? RepeatDay { get; init; }
    public string? NextId { get; init; }
    public string? Memo { get; init; }
    public string[]? Tags { get; init; }
    public TodoStep[]? Steps { get; init; }
    public string[]? Attachments { get; init; }
    // 文件附件保存的是工作目录内的副本；旧的 Attachments 只读兼容，不改结构以免旧版读不回任务。
    public TodoFile[]? Files { get; init; }
}

internal sealed record TodoItem(int Offset, string Line, int Mark, int Body, int ContentEnd,
    string Title, TodoDeadline? Due, bool Done, TodoDetails Details);
internal sealed record TodoDraft(string Title, TodoDeadline? Due, TodoDetails Details);
internal sealed record TodoCatalog
{
    public TodoList[] Lists { get; init; } = [];
    public DeletedTodo[] Deleted { get; init; } = [];
    public string? InboxMemo { get; init; }
}
internal sealed record TodoSnapshot(string Text, IReadOnlyList<TodoItem> Items)
{
    public TodoCatalog Catalog { get; init; } = new();
}

internal static class TodoViews
{
    public const string All = "all";
    public const string Pinned = "pinned";
    public const string Planned = "planned";
    public const string Overdue = "overdue";
    public const string Completed = "completed";
    public const string Trash = "trash";

    public static string Normalize(string? view) => view switch
    {
        "important" => Pinned,
        Pinned or Planned or Overdue or Completed or Trash => view,
        _ => All
    };

    public static string Label(string view) => view switch
    {
        Pinned => "置顶",
        Planned => "有日期或提醒",
        Overdue => "逾期",
        Completed => "已完成",
        Trash => "回收站",
        _ => "全部"
    };

    public static bool Includes(TodoItem item, string view, DateTime now) => view switch
    {
        Pinned => item.Details.Pinned,
        Planned => item.Due is not null || item.Details.Reminder is not null,
        Overdue => !item.Done && item.Due is { } due && due.IsOverdue(now),
        Completed => item.Done,
        Trash => false,
        _ => true
    };

    public static bool Matches(TodoItem item, string query)
    {
        string normalized = query.TrimStart('#');
        if (normalized.Length == 0)
            return true;
        bool Has(string? text) => text?.Contains(normalized, StringComparison.CurrentCultureIgnoreCase) == true;
        return Has(item.Title) || Has(item.Details.Memo) || item.Details.Tags?.Any(Has) == true
            || item.Details.Steps?.Any(step => Has(step.Title)) == true || item.Details.Attachments?.Any(Has) == true
            || item.Details.Files?.Any(file => Has(file.Name)) == true;
    }

    public static IEnumerable<TodoItem> Sort(IEnumerable<TodoItem> items, TaskSort order)
    {
        var pinnedFirst = items.OrderByDescending(item => item.Details.Pinned);
        return order switch
        {
            TaskSort.Title => pinnedFirst.ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            TaskSort.Newest => pinnedFirst.ThenByDescending(item => item.Details.Created).ThenByDescending(item => item.Offset),
            TaskSort.Manual => pinnedFirst,
            _ => pinnedFirst.ThenBy(item => item.Due?.SortAt ?? item.Details.Reminder ?? DateTime.MaxValue)
        };
    }
}

internal sealed partial class TaskFile(string path)
{
    private const string DeadlineSeparator = " 📅 ";
    private const string ItemPrefix = " <!-- hanlin-task:";
    private const string CatalogPrefix = "<!-- hanlin-workspace:";
    private const string CommentEnd = " -->";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    public string Path { get; } = path;

    private (DateTime Stamp, long Length)? lastSeen;
    private int quietChecks;

    /// <summary>
    /// 定时刷新（每 2 秒）先比文件时间戳与大小：没变就不必读整份 todo.md、更不必重新解析。
    /// 低精度时间戳或网络盘每 30 次（约 60 秒）强制读一次兜底。
    /// </summary>
    public bool HasChanged()
    {
        if (++quietChecks > 30)
        {
            quietChecks = 0;
            return true;
        }
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists)
                return lastSeen is not null;
            var stamp = (info.LastWriteTimeUtc, info.Length);
            if (lastSeen == stamp)
                return false;
            lastSeen = stamp;
            quietChecks = 0;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public TodoSnapshot Read(TodoSnapshot? known = null)
    {
        string text = ReadText();
        return known?.Text == text ? known : Parse(text);
    }

    public TodoSnapshot Add(TodoDraft draft)
    {
        var snapshot = Read();
        var details = Normalize(draft.Details, draft.Due) with
        {
            Id = NewId(), Created = DateTime.Now, ReminderSent = false, NextId = null
        };
        return Write(snapshot.Text, AppendLine(snapshot.Text, NewLine(draft.Title, draft.Due, details)));
    }

    public TodoSnapshot Edit(TodoItem expected, TodoDraft draft)
    {
        var snapshot = Read();
        var item = FindCurrent(snapshot, expected);
        var details = Normalize(draft.Details, draft.Due) with
        {
            Id = item.Details.Id ?? NewId(),
            Created = item.Details.Created,
            ReminderSent = item.Details.Reminder == draft.Details.Reminder && item.Details.ReminderSent,
            NextId = item.Details.NextId,
            RepeatDay = draft.Due is null || draft.Details.Repeat == TaskRepeat.None ? null
                : draft.Details.Repeat == item.Details.Repeat && draft.Due == item.Due ? item.Details.RepeatDay : draft.Due.Value.Date.Day
        };
        return Write(snapshot.Text, ReplaceItem(snapshot.Text, item,
            item.Line[..item.Body] + FormatTitle(draft.Title, draft.Due) + SerializeDetails(details)));
    }

    public TodoSnapshot UpdateDetails(TodoItem expected, Func<TodoDetails, TodoDetails> update)
    {
        var snapshot = Read();
        var item = FindCurrent(snapshot, expected);
        var details = Normalize(update(item.Details), item.Due) with { Id = item.Details.Id ?? NewId() };
        return Write(snapshot.Text, ReplaceItem(snapshot.Text, item, item.Line[..item.ContentEnd] + SerializeDetails(details)));
    }

    public TodoSnapshot SetDone(TodoItem expected, bool done)
    {
        var snapshot = Read();
        var item = FindCurrent(snapshot, expected);
        if (item.Done == done)
            return snapshot;
        var details = item.Details;
        string text = snapshot.Text;
        string? nextLine = null;
        if (done && item.Due is { } due && details.Repeat != TaskRepeat.None
            && !snapshot.Items.Any(next => next.Details.Id is not null && next.Details.Id == details.NextId))
        {
            var now = DateTime.Now;
            var cutoff = due.Time is null ? now.Date.AddTicks(-1) : now;
            if (NextDue(due.At, details.Repeat, details.RepeatDay ?? due.Date.Day, cutoff, details.RepeatInterval) is { } nextDue)
            {
                string nextId = NewId();
                details = details with { Id = details.Id ?? NewId(), NextId = nextId };
                var next = details with
                {
                    Id = nextId, NextId = null, MyDay = null, Created = DateTime.Now,
                    Reminder = ShiftReminder(details.Reminder, nextDue - due.At),
                    ReminderSent = false, RepeatDay = details.RepeatDay ?? due.Date.Day,
                    Steps = details.Steps?.Select(step => step with { Done = false }).ToArray()
                };
                nextLine = NewLine(item.Title, due with { Date = DateOnly.FromDateTime(nextDue) }, next);
            }
        }
        string body = item.Line[..item.ContentEnd];
        string mark = done ? "x" : " ";
        string updated = body[..item.Mark] + mark + body[(item.Mark + 1)..];
        bool hasDetails = details.Id is not null || item.ContentEnd < item.Line.Length;
        text = ReplaceItem(text, item, hasDetails ? updated + SerializeDetails(details) : updated);
        if (nextLine is not null)
            text = AppendLine(text, nextLine);
        return Write(snapshot.Text, text);
    }

    public TodoSnapshot Duplicate(TodoItem expected)
    {
        var snapshot = Read();
        var item = FindCurrent(snapshot, expected);
        var details = item.Details with
        {
            Id = NewId(), Created = DateTime.Now, ReminderSent = false, NextId = null,
            Steps = item.Details.Steps?.Select(step => step with { Done = false }).ToArray()
        };
        return Write(snapshot.Text, AppendLine(snapshot.Text, NewLine(item.Title, item.Due, details)));
    }

    public TodoSnapshot Reorder(IReadOnlyList<TodoItem> order)
    {
        var snapshot = Read();
        var byOffset = snapshot.Items.ToDictionary(item => item.Offset);
        var rows = order.Select(expected => byOffset.TryGetValue(expected.Offset, out var item) && item.Line == expected.Line
            ? item : FindCurrent(snapshot, expected)).ToArray();
        var slots = rows.OrderBy(item => item.Offset).ToArray();
        if (slots.Select(item => item.Offset).Distinct().Count() != slots.Length)
            throw new IOException("任务顺序已变化，请重新拖动。");
        string text = snapshot.Text;
        for (int index = slots.Length - 1; index >= 0; --index)
            if (slots[index].Line != rows[index].Line)
                text = ReplaceItem(text, slots[index], rows[index].Line);
        return Write(snapshot.Text, text);
    }

    public TodoSnapshot Delete(IReadOnlyList<TodoItem> selections, string deletionId)
    {
        var snapshot = Read();
        var items = FindSelections(snapshot, selections);
        if (items.Length == 0)
            return snapshot;
        bool batch = items.Length > 1;
        string text = snapshot.Text;
        foreach (var item in items)
            text = RemoveLine(text, item.Offset, item.Line.Length);
        var removed = items.Reverse().Select(item => new DeletedTodo(batch ? NewId() : deletionId,
            item.Title, item.Line, batch ? deletionId : null));
        var catalog = snapshot.Catalog with
        {
            Deleted = [..removed, ..snapshot.Catalog.Deleted]
        };
        return Write(snapshot.Text, PutCatalog(text, catalog));
    }

    public TodoSnapshot Restore(string deletionId)
    {
        var snapshot = Read();
        bool IsSelected(DeletedTodo entry) => entry.Id == deletionId || entry.BatchId == deletionId;
        var entries = snapshot.Catalog.Deleted.Where(IsSelected).ToArray();
        if (entries.Length == 0)
            throw new IOException("任务已从回收站移除。");
        string text = snapshot.Text;
        var usedIds = snapshot.Items.Select(item => item.Details.Id).OfType<string>().ToHashSet();
        foreach (var entry in entries)
        {
            string line = entry.Line;
            if (ParseItem(MarkdownTask().Match(line)) is { } item)
            {
                var details = Normalize(item.Details, item.Due);
                if (details.Id is { } id && !usedIds.Add(id))
                    details = details with { Id = NewId(), NextId = null };
                if (details != item.Details)
                    line = item.Line[..item.ContentEnd] + SerializeDetails(details);
            }
            text = AppendLine(text, line);
        }
        var catalog = snapshot.Catalog with { Deleted = snapshot.Catalog.Deleted.Where(entry => !IsSelected(entry)).ToArray() };
        return Write(snapshot.Text, PutCatalog(text, catalog));
    }

    public (TodoSnapshot Snapshot, IReadOnlyList<string> Titles) ClaimReminders(DateTime now)
    {
        var snapshot = Read();
        var ready = snapshot.Items.Where(item => IsReminderDue(item, now)).OrderByDescending(item => item.Offset).ToArray();
        if (ready.Length == 0)
            return (snapshot, []);
        string text = snapshot.Text;
        foreach (var item in ready)
            text = ReplaceItem(text, item, item.Line[..item.ContentEnd] + SerializeDetails(item.Details with { ReminderSent = true }));
        return (Write(snapshot.Text, text), ready.Select(item => item.Title).Reverse().ToArray());
    }

    public static bool IsReminderDue(TodoItem item, DateTime now) =>
        !item.Done && !item.Details.ReminderSent && item.Details.Reminder is { } reminder && reminder <= now;

    public static IEnumerable<TodoFile> AttachmentLinks(TodoSnapshot snapshot) =>
        snapshot.Items.Concat(snapshot.Catalog.Deleted.Select(entry => ParseItem(MarkdownTask().Match(entry.Line))).OfType<TodoItem>())
            .SelectMany(item => item.Details.Files ?? []);

    public static bool ReferencesAttachment(TodoSnapshot snapshot, string workspace, string path) =>
        AttachmentLinks(snapshot).Any(file => AttachmentStore.IsSameCopy(workspace, file, path));

    public TodoSnapshot RenameAttachment(string workspace, TodoFile file, string name)
    {
        if (name.Length == 0 || name.EndsWith('.') || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("附件名称无效，请输入有效的文件名。");
        string oldPath = AttachmentStore.ManagedCopy(workspace, file)
            ?? throw new IOException("附件副本已丢失或不在工作区内。");
        var snapshot = Read();
        if (!ReferencesAttachment(snapshot, workspace, oldPath))
            throw new IOException("附件引用已发生变化，请重新打开菜单。");
        string nextPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(oldPath)!, name);
        if (string.Equals(oldPath, nextPath, StringComparison.Ordinal))
            return snapshot;

        bool caseOnly = string.Equals(oldPath, nextPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(nextPath) || Directory.Exists(nextPath)))
            throw new IOException("附件文件夹里已有同名文件。");

        string relative = System.IO.Path.GetRelativePath(workspace, nextPath);
        string Rewrite(TodoItem item)
        {
            if (item.Details.Files is not { } files ||
                !files.Any(link => AttachmentStore.IsSameCopy(workspace, link, oldPath)))
                return item.Line;
            var updated = files.Select(link => AttachmentStore.IsSameCopy(workspace, link, oldPath)
                ? link with { Name = name, RelativePath = relative } : link).ToArray();
            return item.Line[..item.ContentEnd] + SerializeDetails(item.Details with { Files = updated });
        }

        string text = snapshot.Text;
        foreach (var item in snapshot.Items.OrderByDescending(item => item.Offset))
            text = ReplaceItem(text, item, Rewrite(item));
        var deleted = snapshot.Catalog.Deleted.Select(entry =>
            ParseItem(MarkdownTask().Match(entry.Line)) is { } item ? entry with { Line = Rewrite(item) } : entry).ToArray();
        if (!deleted.SequenceEqual(snapshot.Catalog.Deleted))
            text = PutCatalog(text, snapshot.Catalog with { Deleted = deleted });

        // Publish the new path before switching the Markdown references so both names resolve during the write.
        if (caseOnly)
            File.Move(oldPath, nextPath);
        else
            File.Copy(oldPath, nextPath, overwrite: false);
        try
        {
            var updated = Write(snapshot.Text, text);
            if (!caseOnly)
                AttachmentStore.DeleteCopy(workspace, file);
            return updated;
        }
        catch
        {
            if (!caseOnly)
            {
                AttachmentStore.DeleteCopy(workspace, file with { RelativePath = relative });
            }
            throw;
        }
    }

    public void EnsureExists()
    {
        using var stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private static TodoDetails Normalize(TodoDetails input, TodoDeadline? due)
    {
        var repeat = due is not null && Enum.IsDefined(input.Repeat) ? input.Repeat : TaskRepeat.None;
        return input with
        {
            Repeat = repeat,
            RepeatDay = repeat == TaskRepeat.None ? null : input.RepeatDay,
            RepeatInterval = repeat is TaskRepeat.None or TaskRepeat.Weekdays || input.RepeatInterval <= 1
                ? 0 : Math.Clamp(input.RepeatInterval, 2, 365)
        };
    }

    private static TodoItem FindCurrent(TodoSnapshot source, TodoItem expected)
    {
        var matches = source.Items.Where(item => item.Line == expected.Line).ToArray();
        return matches.FirstOrDefault(item => item.Offset == expected.Offset) ??
            (matches.Length == 1 ? matches[0] : throw new IOException("任务在文件中已被修改。请刷新便签后重试。"));
    }

    private static TodoItem[] FindSelections(TodoSnapshot source, IReadOnlyList<TodoItem> items) =>
        items.Select(item => FindCurrent(source, item)).DistinctBy(item => item.Offset).OrderByDescending(item => item.Offset).ToArray();

    private TodoSnapshot Write(string before, string after)
    {
        if (before != after)
            AtomicFile.Write(Path, after, () =>
            {
                if (ReadText() != before)
                    throw new IOException("任务文件刚刚被外部编辑，请重试。");
            });
        return Parse(after);
    }

    private string ReadText()
    {
        try
        {
            return File.ReadAllText(Path, Utf8);
        }
        catch (FileNotFoundException)
        {
            return "";
        }
    }

    private static TodoSnapshot Parse(string text)
    {
        var items = new List<TodoItem>();
        foreach (Match match in MarkdownTask().Matches(text))
            if (ParseItem(match) is { } item)
                items.Add(item);
        var (_, catalog) = ReadCatalog(text);
        return new(text, items) { Catalog = catalog };
    }

    private static TodoItem? ParseItem(Match match)
    {
        if (!match.Success)
            return null;
        Group mark = match.Groups["mark"];
        Group body = match.Groups["body"];
        var details = new TodoDetails();
        string content = body.Value;
        int suffix = content.LastIndexOf(ItemPrefix, StringComparison.Ordinal);
        if (suffix >= 0 && content.TrimEnd().EndsWith(CommentEnd, StringComparison.Ordinal)
            && Deserialize<TodoDetails>(content.TrimEnd()[(suffix + ItemPrefix.Length)..^CommentEnd.Length]) is { } metadata)
        {
            details = metadata.LegacyImportant ? metadata with { Pinned = true, LegacyImportant = false } : metadata;
            content = content[..suffix];
        }
        string title = content.Trim();
        TodoDeadline? due = null;
        int delimiter = title.LastIndexOf(DeadlineSeparator, StringComparison.Ordinal);
        if (delimiter > 0 && TodoDeadline.TryParse(title[(delimiter + DeadlineSeparator.Length)..], out var deadline))
        {
            due = deadline;
            title = title[..delimiter].TrimEnd();
        }
        return title.Length == 0 ? null : new(match.Index, match.Value, mark.Index - match.Index, body.Index - match.Index,
            body.Index - match.Index + content.Length, title, due, mark.Value[0] != ' ', details);
    }

    private static (Match? Entry, TodoCatalog Catalog) ReadCatalog(string text)
    {
        foreach (Match match in WorkspaceComment().Matches(text))
            if (Deserialize<TodoCatalog>(match.Groups["json"].Value) is { } data)
                return (match, data with { Lists = data.Lists ?? [], Deleted = data.Deleted ?? [] });
        return (null, new());
    }

    private static string PutCatalog(string text, TodoCatalog catalog)
    {
        var (entry, _) = ReadCatalog(text);
        string line = CatalogPrefix + JsonSerializer.Serialize(catalog, Json) + CommentEnd;
        return entry is null ? AppendLine(text, line) : text[..entry.Index] + line + text[(entry.Index + entry.Length)..];
    }

    private static T? Deserialize<T>(string value) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(value, Json); }
        catch (JsonException) { return null; }
    }

    private static string ReplaceItem(string text, TodoItem item, string line) =>
        text[..item.Offset] + line + text[(item.Offset + item.Line.Length)..];

    private static string RemoveLine(string text, int offset, int length)
    {
        int end = offset + length;
        if (end < text.Length && text[end] == '\r') ++end;
        if (end < text.Length && text[end] == '\n') ++end;
        return text[..offset] + text[end..];
    }

    private static string AppendLine(string text, string line)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
        return text + (text.Length == 0 || text.EndsWith('\n') ? "" : newline) + line + newline;
    }

    private static string NewLine(string title, TodoDeadline? due, TodoDetails details) =>
        "- [ ] " + FormatTitle(title, due) + SerializeDetails(details);

    private static string FormatTitle(string title, TodoDeadline? due) =>
        due is { } date ? title + DeadlineSeparator + date.ToFileText() : title;

    private static string SerializeDetails(TodoDetails details) => ItemPrefix + JsonSerializer.Serialize(details, Json) + CommentEnd;
    private static string NewId() => Guid.NewGuid().ToString("N");

    private static DateTime? ShiftReminder(DateTime? at, TimeSpan elapsed)
    {
        try { return at?.Add(elapsed); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    internal static DateTime? NextDue(DateTime due, TaskRepeat repeat, int day, DateTime now, int interval = 1)
    {
        try
        {
            interval = Math.Clamp(interval, 1, 365);
            if (repeat is TaskRepeat.Daily or TaskRepeat.Weekly)
            {
                int days = interval * (repeat == TaskRepeat.Daily ? 1 : 7);
                var periods = Math.Max(1, Math.Floor((now - due).TotalDays / days) + 1);
                return due.AddDays(periods * days);
            }
            DateTime next = due;
            do
            {
                next = repeat switch
                {
                    TaskRepeat.Weekdays => next.AddDays(1),
                    TaskRepeat.Monthly => next.AddMonths(interval),
                    TaskRepeat.Yearly => next.AddYears(interval),
                    _ => throw new ArgumentOutOfRangeException(nameof(repeat))
                };
                if (repeat is TaskRepeat.Monthly or TaskRepeat.Yearly)
                    next = new DateTime(next.Year, next.Month, Math.Clamp(day, 1, DateTime.DaysInMonth(next.Year, next.Month)))
                        .Add(next.TimeOfDay);
            } while (next <= now || (repeat == TaskRepeat.Weekdays && next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
            return next;
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    [GeneratedRegex(@"^[ \t]*[-+*][ \t]+\[(?<mark>[ xX])\][ \t]+(?<body>[^\r\n]*)(?=\r?$)", RegexOptions.Multiline)]
    private static partial Regex MarkdownTask();

    [GeneratedRegex(@"^<!-- hanlin-workspace:(?<json>\{[^\r\n]*\}) -->[ \t]*(?=\r?$)", RegexOptions.Multiline)]
    private static partial Regex WorkspaceComment();
}
