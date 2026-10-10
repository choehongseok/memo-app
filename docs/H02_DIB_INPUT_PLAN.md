# H02 bounded Windows DIB clipboard input plan

Status: design only; not implemented. H02 remains partial and user acceptance remains unconfirmed. This is a follow-up phase separate from the current H01 source batch. Independent pre-implementation security review by `transfer_final_review` is complete and the parent has reviewed the result. The CF_DIB8/header40 first unit is approved as the next source phase; implementation remains pending after the H01 snapshot. Only this plan update is authorized for this step; no production implementation or build starts until the parent assigns the next implementation unit.

## Goal and first product scope

Support a strict subset of ordinary Windows bitmap clipboard input through the existing explicit attachment button and attachment-panel-focus Ctrl+V command. Preserve the existing exact raw PNG path. Convert supported DIB pixels into a generated PNG, then use existing atomic encrypted attachment preparation, insertion and save. A subsequent explicit “문서에 이미지 추가” command may insert that authenticated PNG attachment through the existing H01 version, selection and document-boundary checks.

The first phase does not intercept Ctrl+V in plain or rich text editors, change normal text/selection paste behavior, automatically display pixels, or automatically insert an image into a document. H02 remains partial until separately reviewed editor routing and broader format support exist. No clipboard monitoring/history, image-file URI retrieval, external dependency, WIC/GDI decoder, `Clipboard.GetImage`, automatic managed bitmap conversion, or external service is introduced.

## Native read-only capture contract

Accept native CF_DIB (format 8) only. CF_DIBV5 (format 17) and BITMAPV4/V5 headers are explicitly refused in this first phase; do not advertise DIBV5 support. A successful format lookup does not authorize content or mutation.

Before any clipboard access, acquire the application-wide `ImagePreviewAdmission` slot and capture the active session, source note, edit version, attachment preview epoch, UI-current predicate and cancellation token. Refuse busy, stale, locked or disposed hosts before querying the clipboard. Preserve existing raw PNG behavior and exact borrowed byte-array/MemoryStream ownership rules; DIB is an explicit fallback, not a reason to query unrelated clipboard types.

Use `OpenClipboard`, `GetClipboardData(8)`, `GlobalSize`, `GlobalLock`, bounded `Marshal.Copy`, `GlobalUnlock` and `CloseClipboard`. Check the native `SIZE_T` without narrowing first: allocation size must be 40 through 4,194,304 bytes. Refuse invalid handles, zero size, failed lock or oversized memory before managed allocation or pointer reads. Copy at most 65,536 bytes per iteration, with cancellation and command-authority checks between iterations. No pointer or native handle escapes capture, and no clipboard handle is used after closing.

Recheck command authority after every potentially reentrant native call and immediately after copying and closing. Capture and verify the clipboard sequence across the operation; sequence 0 is refused and changed input is refused. A successful capture creates an owned bounded byte buffer only. Managed workers receive that owned input/format DTO and cancellation token; they do not receive UI objects, sessions, key owners or authority callbacks.

Always match a successful `GlobalLock` with `GlobalUnlock`, and a successful `OpenClipboard` with `CloseClipboard`, including exception, cancellation and revocation paths. Clear the thread last-error value immediately before `GlobalUnlock`; returning false with last error zero indicates successful final unlock, while false with a nonzero last error is failure. Failed native cleanup discards the capture and zeroes all owned scratch rather than applying an attachment. Never call `GlobalFree` on the clipboard-owned handle, `EmptyClipboard`, `SetClipboardData`, or write into clipboard memory in production.

There is no hard timeout guarantee for a native clipboard call or delayed rendering. Cancellation revokes application authority but does not release image admission or owned buffers until capture/worker cleanup has actually settled.

## Managed DIB grammar and budgets

Parse little-endian fields from a bounded span; never reinterpret an untrusted native pointer as a header. Require:

- Header size exactly 40 bytes (`BITMAPINFOHEADER`).
- Width 1 through 4096; signed height -4096 through -1 or 1 through 4096. Reject zero and `INT_MIN` before taking absolute height.
- Planes exactly 1; bit count exactly 24 or 32; compression exactly `BI_RGB` (0).
- `biClrUsed` and `biClrImportant` both zero; no palette or appended masks.
- Checked 64-bit pixel count at most 4,194,304.
- DWORD-aligned stride `((width * bits + 31) / 32) * 4`, computed with checked arithmetic.
- `biSizeImage` either zero or exactly `stride * abs(height)`.
- Exact logical payload end `40 + stride * abs(height)` no greater than captured allocation size, with the entire native allocation at most 4 MiB.

`GlobalSize` may exceed the producer's requested size. Permit bytes beyond the exact logical payload only when all are zero; refuse any nonzero tail. Padding inside rows is ignored and is never included in encoded pixels. This strict zero-tail policy is a compatibility limit, not evidence that all native producers zero allocation slack.

Positive height is bottom-up; negative height is top-down. Normalize output row order to top-down. Decode 24-bit BGR or 32-bit BGR plus unused byte. The unused 32-bit high byte never becomes inferred alpha: output alpha is always opaque 255. Generate RGB8 PNG, with no source DPI/profile metadata or color correction; existing display uses fixed 96 DPI.

Refuse other header sizes, bit depths, compression, masks, palettes, linked/embedded ICC profiles, embedded JPEG/PNG, malformed dimensions or lengths, and all insufficient payloads. Do not fetch a linked profile or invoke a native codec. The 4 MiB DIB allocation limit excludes many full-screen screenshots, including common larger 32-bit captures; do not claim general screenshot compatibility.

## Bounded PNG conversion and ownership

Use managed BCL `ZLibStream` and a strict PNG writer: signature, RGB8 IHDR, IDAT and IEND with CRC. Encode filter 0 rows using at most 12,289 owned bytes for one maximum-width RGB scanline plus filter byte. No full expanded BGRA raster is needed for conversion.

Compressed and final PNG buffers have a 4 MiB hard limit enforced before every write and during stream finalization. Reject overflow as a whole operation; never truncate. Use zeroing bounded buffers whose replaced growth buffers are also cleared, or a fixed-capacity equivalent. Validate the completed PNG with the existing bounded structure profile before attachment. This structural validation does not grant valid-pixel or publication authority.

Keep owned DIB, scanline, compressed and final PNG buffers accounted for across success, allocation-observer failures, cancellation, encode/finalization errors and cleanup. Zero every application-owned plaintext buffer when its ownership ends. Distinguish these observable owned-buffer guarantees from unobservable internal BCL/OS implementation storage. Borrowed clipboard data is never modified or zeroed by the app.

## Atomic attachment and lifetime integration

Reuse the current `PrepareAttachments` handoff: only its expected single anchoring snapshot may update the captured attachment preview epoch. Every other source edit, accepted snapshot, selection/UI change, lock, closure or cancellation revokes the command. Recheck authority after capture, worker conversion, preparation completion, and immediately before `AttachBytes`. After the worker result and `PrepareAttachments` completion, also re-read the clipboard sequence and require it to remain nonzero and equal to the captured sequence. The final check immediately before attachment application includes this clipboard sequence requirement; matching source and attachment epochs alone is insufficient.

Attach the generated PNG as `clipboard-image.png` / `image/png` through existing atomic attachment rules, then encrypted save. Do not apply partial bytes or early candidate state on refusal. After application, report save failure truthfully as retained edited state rather than claiming rollback. Report that DIB pixels were converted to PNG; unlike raw PNG attachment, the native DIB container is not preserved as attachment bytes. The OS clipboard remains present and is not cleared by app lock.

Hold one application-wide image/OCR admission through capture, conversion, preparation, attachment application, save and actual worker cleanup. A lock may cancel the command and clear display but cannot release admission early while work is still running. Existing explicit preview and authenticated document insertion have their own source/version/boundary checks and remain separate user actions.

## Required tests and evidence

Core test-first contracts:

- Actual 24-bit widths 1 and 3 with DWORD row padding; both height signs; independent exact BGRA oracle after generated PNG decoding.
- Actual 32-bit unused bytes 0, 128 and 255 all result in opaque alpha 255 and exact B/G/R values.
- Header/planes/bit-depth/compression/palette/mask/V4/V5/ICC refusal, every truncation, zero-only allocation tail, nonzero tail rejection, and changed borrowed source preservation.
- Width, height, `INT_MIN`, pixel count, stride, `biSizeImage`, native/source and PNG-output exact bounds and overflow refusal, with no candidate applied.
- Cancellation and failures at every owned allocation, copy, encoding and finalization boundary; observed owned buffers zeroed, including replaced growth buffers and allocation-observer failures.
- Existing raw PNG, RGB/RGBA/grayscale preview, encrypted attachment atomicity and lifetime tests remain unchanged and passing.

Windows evidence must use a synthetic native clipboard fixture. Test code alone may create a movable `HGLOBAL` and set CF_DIB to known synthetic bytes, with explicit cleanup confined to that synthetic fixture. Exercise production button and attachment-panel Ctrl+V routes: real native capture, encrypted attach, explicit frozen WPF BGRA display, actual lock/display clearing/key release, and independent encrypted snapshot/object reopen. Assert exact decoded pixels and generated PNG preservation. Observe clipboard content/format/sequence before and after the production reader; do not label an OS-synthesized conversion as an application write.

Inject or reproduce failures/revocations around Open/Get/Size/Lock/Copy/Unlock/Close; verify edit-version, accepted-snapshot, UI epoch and lock revocation, cleanup, no application, oversize rejection before copy, and admission retained until a delayed worker settles. Explicitly refuse sequence 0 and simulate a changed clipboard sequence after the worker result and after attachment preparation; both must refuse application even when source version and attachment epoch still match. Verify a later explicit H01 image insertion uses one authenticated attachment reference and preserves it after restart without duplicate attachment application. OCR recognition is not required for letter-free screenshot fixtures; shared admission and PNG decoding compatibility are required.

Core RED/GREEN and actual Windows synthetic-native evidence must be reported separately. No physical keyboard, real screenshot-provider compatibility, general native timeout, broad screenshot support or user acceptance claim follows from these automated fixtures.

## Platform limits and primary sources

Windows may synthesize clipboard formats inside `GetClipboardData`; even `EnumClipboardFormats` includes formats the system can convert. Requesting only DIB and avoiding direct WIC/GDI calls does not prove that the OS performed no conversion or that a DIB originated independently of CF_BITMAP. This limitation is part of the supported contract.

- [Standard clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/standard-clipboard-formats): CF_DIB and CF_DIBV5 container distinction.
- [GetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata): untrusted input, clipboard ownership, prompt copy, handle lifetime and implicit format conversion.
- [EnumClipboardFormats](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumclipboardformats): enumeration includes convertible synthesized formats.
- [GlobalSize](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-globalsize): allocation size may exceed requested size.
- [GlobalLock](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-globallock): successful locks must be matched by unlocks.
- [BITMAPINFOHEADER](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader): signed row orientation, planes, BI_RGB size and stride rules.
- [BITMAPV5HEADER](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapv5header): BI_RGB 32-bit high byte is unused; profile/mask fields require separate interpretation and remain refused here.
