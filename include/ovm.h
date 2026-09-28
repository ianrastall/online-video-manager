#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
/* ABI 1. UTF-8, NUL-terminated JSON; cdecl. See Core/Interop/Protocol.cs for schema.
   create -> execute (exactly once, blocking worker call) -> destroy.
   cancel may run concurrently with execute; destroy must wait for execute to return.
   Events may arrive concurrently from worker threads. Event memory is borrowed for
   the duration of the callback. Copy it if needed. Never throw across the boundary.
   execute returns owned JSON; release with ovm_free. NULL indicates allocation failure.
   Keep the native library loaded until process exit (NativeAOT does not support unload). */
int32_t ovm_abi_version(void);
int64_t ovm_create(void);
void ovm_cancel(int64_t handle);
void ovm_destroy(int64_t handle);
typedef void (__cdecl *ovm_event)(const char* json, void* context);
char* ovm_execute(int64_t handle, const char* request, ovm_event callback, void* context);
void ovm_free(char* response);
#ifdef __cplusplus
}
#endif
