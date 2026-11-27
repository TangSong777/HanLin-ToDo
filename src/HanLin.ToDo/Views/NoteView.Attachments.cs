using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HanLin.ToDo;

internal sealed partial class NoteView
{
    private const int AttachmentLimit = 50;
    private readonly HashSet<string> freshAttachments = [];
    private Border? attachmentHover;
    private DispatcherOperation? attachmentLeave;
    public Func<TodoFile, string, string?>? AttachmentRenameRequested { get; set; }

    private FrameworkElement CreateAttachmentBadge(TodoItem item)
    {
        var files = item.Details.Files ?? [];
        if (files.Length == 0)
            return new Border { Visibility = Visibility.Collapsed };

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement glyph = Ui.Glyph("paperclip");
        glyph.Width = glyph.Height = 11;
        content.Children.Add(glyph);
        if (files.Length > 1)
        {
            TextBlock count = Ui.Text(files.Length.ToString(CultureInfo.InvariantCulture), 10, "MutedBrush");
            count.Margin = new Thickness(2, 0, 0, 0);
            content.Children.Add(count);
        }

        bool missing = files.Any(file => !AttachmentStore.Exists(Workspace, file));
        var badge = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3, 1, 3, 1),
            Margin = new Thickness(1, 0, 0, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = AttachmentTip(files, missing)
        };
        badge.Opacity = missing ? 0.4 : item.Done ? 0.55 : 1;
        AutomationProperties.SetName(badge, files.Length + " 个附件");
        badge.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            if (selecting)
                return;
            OpenAttachments(item, badge);
        };
        badge.ContextMenu = BuildAttachmentMenu(item);
        if (item.Details.Id is { } id && freshAttachments.Remove(id))
            Ui.PopIn(badge);
        return badge;
    }

    private static string AttachmentTip(TodoFile[] files, bool missing)
    {
        string list = string.Join("\n", files.Select(file => file.Name));
        return missing ? list + "\n\n有副本已丢失，右键可以重新定位" : list;
    }

    private void OpenAttachments(TodoItem item, FrameworkElement target)
    {
        var files = item.Details.Files ?? [];
        if (files.Length == 0)
            return;
        if (files.Length == 1)
        {
            string path = AttachmentStore.FullPath(Workspace, files[0]);
            OpenAttachment(path, files[0].Name);
            return;
        }
        var menu = BuildAttachmentMenu(item);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private ContextMenu BuildAttachmentMenu(TodoItem item)
    {
        var menu = CreateMenu();
        menu.MaxWidth = 276;
        var files = item.Details.Files ?? [];
        foreach (var file in files)
            menu.Items.Add(BuildAttachmentEntry(item, file, menu));
        menu.Items.Add(MenuDivider());
        // 三个文件动作收进子菜单，主菜单保持短小。
        var filesMenu = MenuBranch("文件操作", "search");
        filesMenu.Items.Add(Menu("在文件夹中显示", () => RevealAttachments(files), icon: "search"));
        filesMenu.Items.Add(Menu("复制文件路径", () => CopyAttachmentPaths(files), icon: "copy"));
        filesMenu.Items.Add(Menu("重新定位…", () => RelocateAttachment(item), icon: "pencil"));
        menu.Items.Add(filesMenu);
        menu.Items.Add(Menu("添加附件…", () => BrowseAttachments(item), icon: "plus"));
        var remove = Menu("移除全部附件…", () => RemoveAttachments(item), icon: "trash");
        remove.SetResourceReference(Control.ForegroundProperty, "OverdueBrush");
        menu.Items.Add(remove);
        return menu;
    }

    // 附件行右侧自带删除按钮：只移除这一条，且先弹自绘确认框。
    private MenuItem BuildAttachmentEntry(TodoItem item, TodoFile file, ContextMenu menu)
    {
        string path = AttachmentStore.FullPath(Workspace, file);
        bool exists = File.Exists(path);
        string name = file.Name;

        var label = Ui.Text(name, 13);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.ToolTip = name + "\n右键或按 F2 重命名";
        if (!exists)
            label.SetResourceReference(TextBlock.ForegroundProperty, "OverdueBrush");

        void RequestRemoval()
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RemoveAttachment(item, file)));
        }
        void RequestRename()
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (AttachmentRenameRequested is { } rename &&
                    ConfirmDialog.Rename(Window.GetWindow(this), name, value => rename(file, value)))
                    ShowNotice("已更新附件名称。");
            }));
        }
        var close = Ui.Button("移除附件（Delete）", RequestRemoval, "close");
        close.Width = close.Height = close.MinHeight = 28;
        close.Focusable = false;
        close.Cursor = Cursors.Hand;
        close.Tag = "Danger";
        close.Margin = new Thickness(4, 0, 0, 0);
        var closeIcon = (System.Windows.Shapes.Path)close.Content;
        closeIcon.Width = closeIcon.Height = 11;
        closeIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "OverdueBrush");
        AutomationProperties.SetName(close, "移除附件 " + name);

        MenuItem entry = new()
        {
            Header = label,
            Icon = Ui.Glyph("file"),
            Tag = close,
            Style = (Style)Application.Current.FindResource("SoftMenuItem")
        };
        if (!exists && entry.Icon is System.Windows.Shapes.Path missing)
            missing.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "OverdueBrush");
        entry.Click += (_, _) =>
        {
            if (File.Exists(path))
                OpenAttachment(path, name);
            else
                ShowNotice("找不到 " + name);
        };
        entry.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Delete)
            {
                args.Handled = true;
                RequestRemoval();
            }
            else if (args.Key == Key.F2)
            {
                args.Handled = true;
                RequestRename();
            }
        };
        entry.PreviewMouseRightButtonUp += (_, args) =>
        {
            if (close.IsMouseOver)
                return;
            args.Handled = true;
            RequestRename();
        };
        AutomationProperties.SetName(entry, name);
        return entry;
    }

    private void OpenAttachment(string path, string name)
    {
        if (!File.Exists(path))
        {
            ShowNotice("找不到 " + name);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ShowNotice("无法打开这个附件。");
        }
    }

    private void RevealAttachments(TodoFile[] files)
    {
        TodoFile? existing = files.FirstOrDefault(file => AttachmentStore.Exists(Workspace, file));
        if (existing is null || string.IsNullOrEmpty(existing.RelativePath))
        {
            ShowNotice("附件副本都不在了。");
            return;
        }
        string path = AttachmentStore.FullPath(Workspace, existing);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowNotice("无法打开附件文件夹。");
        }
    }

    private void CopyAttachmentPaths(TodoFile[] files)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, files.Select(file => AttachmentStore.FullPath(Workspace, file))));
            ShowNotice("已复制 " + files.Length + " 条路径。");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            ShowNotice("复制路径失败。");
        }
    }

    private void RemoveAttachment(TodoItem item, TodoFile file)
    {
        TodoFile[] files = item.Details.Files ?? [];
        if (!files.Contains(file))
            return;
        bool hasCopy = AttachmentStore.ManagedCopy(Workspace, file) is not null;
        string message = "要从这条待办里移除「" + file.Name + "」吗？";
        if (hasCopy)
            message += "\n\n默认保留附件副本。";
        ConfirmChoice choice = ConfirmDialog.Ask(Window.GetWindow(this), "移除附件",
                message, "移除", hasCopy ? "将附件副本移到回收站" : null);
        if (!choice.Confirmed)
            return;
        if (DetailsRequested is null || DetailsRequested(item, details =>
        {
            var current = details.Files ?? [];
            int index = Array.IndexOf(current, file);
            if (index < 0)
                return details;
            var remaining = current.Where((_, slot) => slot != index).ToArray();
            return details with { Files = remaining.Length > 0 ? remaining : null };
        }) is not null)
            return;
        bool deleted = choice.DeleteFiles && DeleteAttachmentCopy(file);
        ShowNotice(deleted ? "已移除附件「" + file.Name + "」，副本已移到回收站。" : "已移除附件「" + file.Name + "」。");
    }

    private void RemoveAttachments(TodoItem item)
    {
        if (item.Details.Files is not { Length: > 0 } files)
            return;
        ConfirmChoice choice = ConfirmDialog.Ask(Window.GetWindow(this), "移除全部附件",
                "要移除这条待办的全部 " + files.Length + " 个附件吗？\n\n不勾选时，副本仍保留在 Attachments 文件夹中。",
                "全部移除", "同时删除复制过来的副本文件（移到回收站）");
        if (!choice.Confirmed)
            return;
        if (DetailsRequested is null || DetailsRequested(item, details => details with { Files = null }) is not null)
            return;
        int deleted = 0;
        if (choice.DeleteFiles)
        {
            foreach (TodoFile file in files)
            {
                if (DeleteAttachmentCopy(file))
                    deleted++;
            }
        }
        ShowNotice(deleted > 0
            ? "已移除 " + files.Length + " 个附件，其中 " + deleted + " 个副本已移到回收站。"
            : choice.DeleteFiles ? "已移除附件，副本仍在 Attachments 文件夹中。" : "已移除附件。");
    }

    // 勾选删除时把副本移到回收站；副本仍被其他任务引用或已不存在时只解除本任务的记录。
    private bool DeleteAttachmentCopy(TodoFile file)
    {
        string? path = AttachmentStore.ManagedCopy(Workspace, file);
        if (path is null || TaskFile.ReferencesAttachment(snapshot, Workspace, path))
            return false;
        return RecycleBin.Delete(path);
    }

    private void BrowseAttachments(TodoItem item)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要复制到便签的附件",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;
        AddAttachmentPaths(item, dialog.FileNames);
    }

    private void RelocateAttachment(TodoItem item)
    {
        var files = item.Details.Files ?? [];
        TodoFile? missing = files.FirstOrDefault(file => !AttachmentStore.Exists(Workspace, file));
        if (missing is null)
        {
            ShowNotice("附件都在，不需要重新定位。");
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "重新选择 " + missing.Name,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var replacement = AttachmentStore.Import(Workspace, dialog.FileName);
            var merged = files.Select(file => file == missing ? replacement : file).ToArray();
            DetailsRequested?.Invoke(item, details => details with { Files = merged });
            ShowNotice("已重新定位附件。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowNotice("无法复制所选文件。");
        }
    }

    private void EnableAttachmentDrop(Border row, TodoItem item, Border feedback)
    {
        row.AllowDrop = true;
        row.PreviewDragEnter += (_, args) => AttachmentDrag(args, feedback);
        row.PreviewDragOver += (_, args) => AttachmentDrag(args, feedback);
        row.PreviewDragLeave += (_, _) =>
        {
            if (attachmentHover != feedback)
                return;
            CancelAttachmentLeave();
            // A child control can leave before the next child in the same row enters.
            attachmentLeave = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                attachmentLeave = null;
                SetAttachmentHover(null);
            }));
        };
        row.PreviewDrop += (_, args) =>
        {
            SetAttachmentHover(null);
            if (!args.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            args.Effects = DragDropEffects.None;
            if (CanCopyAttachment(args) && args.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                args.Effects = DragDropEffects.Copy;
                AddAttachmentPaths(item, paths);
            }
            args.Handled = true;
        };
    }

    private static bool CanCopyAttachment(DragEventArgs args) =>
        (args.AllowedEffects & DragDropEffects.Copy) != 0 && args.Data.GetDataPresent(DataFormats.FileDrop);

    private void AttachmentDrag(DragEventArgs args, Border feedback)
    {
        bool canCopy = CanCopyAttachment(args);
        args.Effects = canCopy ? DragDropEffects.Copy : DragDropEffects.None;
        SetAttachmentHover(canCopy ? feedback : null);
        args.Handled = true;
    }

    private void SetAttachmentHover(Border? target)
    {
        CancelAttachmentLeave();
        if (attachmentHover == target)
            return;
        if (attachmentHover is { } previous)
            Ui.Fade(previous, previous.Opacity, 0, 110);
        attachmentHover = target;
        if (target is not null)
            Ui.Fade(target, target.Opacity, 1, Ui.FastMotion);
    }

    private void CancelAttachmentLeave()
    {
        attachmentLeave?.Abort();
        attachmentLeave = null;
    }

    private void AddAttachmentPaths(TodoItem item, IEnumerable<string> paths)
    {
        var existing = item.Details.Files ?? [];
        var added = new List<TodoFile>();
        int skipped = 0;
        foreach (string path in paths.Take(AttachmentLimit))
        {
            if (!File.Exists(path))
            {
                skipped++;
                continue;
            }
            try
            {
                var file = AttachmentStore.Import(Workspace, path);
                bool duplicate = existing.Concat(added).Any(old =>
                    string.Equals(old.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase));
                if (duplicate)
                {
                    skipped++;
                    continue;
                }
                added.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                skipped++;
            }
        }
        if (added.Count == 0)
        {
            ShowNotice(skipped > 0 ? "这些附件已经在任务里了。" : "没有可添加的文件。");
            return;
        }
        var merged = existing.Concat(added).ToArray();
        if (item.Details.Id is { } id)
            freshAttachments.Add(id);
        DetailsRequested?.Invoke(item, details => details with { Files = merged });
        ShowNotice("已添加 " + added.Count + " 个附件");
    }
}

