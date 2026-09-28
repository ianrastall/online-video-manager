using System.Text.Json.Serialization;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.Core.Interop;

public sealed record CoreRequest(string Operation, string Directory = "", ToolId Tool = ToolId.YtDlp,
    UpdateChannel Channel = UpdateChannel.Stable, ToolRelease? Release = null, DownloadRequest? Download = null);
public sealed record CoreResponse(string? Error = null, bool Canceled = false, int ExitCode = 0,
    string? Version = null, ToolRelease? Release = null);
public sealed record CoreEvent(JobUpdate? Download = null, ToolInstallProgress? Install = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CoreRequest))]
[JsonSerializable(typeof(CoreResponse))]
[JsonSerializable(typeof(CoreEvent))]
public sealed partial class ProtocolJson : JsonSerializerContext;
