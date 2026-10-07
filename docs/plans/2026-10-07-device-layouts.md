# Device-local Windows layout, preferences and date widgets

## Outcome
Continue selected Windows B02/B03/B06/B09/B10/B11/B12/R01/R02/S04/S05/S06 without waiting for physical Windows testing. Keep all 134 IDs and the reviewed AEAD envelope. Synthetic data only; public release/main merge/new authentication remain outside scope.

## Data and identity
Payload schema 3 adds encrypted device UI records: random local UI profile UUID, dark mode, font size and view scale, and memo/calendar/clock window layouts. Layout stores note UUID or widget kind, monitor name, normalized monitor-work-area position, DIP size, recorded DPI, open/topmost/opacity/fold/position-lock flags. Every field is bounded and required in v3 JSON. Reject unknown note references, duplicate device/window records and non-finite/range-invalid values before any state change.

The UI profile is a non-secret random UUID in a fixed-size per-account local identifier file, separate from portable vault data, so copying a vault does not impose another PC's positions. It is not a registered sync device credential and grants no access to content. Root/ancestors must pass the existing local/no-reparse guards. Creation is create-new temporary+flush+non-overwriting rename; concurrent creators read the winning identifier. Malformed existing identifiers are refused rather than overwritten or silently switching profiles. Tests use a synthetic explicit profile and temporary identity root, never the connected user's files.

Schema1/2 load without disk mutation and normalize in memory; first v3 save keeps the exact previous ciphertext through existing expected-base/atomic replacement. Unsupported versions block writes. Layout/preferences changes dirty the encrypted snapshot but do not create content revisions. Existing folders/tags/history/trash and failure recovery remain intact. All UI device records stay local and are excluded from any future note-sync queue.

## Windows behavior
Reopen only the current profile's previously open non-trash memos after unlock. User-close records closed; security conceal closes native windows without changing the saved open intention. Track movement/size only after Loaded and while the same session is unlocked; callbacks after conceal/epoch change cannot mutate a new session. Explicit close/dispose waits for existing ciphertext I/O.

Use official monitor enumeration/monitor-info/window-DPI/native window positioning APIs. Save relative work-area coordinates and DIP dimensions. Restore to the same monitor when present; missing/changed monitor falls back to a visible current work area and clamps size/position. Embedded per-monitor DPI awareness is app-local. Actual mixed-DPI/multiple-display/hardware behavior remains unverified until the user's Windows test.

Topmost/opacity/fold/position-lock are independent of list pinning. Position lock prevents ordinary move/resize, not a security boundary against OS administration. Hide/show all affects unlocked native note windows, keeps data intact and does not defeat security conceal. Arrange uses visible work-area bounds.

Dark mode, global font size and UI scale persist per local profile inside the vault; no plaintext preferences containing note references. Date calendar and clock are local display widgets only, without schedule/to-do/reminder/account/network access. Lock closes them along with note UI. No web content, automatic links or telemetry.

## Verification
Test-first Core fixtures: strict required v3 fields, malformed device/layout IDs/ranges/NaN, two profiles isolation, actual encrypted restart and schema2 migration exact previous-byte preservation, layout-only save leaving note revision/history unchanged, failed mutation/snapshot budget invariants, local identifier create/restart/concurrent/corrupt/reparse cases, hidden recovery full devices preservation.

WPF control tests: move/resize/store/reopen synthetic windows on the runner's monitor, explicit user close versus conceal, theme/font/scale application, hide/show/arrange and widget close/lock behavior, repeated unlock and late callbacks. Keep compile, control execution and IME/physical-monitor/OS-event/user acceptance evidence separate. Request independent design/code review before implementing storage extension. This unit is not the final stopping condition.

## Implemented checks before Windows CI
Core implementation stub failure was observed before schema3/identity implementation. Fresh single-node Release build has zero warnings/errors and the complete synthetic Core suite passes, including authenticated required/type/unknown/duplicate/range/count UI fields, exact v2 migration/fault preservation, UI-only timestamp/history invariance, dirty-content basis separation, payload-budget mutation refusal, hidden recovery, concurrent/corrupt/symlink identity cases and deterministic negative-origin/small-monitor/DPI geometry.

Independent read-only review identified opening-intent loss before Loaded, oversized provisional placement choosing the wrong monitor, and arrange keeping oversized HWND size. Source fixes and dedicated WPF checks have been added. Open intention is preflighted before Show; provisional/final/arrange native rectangles fit the work area. History and clock honor preferences. Both app and WPF-check host embed the same PerMonitorV2 manifest; native HWND awareness is asserted using [GetWindowDpiAwarenessContext](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdpiawarenesscontext) and [AreDpiAwarenessContextsEqual](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-aredpiawarenesscontextsequal). Actual new Windows checks have not run yet. The reviewer has not independently executed tests.

Actual Windows outcome: commit a91ca5e, run37587829442 succeeded with the complete Core/WPF/native-awareness/control suite and publish. Physical mixed-DPI, monitor disconnect, IME, SessionLock and ACL acceptance remain untested; see VERIFICATION.md. This unit is not the final stopping condition.
