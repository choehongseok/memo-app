# Selected authenticated PNG display continuation
Baseline0aaa047; follows existing image-preview/lease/pixel-decoder plans without restarting approved product design. Source27417bytes/hash/134IDs/M06/schema compatibility remain unchanged.

## Execution choices
Explicit selected-file preview button, never selection/import automatic decode. Global synchronous Windows Dispatcher admission is acquired before source decryption and held through awaited worker/UI continuation and raster cleanup. No queue: while busy, reject without decrypting and show retry guidance. This is a narrower retention policy than the earlier optional identifier queue; no lost data or scope removal. At most one globally displayed detached WPF bitmap; admission clears the prior host. Worker captures only registered lease/token, never live coordinator/Workspace. Existing bounded runtime inflater is retained; no generic WIC codec, helper, external service or new dependency. Cancellation is cooperative, not hard time/native-memory/process containment.

Full authority check: panel lifetime/current callback, active session, original draft membership/closed/deleted state, EditVersion, selected attachment ID/current ref, request generation, cancellation, and UI Dispatcher. Recheck before decrypt, after worker decode, before/after bitmap construction and native Source publication. Cancel/clear on selection, edit, detach, deletion, session fault, close/dispose and conceal. Model reads stay on UI context. Posted continuation keeps global admission until cleanup; no new source decrypts while UI is blocked.

WPF only sees bounded detached BGRA with fixed96DPI. Borrowed pixels are copied into an explicitly owned <=4MiB array for BitmapSource.Create and zeroed in finally; raster/lease cleanup also uses finally. Bitmap frozen and assigned only under renewed authority. No file thumbnail cache. Clear native Source/visibility immediately on invalidation; no promise of GPU/WPF cache or paging erasure.

## Tasks and evidence
- [x] Core bridge missing-type RED observed; single-use/revocation/cancellation and literal pixel oracle GREEN.
- [x] Windows missing-Image UI RED, no trial upload.
- [x] Implement UI admission/ownership/conceal checks after preactivation read-only review.
- [x] Windows literal BGRA, native alpha rendering, Main+Sticky global capacity/replacement, spoofed MIME, selection/edit/lock stale publication, detach/close/reopen/fault checks.
- [x] Linux full Core/source/Python/safety and serial crossbuild; exact-commit Windows full checks.
- [x] Independent actual-diff review, fix Important/Critical findings; update H08 partial status, README, STATUS, VERIFICATION.

A narrower supported PNG profile is partial H08, not full image fidelity/user acceptance. JPEG, inline schema image nodes, clipboard/drop and external opening remain separate feature units; original opaque attachment bytes/hash/history/backup stay exact.

Primary display API: https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapsource.create?view=windowsdesktop-10.0 . Actual Windows renderer tests establish the application inference about copying/frozen lifetime; docs alone are not evidence of erasure.

## Review corrections before activation
Independent read-only reviewer found early public invalidation callbacks unsafe (native Source clearing may admit a fresh old-object lease or lock a draft while its setter is still mutating). Early Core hook now ONLY increments scalar epoch and revokes tracker, preserving NoteDraft's inert boundary. Panel clears via existing post-mutation public note/workspace/coordinator notifications and verifies authority before each render. Direct public AcceptPrepared revokes publication synchronously; displayed Source is detached at next rendering boundary, before drawing, without introducing a reentrant callback into the setter/transaction. Lock/selection/close conceal remains immediate.

Worker delegate creation moved into separate static DecodeAsync helper to prevent compiler closure sharing with UI authority/panel. Renderer backend abstraction supports genuine paused lease/raster tests and native creation exceptions; production default carries no model/session fields.

Executed image Windows gates: 37916364054 missing-UI RED; 37917328859 and37917733679 GREEN; isolated Dispatcher shutdown37918019928 GREEN. Independent actual-diff reading found no remaining Critical/Important after epoch/closure corrections; not independent test execution. Final handoff records exact-head CI separately.
