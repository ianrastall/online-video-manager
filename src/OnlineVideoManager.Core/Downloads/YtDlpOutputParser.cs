using System.Globalization;

namespace OnlineVideoManager.Core.Downloads;

/// <summary>Turns lines of yt-dlp output into <see cref="JobUpdate"/>s. The markers match the
/// templates passed by <see cref="YtDlpArguments"/>.</summary>
public static class YtDlpOutputParser
{
    public const string ProgressMarker = "@@P ";
    public const string TitleMarker = "@@T ";
    public const string FileMarker = "@@F ";

    private static readonly HashSet<string> Stages =
    [
        "Merger", "ExtractAudio", "EmbedThumbnail", "Metadata", "EmbedSubtitle",
        "FixupM3u8", "FixupM4a", "FixupDuplicateMoov", "FixupTimestamp", "FixupStretched",
        "VideoConvertor", "VideoRemuxer", "ThumbnailsConvertor", "MoveFiles", "SponsorBlock", "ModifyChapters",
    ];

    public static IReadOnlyList<JobUpdate> Parse(string line, bool fromStdErr)
    {
        if (line.StartsWith(ProgressMarker, StringComparison.Ordinal))
        {
            var f = line[ProgressMarker.Length..].Split('|');
            double? Get(int i) => i < f.Length ? Number(f[i]) : null;
            return [new JobUpdate.Progress(Get(0), Get(1) ?? Get(2), Get(3), Get(4))];
        }

        if (line.StartsWith(TitleMarker, StringComparison.Ordinal))
        {
            return [new JobUpdate.Title(line[TitleMarker.Length..])];
        }

        if (line.StartsWith(FileMarker, StringComparison.Ordinal))
        {
            var path = line[FileMarker.Length..];
            return [new JobUpdate.FileSaved(path), new JobUpdate.Log("Saved: " + path, false)];
        }

        var updates = new List<JobUpdate>(2);
        if (line.StartsWith("[download] ", StringComparison.Ordinal))
        {
            var rest = line["[download] ".Length..];
            if (rest.StartsWith("Destination:", StringComparison.Ordinal))
            {
                updates.Add(new JobUpdate.Destination());
            }
            else if (rest.StartsWith("Downloading item ", StringComparison.Ordinal))
            {
                // "Downloading item 3 of 10"
                var parts = rest["Downloading item ".Length..].Split(" of ");
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var index)
                    && int.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var count))
                {
                    updates.Add(new JobUpdate.PlaylistItem(index, count));
                }
            }
        }
        else if (line.StartsWith("[info] ", StringComparison.Ordinal))
        {
            // "[info] abc: Downloading 1 format(s): 399+251"
            var at = line.IndexOf("format(s): ", StringComparison.Ordinal);
            if (at >= 0)
            {
                updates.Add(new JobUpdate.Streams(line[(at + "format(s): ".Length)..].Trim().Split('+').Length));
            }
        }
        else if (line.StartsWith('[') && line.IndexOf(']') is > 1 and var close && Stages.Contains(line[1..close]))
        {
            updates.Add(new JobUpdate.Stage(line[1..close]));
        }

        var isError = fromStdErr && line.StartsWith("ERROR", StringComparison.Ordinal);
        updates.Add(new JobUpdate.Log(line, isError));
        return updates;
    }

    private static double? Number(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
}
