using System.IO.Compression;

namespace OnlineVideoManager.Core.Tools;

internal static class ZipExtraction
{
    /// <summary>Extract the matching entries into <paramref name="outputDirectory"/>, flattened to
    /// their file names. Returns the names written.</summary>
    public static List<string> Extract(string zipPath, string outputDirectory, Func<string, bool> want)
    {
        var names = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            // Directory entries have an empty Name. Only the file name is used for the output path,
            // so entries with ".." segments cannot escape the output folder.
            if (entry.Name.Length == 0 || !want(entry.FullName))
            {
                continue;
            }

            entry.ExtractToFile(Path.Combine(outputDirectory, entry.Name), overwrite: true);
            names.Add(entry.Name);
        }

        return names;
    }

    /// <summary>"ffmpeg-N-1-win64/bin/ffmpeg.exe" → "bin".</summary>
    public static string? ParentFolderName(string entryPath)
    {
        var parts = entryPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : null;
    }
}
