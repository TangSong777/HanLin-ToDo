using System;
using System.Globalization;
using System.IO;

namespace HanLin.ToDo;

// 附件一律复制进工作目录的 Attachments 文件夹，原文件移动或删除后副本仍在。
internal sealed record TodoFile
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string? OriginalPath { get; init; }
    public long Size { get; init; }
    public DateTime Added { get; init; }
}

internal static class AttachmentStore
{
    public const string FolderName = "Attachments";

    public static string Root(string directory) => Path.Combine(directory, FolderName);

    public static string FullPath(string directory, TodoFile file)
    {
        string relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathFullyQualified(relative) ? relative : Path.Combine(directory, relative);
    }

    public static bool Exists(string directory, TodoFile file) => File.Exists(FullPath(directory, file));

    public static TodoFile Import(string directory, string source)
    {
        var info = new FileInfo(source);
        if (!info.Exists)
            throw new FileNotFoundException("找不到文件。", source);
        string folder = Root(directory);
        Directory.CreateDirectory(folder);
        string target = Target(folder, info);
        return new TodoFile
        {
            Name = info.Name,
            RelativePath = Path.GetRelativePath(directory, target),
            OriginalPath = info.FullName,
            Size = info.Length,
            Added = DateTime.Now
        };
    }

    private static string Target(string folder, FileInfo source)
    {
        string first = Path.Combine(folder, source.Name);
        if (!File.Exists(first))
        {
            File.Copy(source.FullName, first, overwrite: false);
            return first;
        }
        // 同名同大小视为同一份副本，直接复用，避免重复拖入时堆文件。
        if (new FileInfo(first).Length == source.Length)
            return first;
        string stem = Path.GetFileNameWithoutExtension(source.Name);
        string extension = Path.GetExtension(source.Name);
        for (int index = 2; index < 1000; index++)
        {
            string candidate = Path.Combine(folder, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                File.Copy(source.FullName, candidate, overwrite: false);
                return candidate;
            }
            if (new FileInfo(candidate).Length == source.Length)
                return candidate;
        }
        string fallback = Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
        File.Copy(source.FullName, fallback, overwrite: false);
        return fallback;
    }

    public static void DeleteCopy(string directory, TodoFile file)
    {
        try
        {
            File.Delete(FullPath(directory, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 副本被占用时保留文件，只解除记录。
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB"
    };
}
