using System.Text.Json.Serialization;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.Core;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<SavedDownload>))]
[JsonSerializable(typeof(ToolManifest))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
