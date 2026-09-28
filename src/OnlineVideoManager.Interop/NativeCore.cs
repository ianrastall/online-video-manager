using System.Runtime.InteropServices;
using System.Text.Json;
using OnlineVideoManager.Core.Interop;

namespace OnlineVideoManager.Interop;

public static class NativeCore
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EventCallback(nint json, nint context);
    [DllImport("ovm_core", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ovm_abi_version")]
    private static extern int AbiVersion();
    [DllImport("ovm_core", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ovm_create")]
    private static extern long Create();
    [DllImport("ovm_core", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ovm_cancel")]
    private static extern void Cancel(long id);
    [DllImport("ovm_core", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ovm_destroy")]
    private static extern void Destroy(long id);
    [DllImport("ovm_core", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ovm_execute")]
    private static extern nint Execute(long id, [MarshalAs(UnmanagedType.LPUTF8Str)] string json, EventCallback callback, nint context);
    [DllImport("ovm_core", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ovm_free")]
    private static extern void Free(nint data);

    public static Task<CoreResponse> CallAsync(CoreRequest request, Action<CoreEvent>? onEvent = null, CancellationToken ct = default) =>
        Task.Run(() => Call(request, onEvent, ct), ct);

    public static CoreResponse Call(CoreRequest request, Action<CoreEvent>? onEvent = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (AbiVersion() != 1) throw new InvalidOperationException("The installed OVM engine has an incompatible ABI.");
        long id = Create();
        if (id == 0) throw new InvalidOperationException("Could not create an engine operation.");
        try
        {
            using var registration = ct.Register(() => Cancel(id));
            Exception? callbackError = null;
            EventCallback callback = (json, _) =>
            {
                try
                {
                    var update = JsonSerializer.Deserialize(Marshal.PtrToStringUTF8(json)!, ProtocolJson.Default.CoreEvent);
                    if (update is not null) onEvent?.Invoke(update);
                }
                catch (Exception ex) { Interlocked.CompareExchange(ref callbackError, ex, null); Cancel(id); }
            };
            var data = Execute(id, JsonSerializer.Serialize(request, ProtocolJson.Default.CoreRequest), callback, 0);
            GC.KeepAlive(callback);
            if (data == 0) throw new InvalidOperationException("The OVM engine could not return a response.");
            CoreResponse response;
            try { response = JsonSerializer.Deserialize(Marshal.PtrToStringUTF8(data)!, ProtocolJson.Default.CoreResponse)!; }
            finally { Free(data); }
            if (callbackError is not null) throw new InvalidOperationException("Could not process an engine event.", callbackError);
            if (response.Canceled) throw new OperationCanceledException(ct);
            if (response.Error is { } error) throw new InvalidOperationException(error);
            return response;
        }
        finally { Destroy(id); }
    }
}
