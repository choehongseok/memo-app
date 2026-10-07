# Bounded selected-image preview proposal

## Scope and first independent unit
H08 first previews a selected existing encrypted attachment beside the memo. Original object bytes/hash/refs/history/backup remain unchanged. Keep schema5 image/opaque-reference nodes explicitly rejected; inline image editing, clipboard/drop, external opening and export remain separate units. No real personal files, additional packages, codec installation, URI/network source, thumbnails on disk or automatic decoding.

First implement only an internal managed PNG structure/profile preflight with synthetic RED-first tests. Input is a synchronously borrowed ReadOnlySpan, not retained/cloned, and result is immutable scalar-only header data granting no decode/session/publication authority. It neither decompresses nor constructs a native image and is not an enabled preview. A successful result means the strict bounded container/header profile passed, not that compressed pixels are valid. Decoder selection, owned-byte access, queue/lifetime and actual Windows rendering need independent review before activation.

## Strict application profile and budgets
These are trial application limits, not PNG format limits or Microsoft guarantees.
- Authenticated original source at most4MiB; no extension/MIME authority
- PNG signature and exact container EOF; exactly one IHDR of13 bytes first, one or more consecutive IDAT chunks with aggregate nonempty data, then exactly one empty IEND last. Individual empty IDAT chunks may pass. Chunk types compare exact four-byte values. Container EOF is not proof of compressed-stream EOF.
- Only IHDR/IDAT/IEND; reject all ancillary/unknown chunks, APNG, text/EXIF/profiles, palettes and extra frames
-8-bit RGB/RGBA only: depth8/color2 or6/compression0/filter0/interlace0. BGRA16MiB is a calculated pixel equivalent, not a native allocation limit.
- width/height1..4096, checked source pixels at most4,194,304 and BGRA equivalent at most16MiB
- scan at most256 chunks and the bounded entire source; unsigned lengths must be at most2^31-1 and checked against remaining bytes before casting/slicing/addition. At least12 remaining bytes is checked before reading a chunk. Non-IDAT structure means signature8 + every chunk overhead12 + IHDR payload13. With this exact allowlist/chunk cap it is at most3,093bytes, so the conservative64KiB cap is dominated. No valid64KiB/64KiB+1 boundary claim is possible without widening the profile.
- validate every chunk CRC; CRC is a structural checksum, not authentication
- intended output has fixed96DPI, no enlargement, aspect-preserving bounds1024x1024/1,048,576pixels/4MiB; no native allocation in this first preflight unit

This intentionally refuses many otherwise valid PNG files. Original attachment import/storage stays opaque and available. JPEG is a later separately reviewed extension, not assumed supported from a filename or generic native codec.

## Later decode ownership and authority gate
Do not make raw internal ReadAttachmentBytes publicly reusable or access a live Workspace from Task.Run. The coordinator is UI-context owned. A future narrow registered preview lease captures active note/ref/version/epoch and immutable object, revokes publication immediately at lock, and owns every temporary plaintext array until finally-zero. Running native code must not read an array concurrently zeroed by another thread. Conceal is immediate, while unavoidable residual decode lifetime is accurately disclosed.

Use one application-wide decode slot, pending identifiers only, latest-only result/posted callback, and a small explicit live-raster cap. Repeated requests cannot allocate decrypted bytes before admission. Recheck full source authority at admission, after decryption, after decoding, and immediately before/after UI publication. Detach/delete/selection/window-close/lock/new-session invalidate work. No persistent preview cache.

WIC is extensible: a generic factory vendor argument is preference, not an exclusive decoder allowlist. Before native activation, select and verify an exact documented in-box PNG decoder (or independently review an equivalent bounded decoder); fail closed without generic fallback. Produce only detached bounded BGRA pixels and no original stream/frame/metadata/profile retained by UI. Cooperative cancellation/Task.Run/Freeze are not a sandbox or hard native time/memory bound. If hard wall-time/crash containment is required, separately review a disposable helper process with bounded pipes/Job Object lifetime/memory limits. A Job Object is not a file/network sandbox. No claim of immediate WPF/GPU/native-cache/paging erasure.

## Required evidence
Preflight: valid tiny synthetic container, actual boundary/boundary+1 dimensions/pixels/bytes/chunks, overflow/truncation/CRC/order/header/trailing/animation/metadata/depth/interlace and spoofed filename/MIME. Input bytes unchanged; no native decoder invoked.
Later Windows: accepted-header bad compressed pixels, exact decoder identity/frame/dimension/stride/pixel agreement, transparent pixels and independent rendering, disposed source streams/leases and failure zeroing, repeated/Main+Sticky/slow decode/blocked UI/stale-post/lock-at-all-boundaries, cleanup exceptions and application-wide retention. Helper isolation, if selected, also needs hang/crash/oversized-output/parent-exit/no-orphan tests.

Independent source research proposed this split; it is not an independent execution, professional audit or activated preview. Native enablement remains gated. H08/full-format fidelity/user acceptance remain unconfirmed.

## Primary references
- W3C PNG Third Edition: https://www.w3.org/TR/png-3/
- WIC codec identities: https://learn.microsoft.com/en-us/windows/win32/wic/-wic-guids-clsids
- WIC CreateDecoder vendor preference: https://learn.microsoft.com/en-us/windows/win32/api/wincodec/nf-wincodec-iwicimagingfactory-createdecoder
- WIC bounded CopyPixels: https://learn.microsoft.com/en-us/windows/win32/api/wincodec/nf-wincodec-iwicbitmapsource-copypixels
- WPF detached BitmapSource.Create: https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapsource.create?view=windowsdesktop-10.0
- Cancellation: https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
- Freezable lifetime: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/freezable-objects-overview
- Job Objects: https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects

Independent pre-code read-only review permits this internal non-native split conditionally with RED-first tests; the dominated structural-budget definition above incorporates its required correction. Actual parser diff and execution evidence remain separate gates.
