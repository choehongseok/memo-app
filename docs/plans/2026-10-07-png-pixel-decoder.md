# Strict bounded PNG pixel decoder proposal

## Purpose and permitted next unit
Decode only the existing internal RGB/RGBA PNG profile into an owned bounded BGRA raster. This Core-only unit activates no preview, file access, URI/network, generic WIC/installed codec, key use, schema/image-node change, clipboard/drop/open/export, cache, or automatic decode. Original attachment bytes/hash/history/backup are preserved. Later application-wide admission and fresh UI/source publication authority remain mandatory before enabling H08.

## Chosen bounded decoder
Use the installed pinned .NET10 runtime's BCL ZLibStream solely as the zlib/DEFLATE primitive, with no new package or codec. The application itself reconstructs the five PNG scanline filters for depth8 truecolor/RGBA, then selects nearest-neighbor samples into the existing no-enlargement preview dimensions (max1024 per axis, max1,048,576 pixels, max4MiB BGRA). RGB alpha is255; RGBA uses straight alpha with transparent RGB preserved. Fixed96DPI is later UI metadata only.

Inspect the whole authenticated source with PngPreviewProfile first. Collect exactly its validated IDAT payloads into one owned compressed array (max4MiB); do not clone or retain the full original source. Header/profile scalars must not be accepted from a caller independently of source. Decompress exactly Height rows of one filter byte plus Width*Channels bytes, using two reusable row arrays of at most16,384 bytes each; never allocate a full source raster. Checked row/output calculations derive from validated scalars. All rows, even discarded/downsampled rows, must be parsed, reconstructed and counted. Reject filter values outside0..4 and any extra decompressed byte. Do not use unrestricted CopyTo/ReadToEnd.

## Complete compressed stream boundary
A bespoke input stream supplies at most one compressed byte per nonempty Read, so ZLibStream cannot swallow a trailing byte or second stream in a read-ahead block. If the inflater requests more input after the exact payload is exhausted, throw a fixed InvalidDataException instead of returning ordinary EOF; this prevents treating an unfinished stream as a successful zero-output EOF. After the exact row budget, perform one nonempty output read and require zero bytes plus exactly all compressed bytes consumed. Reject missing/truncated trailers, checksum corruption, appended bytes/streams, malformed zlib/DEFLATE, preset dictionary and invalid CMF/FLG. Independently verify zlib CMF/FLG constraints and Adler32 over the exact filtered output, including filter bytes, against the final4 payload bytes. The installed runtime's successful-finished behavior is a RED/compatibility gate, never assumed from container CRC. If this gate cannot be established, leave decoding disabled.

## Lifetime and cancellation
The synchronous method borrows only its input span and returns an IDisposable owned raster with immutable width/height/stride and one borrowed pixel callback, never a public array. The call must be globally admitted before acquiring/decrypting a lease when UI is later connected; this unit has no UI queue. A returned raster grants no source/publication authority. A later controller must dispose on revoked Consume=false or stale source/generation.

Compressed/row arrays and any unreturned raster are zeroed in finally after synchronous decoder use, including profile/decompression/filter/checksum/cancellation failure. Decoder stream disposal occurs before owned source arrays are zeroed. Returned pixels are owned until explicit raster disposal; callbacks are synchronous/exclusive/single-use. Callback entry permanently consumes access. Disposal marks access unavailable immediately and, if a callback is running, defers zeroing until its finally exits. Callback finally clears the output on success/failure and reentrant disposal; repeated and reentrant entry are refused. No finalizer or forced thread interruption. Cancellation is checked before allocations, each input read, each row and before result transfer. No caller delegate or live Workspace/key owner is retained.

Per-invocation application-owned scratch <=4MiB compressed +32KiB rows +4MiB result (plus one filter byte), independent of the original lease's4MiB. Concurrent invocations and earlier returned rasters are not included in this per-invocation cap; a global admission/result-lifetime cap is required before UI. BCL pooled/native inflater internals, allocator overhead, OS paging and arbitrary caller copies are not included in this bound or claimed immediately erased. BCL compression can use native code; this is neither a managed-only guarantee nor process isolation/hard CPU/native-memory containment. A later UI enablement review must decide whether a disposable helper is required. This unit does not claim sandboxing, full PNG fidelity or user acceptance.

## RED-first implementation and evidence
1. Add missing-decoder runtime check; observe RED before creating decoder types
2. Add independent synthetic stored/fixed/dynamic zlib fixtures, all five filter oracles (first/previous row, wraparound and Paeth tie order), RGB/RGBA transparency, exact bytes/stride/no enlargement/downsampling, multi-IDAT split including trailer; then implement only this profile
3. Test every compressed-byte truncation, malformed/checksum/FDICT/extra output/trailing byte/concatenated stream, invalid filter, structurally valid bad pixels, exact source/dimension/pixel/output bounds, input invariance and fixed diagnostics
4. Retain actual scratch references through a test-only allocation observer and verify finally-zero for success/failure/cancellation. Verify raster callback/dispose/exception/reentrancy ownership and no public raw accessor/implementation-owned key-owner graph
5. Run targeted/fullCore/four-project serial crossbuild/original134/Python/safety; obtain independent actual-diff read-only review. Exact-commit Windows CI remains a distinct gate before later UI activation

## Primary sources and inference boundary
- PNG filters and zlib/IDAT layout: https://www.w3.org/TR/png-3/
- BCL ZLibStream API: https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibstream?view=net-10.0
- .NET source ReadCore distinguishes completed inflation from source exhaustion: https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs

The single-byte/exhaustion-throw boundary is an application inference from that source, requiring actual installed-runtime fixtures on Linux and Windows. WIC/general image codec activation is absent; Core tests may exercise native BCL inflate. Independent read-only pre-code review permits this inactive unit after the incorporated runtime/single-use/per-invocation/teardown conditions. Actual implementation review and execution remain separate gates.

## Pinned-runtime compatibility evidence boundary
The local installed SDK is10.0.401 (commit e34a38d2ae), with Host/Microsoft.NETCore.App10.0.12 (commit95017c711e). The v10.0.12 source motivates the runtime-specific EOF adapter. global.json pins SDK selection, not every possible runtime-selection route. Record actual runtime identity for Linux/Windows fixtures and repeat the compatibility gate if it changes. Both local targeted fixtures and the exact-commit fixed-SDK Windows fullCore run must establish compatibility before any later UI activation.

Inflater teardown is inside the outer owned-buffer finally boundary. Compressed/row buffers and unreturned pixels are cleared even if stream disposal throws. Transfer of returned raster ownership occurs only after all inflater/stream teardown succeeds.

## Core implementation evidence
Missing-type RED and the short-output/early-allocation cancellation RED regressions were observed before their fixes. Targeted fixtures pass, including independent literal stored/fixed/dynamic block evidence and pixel-level Paeth tie vectors. Independent actual-diff read-only review found no remaining mandatory blocker after the corrections; execution is separate. The source/lease/UI/global-admission gates remain as stated above.
