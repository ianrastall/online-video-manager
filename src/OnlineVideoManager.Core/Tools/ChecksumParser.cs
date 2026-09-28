namespace OnlineVideoManager.Core.Tools;

/// <summary>Reads SHA-256 values out of the checksum files the tool projects publish.</summary>
public static class ChecksumParser
{
    /// <summary>
    /// The expected hash for <paramref name="assetName"/>. Handles <c>sha256sum</c> output
    /// (optionally with <c>*</c> binary markers or paths), multi-file lists, and PowerShell
    /// <c>Get-FileHash</c> output. If the file holds exactly one hash and no line names the
    /// asset, that hash is used.
    /// </summary>
    public static string? Find(string text, string assetName)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var tokens = line.Split([' ', '\t', '*', '/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Contains(assetName, StringComparer.Ordinal) && HashIn(line) is { } named)
            {
                return named;
            }
        }

        var all = lines.Select(HashIn).OfType<string>().ToList();
        return all.Count == 1 ? all[0] : null;
    }

    private static string? HashIn(string line)
    {
        var start = -1;
        for (var i = 0; i <= line.Length; i++)
        {
            var isHex = i < line.Length && char.IsAsciiHexDigit(line[i]);
            if (isHex && start < 0)
            {
                start = i;
            }
            else if (!isHex && start >= 0)
            {
                if (i - start == 64)
                {
                    return line[start..i].ToLowerInvariant();
                }

                start = -1;
            }
        }

        return null;
    }
}
