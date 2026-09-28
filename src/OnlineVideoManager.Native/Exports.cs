using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Interop;
using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.Native;

public static unsafe class Exports
{
    private static readonly ConcurrentDictionary<long, CancellationTokenSource> Operations = new();
    private static long _nextId;

    [UnmanagedCallersOnly(EntryPoint = "ovm_abi_version", CallConvs = [typeof(CallConvCdecl)])]
    public static int Version() => 1;

    [UnmanagedCallersOnly(EntryPoint = "ovm_create", CallConvs = [typeof(CallConvCdecl)])]
    public static long Create()
    {
        try
        {
            long id = Interlocked.Increment(ref _nextId);
            return Operations.TryAdd(id, new CancellationTokenSource()) ? id : 0;
        }
        catch { return 0; }
    }

    // The caller retains the handle until execute returns; cancellation can arrive on another thread.
    [UnmanagedCallersOnly(EntryPoint = "ovm_cancel", CallConvs = [typeof(CallConvCdecl)])]
    public static void Cancel(long id)
    {
        try { if (Operations.TryGetValue(id, out var cts)) cts.Cancel(); }
        catch { /* Never allow managed exceptions across the C ABI. */ }
    }

    [UnmanagedCallersOnly(EntryPoint = "ovm_destroy", CallConvs = [typeof(CallConvCdecl)])]
    public static void Destroy(long id)
    {
        try { if (Operations.TryRemove(id, out var cts)) cts.Dispose(); }
        catch { }
    }

    [UnmanagedCallersOnly(EntryPoint = "ovm_execute", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Execute(long id, nint json, delegate* unmanaged[Cdecl]<nint, nint, void> callback, nint context)
    {
        CoreResponse response;
        try
        {
            if (!Operations.TryGetValue(id, out var cts)) throw new ArgumentException("Invalid operation handle.");
            var request = JsonSerializer.Deserialize(Marshal.PtrToStringUTF8(json) ?? "", ProtocolJson.Default.CoreRequest)
                ?? throw new ArgumentException("Missing request.");
            void Report(CoreEvent update)
            {
                if (callback == null) return;
                var data = Marshal.StringToCoTaskMemUTF8(JsonSerializer.Serialize(update, ProtocolJson.Default.CoreEvent));
                try { callback(data, context); }
                finally { Marshal.FreeCoTaskMem(data); }
            }
            response = DispatchAsync(request, Report, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { response = new(Canceled: true); }
        catch (Exception ex) { response = new(Error: ex.Message); }
        try { return Marshal.StringToCoTaskMemUTF8(JsonSerializer.Serialize(response, ProtocolJson.Default.CoreResponse)); }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(EntryPoint = "ovm_free", CallConvs = [typeof(CallConvCdecl)])]
    public static void Free(nint data) => Marshal.FreeCoTaskMem(data);

    private static Task<CoreResponse> DispatchAsync(CoreRequest request, Action<CoreEvent> report, CancellationToken ct) =>
        EngineDispatch.RunAsync(request, report, ct);
}

internal static class EngineDispatch
{
    public static async Task<CoreResponse> RunAsync(CoreRequest request, Action<CoreEvent> report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var tools = new ToolManager();
        switch (request.Operation)
        {
            case "download":
                return new(ExitCode: await new YtDlpRunner().RunAsync(request.Download ?? throw new ArgumentException("Missing download."),
                    update => report(new(Download: update)), ct).ConfigureAwait(false));
            case "version": return new(Version: await tools.GetLocalVersionAsync(request.Directory, request.Tool, ct).ConfigureAwait(false));
            case "latest": return new(Release: await tools.GetLatestAsync(request.Tool, request.Channel, ct).ConfigureAwait(false));
            case "install":
                await tools.InstallAsync(request.Directory, request.Tool, request.Release ?? throw new ArgumentException("Missing release."),
                    new InlineProgress(update => report(new(Install: update))), ct).ConfigureAwait(false);
                return new();
            case "cleanup": tools.Cleanup(request.Directory); return new();
            default: throw new ArgumentException("Unknown operation.");
        }
    }
    private sealed class InlineProgress(Action<ToolInstallProgress> callback) : IProgress<ToolInstallProgress>
    {
        public void Report(ToolInstallProgress value) => callback(value);
    }
}
