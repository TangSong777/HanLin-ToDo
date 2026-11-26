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

namespace HanLin.ToDo;

internal sealed partial class NoteView
{
    private const int AttachmentLimit = 50;
    private readonly HashSet<string> freshAttachments = [];
    private Border? attachmentHover;

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
        badge.ContextMenu = BuildAttachmentMenu(item, badge);
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
        var menu = BuildAttachmentMenu(item, target);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private ContextMenu BuildAttachmentMenu(TodoItem item, UIElement target)
    {
        var menu = CreateMenu();
        var files = item.Details.Files ?? [];
        foreach (var file in files)
            menu.Items.Add(BuildAttachmentEntry(item, file, menu));
        menu.Items.Add(MenuDivider());
        var filesMenu = MenuBranch("文件操作", "search");
        filesMenu.Items.Add(Menu("在文件夹中显示", () => RevealAttachments(files), icon: "search"));
        filesMenu.Items.Add(Menu("复制文件路径", () => CopyAttachmentPaths(files), icon: "copy"));
        filesMenu.Items.Add(Menu("重新定位…", () => RelocateAttachment(item), icon: "pencil"));
        menu.Items.Add(filesMenu);
        menu.Items.Add(MenuDivider());
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
        label.MaxWidth = 160;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        if (!exists)
            label.SetResourceReference(TextBlock.ForegroundProperty, "OverdueBrush");

        var closeIcon = (System.Windows.Shapes.Path)Ui.Glyph("close");
        closeIcon.Width = 11;
        closeIcon.Height = 11;
        var close = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Child = closeIcon,
            Cursor = Cursors.Hand,
            ToolTip = "移除这个附件",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        close.MouseEnter += (_, _) =>
        {
            close.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            closeIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "OverdueBrush");
        };
        close.MouseLeave += (_, _) =>
        {
            close.Background = Brushes.Transparent;
            closeIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        };
        close.MouseLeftButtonDown += (_, args) => args.Handled = true;
        close.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => RemoveAttachment(item, file)));
        };
        AutomationProperties.SetName(close, "移除附件 " + name);

        // 负边距吃掉模板右侧的勾选列，让 ✕ 贴住菜单右缘。
        var header = new Grid { MinWidth = 168, Margin = new Thickness(0, 0, -12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(label);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        MenuItem entry = new()
        {
            Header = header,
            Icon = Ui.Glyph("file"),
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
        ConfirmChoice choice = ConfirmDialog.Ask(Window.GetWindow(this), "移除附件",
                "要从这条待办里移除「" + file.Name + "」吗？\n\n不勾选时，副本仍保留在 Attachments 文件夹中。",
                "移除", "同时删除复制过来的副本文件（移到回收站）");
        if (!choice.Confirmed)
            return;
        TodoFile[] remaining = files.Where(other => other != file).ToArray();
        DetailsRequested?.Invoke(item, details => details with { Files = remaining.Length > 0 ? remaining : null });
        bool deleted = choice.DeleteFiles && DeleteAttachmentCopy(item, file);
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
        int deleted = 0;
        if (choice.DeleteFiles)
        {
            foreach (TodoFile file in files)
            {
                if (DeleteAttachmentCopy(item, file))
                    deleted++;
            }
        }
        DetailsRequested?.Invoke(item, details => details with { Files = null });
        ShowNotice(deleted > 0
            ? "已移除 " + files.Length + " 个附件，其中 " + deleted + " 个副本已移到回收站。"
            : choice.DeleteFiles ? "已移除附件，副本仍在 Attachments 文件夹中。" : "已移除附件。");
    }

    // 勾选删除时把副本移到回收站；副本仍被其他任务引用或已不存在时只解除本任务的记录。
    private bool DeleteAttachmentCopy(TodoItem item, TodoFile file)
    {
        string path = AttachmentStore.FullPath(Workspace, file);
        if (!File.Exists(path) || ReferencedByOtherTask(item, file))
            return false;
        return RecycleBin.Delete(path);
    }

    private bool ReferencedByOtherTask(TodoItem item, TodoFile file)
    {
        foreach (TodoItem other in snapshot.Items)
        {
            if (ReferenceEquals(other, item) || item.Details.Id is { } id && other.Details.Id == id)
                continue;
            if ((other.Details.Files ?? []).Any(candidate => string.Equals(candidate.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
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
        row.DragEnter += (_, args) => AttachmentDrag(args, feedback, entering: true);
        row.DragOver += (_, args) => AttachmentDrag(args, feedback, entering: false);
        row.DragLeave += (_, _) => ClearAttachmentHover(feedback, 150);
        row.Drop += (_, args) =>
        {
            ClearAttachmentHover(feedback, 120);
            if (!args.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            if (args.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddAttachmentPaths(item, paths);
            args.Handled = true;
        };
    }

    // The highlight fades in once per drag instead of restarting on every DragOver.
    private void AttachmentDrag(DragEventArgs args, Border feedback, bool entering)
    {
        bool files = args.Data.GetDataPresent(DataFormats.FileDrop);
        args.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        if (files)
        {
            if (attachmentHover != feedback)
            {
                attachmentHover = feedback;
                Ui.Fade(feedback, feedback.Opacity, 0.75, entering ? 110 : 90);
            }
        }
        else if (attachmentHover == feedback)
        {
            ClearAttachmentHover(feedback, 120);
        }
        args.Handled = true;
    }

    private void ClearAttachmentHover(Border feedback, int durationMs)
    {
        if (attachmentHover != feedback)
            return;
        attachmentHover = null;
        Ui.Fade(feedback, feedback.Opacity, 0, durationMs);
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

