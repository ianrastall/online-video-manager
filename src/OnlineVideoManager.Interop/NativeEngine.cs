using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using OnlineVideoManager.Contracts;

namespace OnlineVideoManager.Interop;

/// <summary>Marshalling and lifetime only; all application operations execute inside ovm_core.dll.</summary>
public sealed class NativeEngine : IEngine
{
    private readonly EngineHandle _handle;
    public NativeEngine(string home = "")
    {
        if (Methods.Version() != 2) throw new InvalidOperationException("OVM requires native engine ABI 2.");
        var reply = Read(Methods.Open(home));
        if (reply.Handle == 0) throw new InvalidOperationException("Native engine returned an invalid handle.");
        _handle = new EngineHandle(reply.Handle);
    }
    public EngineReply Execute(EngineRequest request) => Read(Methods.Execute(_handle,
        JsonSerializer.Serialize(request, ProtocolJson.Default.EngineRequest)));
    public Task<EngineReply> ExecuteAsync(EngineRequest request) => Task.Run(() => Execute(request));
    public void Dispose() => _handle.Dispose();
    private static EngineReply Read(nint pointer)
    {
        if (pointer == 0) throw new OutOfMemoryException("The native engine could not allocate a reply.");
        try
        {
            var reply = JsonSerializer.Deserialize(Marshal.PtrToStringUTF8(pointer)!, ProtocolJson.Default.EngineReply)
                ?? throw new InvalidOperationException("The native engine returned an empty reply.");
            if (reply.Error is { } error) throw new InvalidOperationException(error);
            return reply;
        }
        finally { Methods.Free(pointer); }
    }
    private sealed class EngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public EngineHandle(ulong value) : base(true) => SetHandle(unchecked((nint)value));
        protected override bool ReleaseHandle() { Methods.Close(unchecked((ulong)handle)); return true; }
    }
    private static class Methods
    {
        [DllImport("ovm_core", EntryPoint = "ovm_abi_version", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Version();
        [DllImport("ovm_core", EntryPoint = "ovm_open", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Open([MarshalAs(UnmanagedType.LPUTF8Str)] string home);
        [DllImport("ovm_core", EntryPoint = "ovm_execute", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Execute(EngineHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string request);
        [DllImport("ovm_core", EntryPoint = "ovm_close", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Close(ulong handle);
        [DllImport("ovm_core", EntryPoint = "ovm_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(nint result);
    }
}
