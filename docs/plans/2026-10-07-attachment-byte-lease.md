# Narrow revocable attachment-byte lease proposal

## Purpose and bounded next unit
Prepare an explicitly selected authenticated attachment for later image preflight/decoding without making ReadAttachmentBytes public or returning a public raw array. This unit enables no native decoder, preview, external opening/export, files, network, new key lifetime, new schema or image-node interpretation. Use synthetic inputs only. Existing root/nonce/3-wrap/anchor/backup/history rules remain unchanged.

## Recommended ownership contract
Keep decryption synchronous on the coordinator's existing single UI-context call path, as attachment sealing already is. Capture active coordinator/epoch, exact note membership/reference/EditVersion and immutable object; use existing private vault-owned decrypt, recheck authority, then transfer the owned array into a registered lease. Never access Workspace/NoteDraft from a worker or clone vault/root keys into I/O/decode workers. Bound creation to at most2 outstanding leases per coordinator, including revoked-but-running consumers: original plaintext at most8MiB. The later application-wide decode queue must admit before lease creation.

Public lease offers one synchronous Consume callback with a borrowed ReadOnlySpan and no raw-array/span property. It is single-use, does not retain the callback, and always zeroes its backing array in finally after the callback exits, including callback failure. Consumer code may run this narrowly owned callback on a worker, but it only sees its lease state/bytes and never live Workspace, note or keys. Creation and source-authority checks remain coordinator/UI-context work. No claim of enforced general thread-affinity or defense against malicious code already inside the process.

Consume returns success only if its grant remained valid through callback completion. A callback may produce an independently owned decoded result; later consumer/controller code must dispose that result if Consume returns false or source/generation/UI authority is stale. The lease alone grants no publication authority and cannot prevent copies made by already-authorized code.

## Revoke/dispose and races
Ownership transitions use a small lease monitor, never held through the callback. Enter checks valid/unused state and marks running. Lock/close/dispose/source-version change revoke authority immediately. If not running, zero immediately; if running, mark revoked and defer zero until its exclusive callback finally exits. Never zero an array concurrently while native/consumer code is using it. New consumes and repeated/reentrant consumes fail closed. Source refs/name/hash are not lease properties.

Coordinator Lock revokes all leases before native conceal and does not wait for a running consumer before hiding or releasing cryptographic keys. Do not wait solely for WorkspaceChanged, which can follow earlier public note notifications. The reviewed implementation must add an internal read-source invalidation boundary in NoteDraft Advance/StageEvent/Close before public notifications (or a reviewed atomic equivalent). Only internal revoke handlers may use it; they must not throw into model staging or invoke UI callbacks. This revokes grants when note/version/ref changes before a public callback can use a stale grant. Recovery/new sessions cannot revive old grants. A separate WhenAttachmentReadsIdle task describes buffer cleanup completion; it must not be folded into the UI conceal barrier or ciphertext-only save chain. Coordinator disposal revokes without leaving keys active; outstanding callback cleanup can finish later. Managed/native/OS copies or a callback that never returns are not promised erased or forcibly interrupted.

Unregister/drain notifications happen after releasing the lease monitor to avoid owner-gate/lease-gate deadlock. Registration and completion tracking use a separate owner collection monitor and never invoke external/UI callbacks while it is held. Define race-safe task replacement/completion and exact single cleanup/unregister behavior before code.

## Required RED-first evidence
- Missing lease API/runtime first; exact genuine object bytes, original ciphertext/refs/revisions unchanged
- No public raw buffer/span accessor, single-use/repeated/reentrant refusal, callback failure and exact backing-array zeroing
- Wrong/foreign/trash/closed/stale/unreferenced note refusal and capacity refusal before decrypt/publication
- Unused lease lock/source edit/detach/dispose zero immediately; no old grant after hidden recovery/new session
- Active consumer paused on a worker: lock/conceal/key release occur before its release, input is not concurrently zeroed, completion returns false and then actual buffer zero + idle completion
- Dispose/source revoke races and cleanup exceptions do not retain owner keys or publish late source
- Complete existing Core/crossbuild/original134/Python regressions; actual WPF/native decode/publication stays a later gate

## Alternatives and unresolved scope
An asynchronous decryption API could avoid UI compute latency, but adds worker key-use/capture/ownership races and is unnecessary for this first bounded lease. A public raw-array reader has no revocation/cleanup discipline and is rejected. Later native decode can still hang/crash and cannot be interrupted by this lease; exact decoder identity/queue/rasters/helper isolation need their own reviewed design and real Windows tests.

The independent pre-code review conditions below were incorporated before implementation. The bounded Core-only lease is implemented and covered by synthetic runtime checks; native decoding and H08 preview remain disabled. Performance and full physical/user acceptance remain unconfirmed.

The2-lease/8MiB plaintext cap is per coordinator. It is not an application-wide bound across repeatedly replaced coordinators with still-running callbacks; the later global admission/lifetime layer must cover old sessions too before any decode UI is enabled.

## Independent pre-code conditions incorporated
The review permits this Core-only unit conditionally after these exact corrections, without independent execution or native activation.
- Revoke is the source-transition linearization point, before changing source fields/version and before public notifications. Workspace forwards a trusted early NoteDraft hook for actual edits/StageEvent/Close; internal handlers cannot throw into staging or invoke UI. This covers restore/conversion/batch/attachment refs and direct Clear.
- AcceptPrepared is a bypass because it replaces roots/objects without note version changes. First implementation conservatively revokes all registered/pending reads before AcceptPrepared, including unchanged-save accepts. Avoid a weaker object-ID-only comparison. Validate the exact captured immutable object before decrypt and again before returning the lease.
- Lease states are Ready→Running→Finished with irreversible revoked. Revocation after Consume's monitored completion cannot retroactively change the returned boolean; later publication always needs a fresh UI/source check.
- Capacity reserves/counts before decrypt and rolls back every failure. A fresh RunContinuationsAsynchronously drain TCS is created only at0→1. Final cleanup captures that exact cycle under the tracker monitor, then completes it after unlocking; old cleanup cannot complete a newer cycle. Pending reservations and revoked-running readers retain capacity until actual cleanup. WhenAttachmentReadsIdle is a snapshot of the current cycle.
- Never call lease Revoke while holding the tracker monitor, or tracker unregister while holding the lease monitor. Snapshot then revoke; cleanup reaches a tracker-only object with no vault/key-use closure or live Workspace queries. Hidden recovery preserves tracking while old reads drain.
- Actual commit fault must revoke old grants before public Changed notifications even when note version remains unchanged; new creation remains blocked by existing fault authority checks.

Additional RED evidence: early public-notification consume refusal; AcceptPrepared effective replacement/removal with unchanged version; old-drain/new-admission race; two revoked-running grants refuse a third until cleanup; callback self-dispose/failure, duplicate dispose, batch/direct source-close/hidden-recovery no revival, pending-creation cancellation and fault revocation.

## Implementation checkpoint (2026-10-07)
The source-transition hooks precede every relevant NoteDraft assignment and public notification. AcceptPrepared and Clear revoke all reads, and actual commit failure revokes before public status changes. Pending reservations cannot be revived during Bind; both pending and running-revoked slots retain the two-slot cap until cleanup. The tracker captures the exact zero-count drain cycle under its monitor and completes it outside the monitor.

An actual-diff independent read found a live NoteDraft retention path through Slot.Source. The implementation now retains a fresh inert per-note identity token instead; only the UI-context coordinator maps a draft to that token. A production-owned field/closure graph test first failed on the live draft, then passed after this fix. Worker cleanup has no reference to the draft, Workspace, coordinator, vault, or key-use closure. Arbitrary caller copies and caller-installed task continuations are not covered by this claim.

RED-first evidence: missing CreateAttachmentReadLease failed at runtime. Targeted checks then cover exact authenticated bytes and zeroing, original snapshot invariance, no public raw accessor, callback failure/self-dispose/reentrancy/repeat refusal, stale/foreign/trash/detached sources, early staged notifications, accepted object replacement without a version change, paused source/lock/dispose consumers, two revoked-running slots, pending binding cancellation, old/new drain-cycle race, direct Clear, hidden recovery with no old-grant revival, actual flush fault before status callbacks, and vault release despite a public close callback exception. Core-only evidence remains separate from actual Windows CI and native preview.
