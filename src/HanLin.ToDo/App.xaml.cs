using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HanLin.ToDo;

public partial class App : Application
{
    private const int HotkeyId = 1;
    private const uint CtrlAltNoRepeat = 0x4003;
    private const uint KeyT = 0x54;
    private TaskFile tasks = null!;
    private TaskLocation taskLocation = null!;
    private bool taskDirectoryReady;
    private PreferencesFile storage = null!;
    private StartupEntry startup = null!;
    private NotePreferences preferences = new();
    private TodoSnapshot current = new("", []);
    private NoteView note = null!;
    private DesktopSurface? desktop;
    private DispatcherTimer? timer;
    private CaptureWindow? capture;
    private SettingsWindow? settingsWindow;
    private ListsWindow? listsWindow;
    private Forms.NotifyIcon? tray;
    private TrayMenu? trayMenu;
    private Drawing.Icon? trayIcon;
    private Forms.ToolStripMenuItem? visibleMenu;
    private HwndSource? hotkey;
    private EventWaitHandle? launchSignal;
    private RegisteredWaitHandle? launchWait;
    private Mutex? instance;
    private bool ownsInstance;
    private bool noteVisible = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string? alternate = DataDirectoryArgument(e.Args);
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(alternate ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HanLin-ToDo")));
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..16];
            string exclusiveName = @"Local\HanLinToDo-" + identity;
            launchSignal = new EventWaitHandle(false, EventResetMode.AutoReset, exclusiveName + "-show");
            instance = new Mutex(true, exclusiveName, out ownsInstance);
            if (!ownsInstance)
            {
                launchSignal.Set();
                Shutdown();
                return;
            }

            Directory.CreateDirectory(directory);
            taskLocation = new TaskLocation(directory, commandLineOverride: alternate is not null);
            storage = new PreferencesFile(Path.Combine(directory, "settings.json"));
            startup = new StartupEntry(alternate is null ? "HanLin-ToDo" : "HanLin-ToDo-" + identity,
                alternate is null ? null : directory);
            preferences = storage.Read(out string? warning);
            warning = LoadTaskDirectory(warning);
            Ui.SetTheme(preferences.Theme);
            note = new NoteView();
            note.Apply(preferences);
            note.NewRequested += ShowCapture;
            note.SettingsRequested += ShowSettings;
            note.LockRequested += ToggleLock;
            note.CompletedRequested += () => ChangePreferences(preferences with { ShowCompleted = !preferences.ShowCompleted });
            note.DoneChanged += (item, done) => Mutate(() => tasks.SetDone(item, done));
            note.SaveEdit = (item, draft) => Mutate(() => tasks.Edit(item, draft));
            note.AddTask = draft => Mutate(() => tasks.Add(draft));
            note.ViewRequested += view => ChangePreferences(preferences with { TaskView = view });
            note.SortRequested += sort => ChangePreferences(preferences with { Sort = sort });
            note.ListsRequested += ShowLists;
            note.DetailsRequested += (item, update) => Mutate(() => tasks.UpdateDetails(item, update));
            note.DuplicateRequested += item => Mutate(() => tasks.Duplicate(item));
            note.ReorderRequested += ReorderTasks;
            note.DeleteRequested += item => DeleteTasks([item]);
            note.RestoreRequested += RestoreTask;
            note.BatchMoveRequested += MoveTasks;
            note.BatchDeleteRequested += DeleteTasks;
            note.ExportRequested += ExportTasks;
            desktop = new DesktopSurface(note, preferences, next => ChangePreferences(next));
            note.UpdateTasks(current);
            note.ShowNotice(warning);
            CreateTray();
            RegisterHotkey();
            RefreshDesktop();
            timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
            {
                ReloadTasks();
                DeliverReminders();
                note.RefreshTime();
                RefreshDesktop();
            }, Dispatcher);

            launchWait = ThreadPool.RegisterWaitForSingleObject(launchSignal,
                (_, _) => Dispatcher.BeginInvoke(new Action(() => SetNoteVisible(true))),
                null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show("HanLin-ToDo 启动失败。\n" + ex.Message, "HanLin-ToDo", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private string? LoadTaskDirectory(string? settingsWarning)
    {
        string? warning = settingsWarning;
        string directory;
        try
        {
            directory = taskLocation.Resolve(preferences.WorkingDirectory);
        }
        catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException or NotSupportedException)
        {
            directory = taskLocation.DefaultDirectory;
            preferences = preferences with { WorkingDirectory = null };
            warning = "保存的工作目录无效，已使用默认目录。";
        }

        tasks = TaskLocation.FileIn(directory);
        try
        {
            current = TaskLocation.Prepare(directory, tasks, PendingLegacyFile());
            taskDirectoryReady = true;
        }
        catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException or NotSupportedException)
        {
            return "无法使用工作目录。可以在便签设置中重新选择。";
        }

        if (taskLocation.CanChoose && !preferences.LegacyTasksHandled && settingsWarning is null)
        {
            try
            {
                var updated = preferences with { LegacyTasksHandled = true };
                storage.Save(updated);
                preferences = updated;
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                warning ??= "工作目录已读取。迁移状态无法保存。";
            }
        }
        return warning;
    }

    private string? PendingLegacyFile() => preferences.LegacyTasksHandled ? null : taskLocation.LegacyFile;

    private void RegisterHotkey()
    {
        hotkey = new HwndSource(new HwndSourceParameters("HanLin-ToDo Hotkey", 0, 0)
        {
            WindowStyle = Native.Popup,
            HwndSourceHook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == Native.HotKeyMessage && wParam.ToInt32() == HotkeyId)
                {
                    ShowCapture();
                    handled = true;
                }
                return IntPtr.Zero;
            }
        });
        if (!Native.RegisterHotKey(hotkey.Handle, HotkeyId, CtrlAltNoRepeat, KeyT))
        {
            note.ShowNotice("Ctrl+Alt+T 已被占用，可以从托盘新建待办。");
            tray!.Text = "HanLin-ToDo";
        }
    }

    private void CreateTray()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/HanLin-ToDo.ico")).Stream;
        var size = Forms.SystemInformation.SmallIconSize;
        trayIcon = new Drawing.Icon(stream, size.Width, size.Height);
        trayMenu = new TrayMenu();
        trayMenu.AddAction("新建待办", "plus", ShowCapture, "Ctrl+Alt+T");
        trayMenu.AddAction("管理清单和便笺", "list", () => ShowLists(note.ActiveView));
        visibleMenu = trayMenu.AddAction("隐藏便签", "eye-off", () => SetNoteVisible(!noteVisible));
        trayMenu.AddAction("便签设置", "settings", ShowSettings);
        trayMenu.AddAction("打开任务文件", "file", OpenTaskFile);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.AddAction("退出 HanLin-ToDo", "exit", Shutdown);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "HanLin-ToDo · Ctrl+Alt+T", ContextMenuStrip = trayMenu, Visible = true };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                ShowCapture();
        };
        tray.BalloonTipClicked += (_, _) =>
        {
            SetNoteVisible(true);
            ChangePreferences(preferences with { TaskView = TodoViews.Planned });
        };
    }

    private void ToggleLock()
    {
        desktop?.EndDrag();
        ChangePreferences(preferences with { Locked = !preferences.Locked });
    }

    private void ShowCapture()
    {
        if (capture is null)
        {
            var draft = new TodoDraft("", null, TodoViews.NewDetails(note.ActiveView, DateTime.Now));
            capture = new CaptureWindow(preferences.Theme, draft, current.Catalog, task => Mutate(() => tasks.Add(task)));
            capture.Closed += (_, _) => capture = null;
            capture.Show();
            return;
        }
        capture.FocusEditor();
    }

    private void ShowLists(string view)
    {
        if (listsWindow is null)
        {
            listsWindow = new ListsWindow(preferences.Theme, current.Catalog, view, SaveList, RemoveList);
            listsWindow.Closed += (_, _) => listsWindow = null;
            listsWindow.Show();
        }
        listsWindow.Activate();
    }

    private string? SaveList(string? id, string name, string? group, string? memo)
    {
        string? error = Mutate(() => id == "" ? tasks.SaveListMemo(null, memo) : tasks.SaveList(id, name, group, memo));
        if (error is null && id is null)
        {
            var added = current.Catalog.Lists.LastOrDefault();
            if (added is not null) ChangePreferences(preferences with { TaskView = added.Id });
        }
        return error;
    }

    private string? RemoveList(string id)
    {
        string? error = Mutate(() => tasks.RemoveList(id));
        if (error is null && preferences.TaskView == id)
            ChangePreferences(preferences with { TaskView = TodoViews.Inbox });
        return error;
    }

    private void ReorderTasks(IReadOnlyList<TodoItem> order)
    {
        var previousSort = preferences.Sort;
        if (previousSort != TaskSort.Manual && !ChangePreferences(preferences with { Sort = TaskSort.Manual }))
            return;
        string? error = Mutate(() => tasks.Reorder(order));
        if (error is not null && previousSort != TaskSort.Manual)
        {
            ChangePreferences(preferences with { Sort = previousSort });
            note.ShowNotice(error);
        }
    }

    private void MoveTasks(IReadOnlyList<TodoItem> items, string? destination)
    {
        if (Mutate(() => tasks.MoveToList(items, destination)) is null)
        {
            note.EndSelection();
            note.ShowNotice($"已移动 {items.Count} 项任务。");
        }
    }

    private void DeleteTasks(IReadOnlyList<TodoItem> items)
    {
        string id = Guid.NewGuid().ToString("N");
        if (items.Count > 0 && Mutate(() => tasks.Delete(items, id)) is null)
        {
            note.EndSelection();
            note.ShowUndo(id);
        }
    }

    private void RestoreTask(string id)
    {
        if (Mutate(() => tasks.Restore(id)) is null)
        {
            note.ShowUndo(null);
            note.ShowNotice("任务已恢复。");
        }
    }

    private void ExportTasks(string heading, IReadOnlyList<TodoItem> items)
    {
        var dialog = new SaveFileDialog { DefaultExt = ".md", Filter = "Markdown 文件|*.md", FileName = "HanLin-ToDo.md" };
        try
        {
            if (dialog.ShowDialog() != true)
                return;
            if (string.Equals(Path.GetFullPath(dialog.FileName), tasks.Path, StringComparison.OrdinalIgnoreCase))
            {
                note.ShowNotice("请选择另一个文件名，保留应用正在使用的任务文件。");
                return;
            }
            var markdown = new StringBuilder().Append("# ").AppendLine(heading).AppendLine();
            foreach (var item in items)
            {
                markdown.Append(item.Done ? "- [x] " : "- [ ] ").Append(item.Title);
                if (item.Due is { } due)
                    markdown.Append(" 📅 ").Append(due.ToFileText());
                if (item.Details.Pinned)
                    markdown.Append(" 📌");
                if (item.Details.Tags is { Length: > 0 } tags)
                    markdown.Append(" #").AppendJoin(" #", tags);
                markdown.AppendLine();
                foreach (var step in item.Details.Steps ?? [])
                    markdown.Append(step.Done ? "  - [x] " : "  - [ ] ").AppendLine(step.Title);
                if (item.Details.Memo is { } memo)
                    foreach (string line in memo.ReplaceLineEndings("\n").Split('\n'))
                        markdown.Append("  > ").AppendLine(line);
                foreach (string attachment in item.Details.Attachments ?? [])
                    markdown.Append("  - ").AppendLine(attachment);
            }
            AtomicFile.Write(dialog.FileName, markdown.ToString());
            note.ShowNotice($"已导出 {items.Count} 项任务。");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            note.ShowNotice("无法导出任务，请检查保存位置。");
        }
    }

    private void DeliverReminders()
    {
        var now = DateTime.Now;
        if (tray is null || !current.Items.Any(item => TaskFile.IsReminderDue(item, now)))
            return;
        try
        {
            var (next, titles) = tasks.ClaimReminders(now);
            if (titles.Count == 0)
                return;
            current = next;
            note.UpdateTasks(current);
            var names = string.Join("\r\n", titles.Take(3));
            if (names.Length > 190) names = names[..190] + "…";
            if (titles.Count > 3) names += $"\r\n另有 {titles.Count - 3} 条提醒";
            tray.ShowBalloonTip(7000, "HanLin-ToDo 提醒", names, Forms.ToolTipIcon.Info);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            note.ShowNotice("提醒暂时无法保存，下次会重试。");
        }
    }

    private void ShowSettings()
    {
        if (settingsWindow is null)
        {
            try
            {
                settingsWindow = new SettingsWindow(preferences.Theme, startup.Command is not null,
                    Path.GetDirectoryName(tasks.Path)!, taskLocation.CanChoose ? taskLocation.DefaultDirectory : null,
                    OpenTaskFile, ApplySettings);
                settingsWindow.Closed += (_, _) => settingsWindow = null;
                settingsWindow.Show();
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                note.ShowNotice("无法读取启动设置。");
            }
        }
        settingsWindow?.Activate();
    }

    private string? ApplySettings(NoteTheme theme, bool startWithWindows, string requestedDirectory)
    {
        string directory;
        try
        {
            directory = taskLocation.Resolve(requestedDirectory);
        }
        catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException or NotSupportedException)
        {
            return "请输入有效的工作目录完整路径。";
        }
        var replacement = TaskLocation.FileIn(directory);
        bool switching = !string.Equals(tasks.Path, replacement.Path, StringComparison.OrdinalIgnoreCase);
        if (switching && (note.IsEditingTask || capture is not null || listsWindow is not null))
            return "请先保存或取消正在编辑的任务或清单，再切换工作目录。";

        TodoSnapshot? replacementSnapshot = null;
        if (switching || !taskDirectoryReady || !Directory.Exists(directory))
        {
            string? seed = switching && (taskDirectoryReady || File.Exists(tasks.Path)) ? tasks.Path : PendingLegacyFile();
            try
            {
                replacementSnapshot = TaskLocation.Prepare(directory, replacement, seed);
            }
            catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException or NotSupportedException)
            {
                return "无法使用所选工作目录，请检查文件夹权限和 todo.md 的编码。";
            }
        }

        string? previous;
        try
        {
            previous = startup.Command;
            startup.Configure(startWithWindows);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return "保存开机启动设置失败。";
        }

        var updated = preferences with { Theme = theme };
        if (taskLocation.CanChoose)
            updated = updated with
            {
                WorkingDirectory = string.Equals(directory, taskLocation.DefaultDirectory, StringComparison.OrdinalIgnoreCase)
                    ? null : directory,
                LegacyTasksHandled = true
            };
        if (ChangePreferences(updated))
        {
            if (replacementSnapshot is not null)
            {
                tasks = replacement;
                current = replacementSnapshot;
                taskDirectoryReady = true;
                if (switching)
                {
                    note.ShowUndo(null);
                    note.EndSelection();
                }
                note.UpdateTasks(current);
            }
            return null;
        }
        try
        {
            startup.SetCommand(previous);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return "便签设置保存失败。开机启动设置已更新。";
        }
        return "保存便签设置失败。";
    }

    private bool ChangePreferences(NotePreferences next)
    {
        try
        {
            storage.Save(next);
            if (next.Theme.Accent != preferences.Theme.Accent || next.Theme.Surface != preferences.Theme.Surface
                || next.Theme.Font != preferences.Theme.Font || next.Theme.TitleFont != preferences.Theme.TitleFont)
                Ui.SetTheme(next.Theme);
            preferences = next;
            note.Apply(preferences);
            desktop?.Apply(preferences);
            settingsWindow?.FollowWindowSize(preferences.Theme);
            note.ShowNotice(null);
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            note.ShowNotice("保存便签设置失败。");
            return false;
        }
    }

    private string? Mutate(Func<TodoSnapshot> change)
    {
        try
        {
            current = change();
            note.UpdateTasks(current);
            note.ShowNotice(null);
            return null;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ReloadTasks();
            note.UpdateTasks(current);
            string message = ex is IOException && ex.Message.StartsWith("任务", StringComparison.Ordinal)
                ? ex.Message : "保存任务失败，请检查 todo.md 是否可写。";
            note.ShowNotice(message);
            return message;
        }
    }

    private void ReloadTasks()
    {
        try
        {
            var next = tasks.Read(current);
            if (ReferenceEquals(next, current))
                return;
            current = next;
            note.UpdateTasks(current);
            note.ShowNotice(null);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            note.ShowNotice("无法读取 todo.md，请检查工作目录、文件权限及编码。");
        }
    }

    private void OpenTaskFile()
    {
        try
        {
            tasks.EnsureExists();
            Process.Start(new ProcessStartInfo(tasks.Path) { UseShellExecute = true });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            note.ShowNotice("无法打开 todo.md，请检查 Markdown 文件的默认打开方式。");
        }
    }

    private void SetNoteVisible(bool visible)
    {
        noteVisible = visible;
        if (visibleMenu is not null)
        {
            visibleMenu.Tag = visible ? "eye-off" : "eye";
            visibleMenu.Text = visible ? "隐藏便签" : "显示便签";
        }
        RefreshDesktop();
    }

    private void RefreshDesktop()
    {
        try
        {
            desktop?.Refresh(noteVisible);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            Debug.WriteLine(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        timer?.Stop();
        launchWait?.Unregister(null);
        if (hotkey is { IsDisposed: false })
        {
            Native.UnregisterHotKey(hotkey.Handle, HotkeyId);
            hotkey.Dispose();
        }
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        trayMenu?.Dispose();
        trayIcon?.Dispose();
        desktop?.Dispose();
        if (ownsInstance)
            instance?.ReleaseMutex();
        instance?.Dispose();
        launchSignal?.Dispose();
        base.OnExit(e);
    }

    private static string? DataDirectoryArgument(string[] args)
    {
        int index = Array.IndexOf(args, "--data-dir");
        if (index < 0)
            return null;
        if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException("--data-dir 需要一个文件夹路径。");
        return args[index + 1];
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or Win32Exception or SecurityException or DecoderFallbackException;
}

internal sealed class StartupEntry(string name, string? customData)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Command
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(name) as string;
        }
    }

    public void Configure(bool enabled)
    {
        string? command = enabled ? $"\"{Environment.ProcessPath}\" --startup" : null;
        if (command is not null && customData is not null)
            command += $" --data-dir \"{customData}\"";
        if (command != Command)
            SetCommand(command);
    }

    public void SetCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (command is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, command, RegistryValueKind.String);
    }
}
