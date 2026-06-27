using System.IO;
using System.Text;

namespace VidSlice.Services;

/// <summary>Filesystem helpers: name sanitizing, collision-safe paths, free space.</summary>
public static class PathUtils
{
    /// <summary>
    /// Strip characters that are invalid in Windows file names and collapse
    /// whitespace. Returns a safe fallback when the result is empty.
    /// </summary>
    public static string SanitizeFileName(string? name, string fallback = "output")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        // Trim trailing dots/spaces (illegal at the end of a Windows name).
        var cleaned = sb.ToString().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    /// <summary>
    /// If <paramref name="path"/> already exists, returns the same path with a
    /// " (n)" suffix before the extension that does not yet exist.
    /// </summary>
    public static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        // Extremely unlikely; fall back to a timestamp-free unique-ish name.
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    /// <summary>Bytes available on the volume that hosts <paramref name="folder"/>, or -1 if unknown.</summary>
    public static long GetAvailableFreeBytes(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return -1;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }
}
