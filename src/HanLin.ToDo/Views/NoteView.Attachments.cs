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
            Ui.FadeDecoration(badge, 0, badge.Opacity);
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
        {
            string path = AttachmentStore.FullPath(Workspace, file);
            bool exists = File.Exists(path);
            string name = file.Name;
            // 文件名里的下划线在菜单里是助记符，显示前需要转义。
            MenuItem entry = Menu(name.Replace("_", "__"), exists ? () => OpenAttachment(path, name) : () => ShowNotice("找不到 " + name), icon: "file");
            if (!exists)
                entry.SetResourceReference(Control.ForegroundProperty, "OverdueBrush");
            menu.Items.Add(entry);
        }
        menu.Items.Add(MenuDivider());
        menu.Items.Add(Menu("在文件夹中显示", () => RevealAttachments(files), icon: "search"));
        menu.Items.Add(Menu("复制文件路径", () => CopyAttachmentPaths(files), icon: "copy"));
        menu.Items.Add(Menu("重新定位…", () => RelocateAttachment(item), icon: "pencil"));
        menu.Items.Add(MenuDivider());
        menu.Items.Add(Menu("添加附件…", () => BrowseAttachments(item), icon: "plus"));
        var remove = Menu("移除附件", () => RemoveAttachments(item), icon: "trash");
        remove.SetResourceReference(Control.ForegroundProperty, "OverdueBrush");
        menu.Items.Add(remove);
        return menu;
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

    private void RemoveAttachments(TodoItem item)
    {
        if (item.Details.Files is not { Length: > 0 })
            return;
        DetailsRequested?.Invoke(item, details => details with { Files = null });
        ShowNotice("已移除附件，副本仍保留在 Attachments 文件夹。");
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
        row.DragEnter += (_, args) => AttachmentDrag(args, feedback);
        row.DragOver += (_, args) => AttachmentDrag(args, feedback);
        row.DragLeave += (_, _) => feedback.Opacity = 0;
        row.Drop += (_, args) =>
        {
            feedback.Opacity = 0;
            if (!args.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            if (args.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddAttachmentPaths(item, paths);
            args.Handled = true;
        };
    }

    private static void AttachmentDrag(DragEventArgs args, Border feedback)
    {
        bool files = args.Data.GetDataPresent(DataFormats.FileDrop);
        args.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        feedback.Opacity = files ? 0.75 : 0;
        args.Handled = true;
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

