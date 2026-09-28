using System.Text.Json;

namespace OnlineVideoManager.Core.Tools;

/// <summary>Kept in the tools directory; records what this app installed there.</summary>
public sealed class ToolManifest
{
    public const string FileName = ".ovm-tools.json";

    public Dictionary<string, string> Versions { get; set; } = [];

    /// <summary>Files installed from the last ffmpeg archive, so stale DLLs can be removed on update.</summary>
    public List<string> FfmpegFiles { get; set; } = [];

    public static ToolManifest Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.ToolManifest) ?? new ToolManifest();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        return new ToolManifest();
    }

    public void Save(string directory) =>
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, CoreJsonContext.Default.ToolManifest));
}
