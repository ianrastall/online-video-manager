# OVM C ABI 2

`ovm_core.dll` is compiled from C++20 with MSVC and a static C/C++ runtime.
It does not load .NET. `include/ovm.h` is consumable by C, C++, and P/Invoke.
Windows x64 only; all strings are null-terminated UTF-8.

## Lifetime and threading

- `ovm_abi_version()` returns 2.
- `ovm_open(home)` returns `{"handle":123}` or `{"error":"message"}`. A null or empty home uses `OVM_HOME`, then LocalAppData/OnlineVideoManager. One engine may own a data directory at a time.
- `ovm_execute(handle, request)` returns a JSON object containing `state` plus operation results, or an `error`. Each request must be a JSON object; the maximum encoded request size is 32 MiB.
- `ovm_close(handle)` cancels all work, joins the scheduler/workers and releases the data-directory lock. Calling it twice is harmless. Finish outstanding calls before closing; keep the DLL loaded until every handle and returned buffer is released.
- `ovm_free(buffer)` releases every returned string, including errors. It accepts null. Never free these buffers using a caller's allocator. A null response indicates allocation failure.

No C++ exception intentionally crosses the boundary. Handles are validated tokens, not pointers. Commands on one engine are serialized. Network and child-process work runs on native worker threads; quick command calls return snapshots. Native updates wait for active download process trees to exit and stop new jobs from starting during replacement. Closing may wait for an in-progress Windows network call to time out.

## Commands

Each request contains an `operation` string and the listed optional/required arguments.

| Operation | Arguments and result |
| --- | --- |
| `snapshot` | Returns current state. Poll about four times a second while the UI is open. |
| `initialize` | Detects installed tools, applies startup update preferences, enables the native daily update schedule. Idempotent. |
| `settings.set` | Required full `settings` object. Native normalization and atomic persistence happen before acceptance. |
| `settings.resetTemplate` | Restores the native default output filename template. |
| `links.preview` | `text`, `html`; returns extracted, deduplicated `links` and `count`. |
| `clipboard.collect` | `text`, `html`; filters and saves inbox links; returns `added`. Never enqueues or starts downloads. |
| `inbox.clear` | Clears the saved inbox. |
| `inbox.remove` | Required `urls` array. Removes selected inbox links. |
| `inbox.queue` | Required `urls` array; optional `mode`. Moves selected inbox links into the queue. |
| `queue.add` | `text`, `html`; optional `mode` (`Video` or `Audio`). Returns `added`, `skipped`. |
| `queue.import` | Required text-file `path`; optional `mode`. Native file reading supports UTF-8/BOM and UTF-16LE/BOM, up to 16 MiB. |
| `queue.start`, `queue.pause` | Starts or pauses scheduling. Pause leaves current downloads running. |
| `queue.cancel`, `queue.remove`, `queue.retry` | Required item `id`. Cancellation terminates the complete Windows process job. |
| `queue.cancelAll`, `queue.retryFailed`, `queue.clearFinished` | Queue-wide actions. |
| `tools.check` | Starts upstream version checks. |
| `tools.update` | Installs missing/outdated tools; optional `tool` (`yt-dlp`, `deno`, `ffmpeg`) forces reinstall of that tool. FFmpeg includes ffprobe and shared libraries. |
| `tools.cancel` | Cancels the current tool operation. |
| `folder.prepare` | `kind`: `tools` or `output`; creates the folder and returns its `path`. |

## Snapshots and settings

State contains `revision`, `settings`, `dataDirectory`, `toolsDirectory`, `knownSites`,
`items`, `inbox`, `running`, `toolsBusy`, `tools`, `toolStatus`, `toolLogs`,
`noticeId`, and `notice`. Revisions increase after mutations; clients should discard older
snapshots received out of order. Notice IDs let the UI avoid repeating notifications.

Download items include IDs, URL, mode, status, title, saved-file path, error, aggregate progress
(0–100), indeterminate/stage flags, byte totals, speed, ETA, stream/playlist positions, and logs.
Tool progress is a fraction (0–1), or -1 for an indeterminate phase.
Statuses are `Queued`, `Running`, `Completed`, `Failed`, and `Canceled`.

The complete wire schema is in `src/OnlineVideoManager.Contracts/Protocol.cs`. These are
serialization declarations only. Native defaults and validation live in `native/media.cpp`.
To edit settings, read the snapshot, modify its settings object, and send `settings.set`.

Native persistence uses `settings.json`, `queue.json`, and `inbox.json` in the engine home.
Unfinished jobs restore paused. Malformed JSON is preserved under an `.invalid.*` filename.
The download archive, when enabled, lives in the output folder.

See `native/c_abi_smoke.c` for a C-only client and `OnlineVideoManager.Interop/NativeEngine.cs`
for the managed adapter. Source and DLL must be rebuilt together when the ABI version changes.
