#pragma once
#include <stdint.h>
#define OVM_CALL __cdecl
#ifdef OVM_BUILD
#define OVM_API __declspec(dllexport)
#else
#define OVM_API __declspec(dllimport)
#endif
#ifdef __cplusplus
extern "C"
{
#endif
    /* ABI 2: C++ engine; UTF-8 JSON, opaque uint64 handles, no CLR or C++ objects.
       ovm_open returns {handle,error}; empty home selects the Windows default.
       execute returns {state,...} or {error}; see docs/abi.md for commands.
       Every returned string is caller-owned and must be passed to ovm_free.
       NULL indicates allocation failure. Commands are serialized, workers are native.
       close cancels and joins workers; finish outstanding calls before closing.
       No exception crosses the ABI. Free buffers before unloading the DLL. */
    OVM_API int32_t OVM_CALL ovm_abi_version(void);
    OVM_API char *OVM_CALL ovm_open(const char *home_utf8);
    OVM_API char *OVM_CALL ovm_execute(uint64_t handle, const char *request_json);
    OVM_API void OVM_CALL ovm_close(uint64_t handle);
    OVM_API void OVM_CALL ovm_free(char *response);
#ifdef __cplusplus
}
#endif
