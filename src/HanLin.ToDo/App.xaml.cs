using System;
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
    private PreferencesFile storage = null!;
    private StartupEntry startup = null!;
    private NotePreferences preferences = new();
    private TodoSnapshot current = new("", []);
    private NoteView note = null!;
    private DesktopSurface? desktop;
    private DispatcherTimer? timer;
    private CaptureWindow? capture;
    private SettingsWindow? settingsWindow;
    private Forms.NotifyIcon? tray;
    private Forms.ContextMenuStrip? trayMenu;
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
            tasks = new TaskFile(Path.Combine(directory, "todo.md"));
            storage = new PreferencesFile(Path.Combine(directory, "settings.json"));
            startup = new StartupEntry(alternate is null ? "HanLin-ToDo" : "HanLin-ToDo-" + identity,
                alternate is null ? null : directory);
            preferences = storage.Read(out string? warning);
            Ui.SetTheme(preferences.Theme);
            note = new NoteView();
            note.Apply(preferences);
            note.ShowNotice(warning);
            note.NewRequested += ShowCapture;
            note.SettingsRequested += ShowSettings;
            note.LockRequested += ToggleLock;
            note.CompletedRequested += () => ChangePreferences(preferences with { ShowCompleted = !preferences.ShowCompleted });
            note.DoneChanged += (item, done) => Mutate(() => tasks.SetDone(item, done));
            note.SaveEdit = (item, title, due) => Mutate(() => tasks.Edit(item, title, due));
            desktop = new DesktopSurface(note, preferences, point =>
                ChangePreferences(preferences with { Left = point.X, Top = point.Y }));
            ReloadTasks();
            CreateTray();
            RegisterHotkey();
            RefreshDesktop();
            timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
            {
                ReloadTasks();
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
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("新建待办     Ctrl+Alt+T", null, (_, _) => ShowCapture());
        visibleMenu = (Forms.ToolStripMenuItem)trayMenu.Items.Add("隐藏便签", null, (_, _) => SetNoteVisible(!noteVisible));
        trayMenu.Items.Add("便签设置", null, (_, _) => ShowSettings());
        trayMenu.Items.Add("打开任务文件", null, (_, _) => OpenTaskFile());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出 HanLin-ToDo", null, (_, _) => Shutdown());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "HanLin-ToDo · Ctrl+Alt+T", ContextMenuStrip = trayMenu, Visible = true };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                ShowCapture();
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
            capture = new CaptureWindow(preferences.Theme, (title, due) => Mutate(() => tasks.Add(title, due)));
            capture.Closed += (_, _) => capture = null;
            capture.Show();
        }
        capture.FocusEditor();
    }

    private void ShowSettings()
    {
        if (settingsWindow is null)
        {
            try
            {
                settingsWindow = new SettingsWindow(preferences.Theme, startup.Command is not null, OpenTaskFile, ApplySettings);
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

    private string? ApplySettings(NoteTheme theme, bool startWithWindows)
    {
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

        if (ChangePreferences(preferences with { Theme = theme }))
            return null;
        try
        {
            startup.SetCommand(previous);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return "外观保存失败。开机启动设置已更新。";
        }
        return "保存便签外观失败。";
    }

    private bool ChangePreferences(NotePreferences next)
    {
        try
        {
            storage.Save(next);
            if (next.Theme != preferences.Theme)
                Ui.SetTheme(next.Theme);
            preferences = next;
            note.Apply(preferences);
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
            var next = tasks.Read();
            if (next.Text == current.Text)
                return;
            current = next;
            note.UpdateTasks(current);
            note.ShowNotice(null);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            note.ShowNotice("无法读取 todo.md，请检查文件权限及编码。");
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
            visibleMenu.Text = visible ? "隐藏便签" : "显示便签";
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
