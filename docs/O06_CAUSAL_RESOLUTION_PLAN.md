# O06 Keep-Current Causal Resolution Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` for an assigned implementation unit. Parent-owned source/build coordination and independent security review precede implementation. Checkbox steps record actual execution evidence; no implementation is authorized by this document alone.

**Goal:** Let a user explicitly acknowledge one preserved maximal branch while keeping current content, recording a new current revision with exactly two causal parents.

**Architecture:** Resolve only an already authenticated, retained branch in the current vault. A clean checkpoint precedes a session-owned preview; a default-No confirmation authorizes an encrypted prechange copy and atomic candidate publication. Existing snapshot schemas 1–11 already represent the required DAG; no schema extension or network transport is needed.

**Tech Stack:** Existing C#/.NET 10 Core and WPF, SDK/BCL checks, current encrypted vault/committed-copy APIs; no new dependency.

**Spec:** `SOURCE_PROMPT.txt` O06 “백업 파일 선택 후 복구”, P05 “여러 PC 자동 동기화”, P06 “PC별 서로 다른 메모 일부 유지”; original final P01–P06 interpretation; `PRODUCT_SPEC.md` revision DAG, recovery/backup and sync contracts; `O06_CONFLICT_MERGE_PLAN.md` explicit deferred two-parent resolution contract.

**Status:** Independent preimplementation scope/security review completed with no Critical/Important finding on 2026-10-10. Parent authorized the bounded model/coordinator/WPF units within existing development scope. Core/model/owner implementation, focused/full Core checks, WPF source and crossbuild, and independent postimplementation source review are complete. Actual new Windows runtime evidence remains pending. The immutable previous Core DLL baseline for genuine API RED is 2,516,480 bytes, SHA256 `1603bc57be59738253c0bf60c3d9f0a303080873abb34376269d4a1d5e763f36`.

## Global constraints and scope fit

- This is the next bounded offline O06 vertical following same-ID branch preservation. It preserves both predecessors and gives an explicit local causal outcome; P05/P06 transport, device enrollment, encrypted change delivery, retry operation IDs and device-only upload exclusion remain unimplemented.
- Preserve the original source bytes, 134 feature IDs, M06 normalization, exact schema1–11 interpretation/writers, opaque document-v1/v2 source, envelope formats and existing crypto/key-use limits. No schema12, new event format or timestamp arbitration.
- A retained live current note and an explicitly selected live maximal pending tip for that same NoteId are required. Refuse current trash, selected deletion tip, discarded/contentless IDs, missing tips, nonmaximal tips, ancestors of current, foreign note IDs and unsupported scopes. No resurrection, root adoption or deletion-policy resolution.
- Preserve current DeviceId, scope (`device-only` is the currently supported scope), CreatedAt, title, body, mode, exact document SourceJson, every supported metadata value including note-scoped OCR/file links, ordered attachment IDs, organization, device settings/search/backup policy, all other current notes, immutable objects, tombstones/discarded witnesses and all previous history.
- Only the target current RevisionId, ordered parents, ModifiedAt and draft edit version change. Parents are exactly `[oldCurrentRevisionId, selectedPendingTipRevisionId]`, distinct and nonempty. Append one exact old-current historical record. The selected tip and its full content remain in history. Do not prune to fit a budget.
- Existing limits apply to the whole candidate: 100 notes; 10,000 history and 512 history records per retained note; 10,100 combined history/discarded; 8 parents; 16 attachment references per record; 128 immutable objects; 4 MiB/object; 8 MiB retained attachment plaintext; 16 MiB encrypted file. Schema11 additionally enforces 16 OCR records per note, 128 current/history occurrences, 1 MiB aggregate OCR UTF-8, 65,536 UTF-16 units and 262,144 UTF-8 bytes per OCR text. Duplicating old-current metadata into history consumes these existing budgets.
- No attachment decode, OCR, native document parser, path/URL launch, plaintext cache or network provider. Historical imported provenance remains historical facts; resolution does not mint a fresh recognition result.
- User-development authorization already covers this vertical. Routine implementation choices require no repeated approval. `AGENTS.md` still requires separate security review before storage/recovery implementation.

## Exact behavior and source authority

Opening durable branch comparison may checkpoint current dirty edits first, visibly and separately. Capture original session/workspace/note instance, complete note membership/versions, generation and source epoch before that await. Admit no resolution preview when checkpoint fails, edits change during it, host closes, keys end or clean writer settlement fails. After a successful checkpoint, take a fresh stable committed basis; do not fingerprint provisional dirty captures or save silently inside an already confirmed preview.

A preview is for one target note and one user-selected maximal tip. Initially no tip is selected and the resolution button is disabled; the existing comparison window's automatic first-row selection must not create authority. Bound displayed titles/excerpts to 256 UTF-16 units with valid text boundaries and disclose truncation, mode and attachment count. Rendering counts as displayed only after all native setters succeed and the host is still current. Selection changes revoke the prior token rather than rebind it.

Suggested action label: `현재 내용을 유지하고 선택 분기 해결`. Suggested confirmation: `현재 내용을 그대로 유지하며 선택한 보존 분기를 확인 완료로 기록합니다. 두 수정본의 내용은 이력에 남고, 다른 미해결 분기는 유지합니다. 적용 전 현재 암호문을 보존합니다. 계속할까요?` Use Yes/No with **No as the default**. Confirm only the exact displayed token after checking source and host state again; a native confirmation callback is a reentry boundary, requiring a second pure state check afterwards.

The selected source is the full historical revision already present in the authenticated current vault, identified by immutable NoteId/RevisionId and the complete clean-basis fingerprint. The former external backup file is no longer its authority: users may move/delete that file after preservation, and offline resolution must still work. Do not require or silently import from it again. Actual source authority comes from the current committed ciphertext, current writer identity/session and actual current-root-key authentication of the complete candidate objects.

Before publication, persist the exact clean prechange ciphertext using `EncryptedVault.CaptureCommittedCopy()` and `PreparedEncryptedCopy.WriteMergeRecovery` to a fresh `previous-{32 lowercase hex}.vault` name. The worker receives only the detached ciphertext owner and destination/file adapter; no vault/session/key/live draft/host delegate. Require CreateNew, flush and verified output. Retain a successfully written copy on later refusal; do not rotate it away in this unit.

After the preservation await, recheck the exact preview/session/whole workspace/host, then use a second `CaptureCommittedCopy()` and dispose it to verify that `current.vault` still equals the authenticated committed `lastKnownBase`. This is bounded existing owner-context I/O without a key-gate lock over the read; key release can proceed, and the helper checks authority after reading. Run `vault.ValidateImportedCandidate(candidate)` for full structure and actual current-root authentication, then recheck token authority. No new external-backup reader or recovery-key worker is needed. A disk race after this final check remains possible: the existing expected-base commit guard prevents overwriting changed ciphertext; postpublication save failure becomes AppliedDirty, preserving the complete in-memory candidate and the prechange copy.

Compute pending tips from graph ancestry. After success, selected tip and its ancestors become ancestors of the new current head. Other maximal pending branches remain pending unless actually reachable through the two chosen parents. Keep UUID sorting for display only; never choose by time, UUID order, excerpt, import order or apparent freshness. A descendant of old current still receives the two distinct explicitly requested parents, even though the first edge is transitively redundant.

## Proposed file inventory and contracts

| File | Ownership and narrow change |
|---|---|
| New `src/MemoApp.Core/Editing/EditingWorkspace.BranchResolution.cs` | Prepared whole candidate, strict maximal-tip eligibility, atomic publication, no key/file/UI access. |
| New `src/MemoApp.Core/Editing/SaveCoordinator.BranchResolution.cs` | Closed owner tokens/view, clean checkpoint, current-cipher checks, preservation/save lifecycle and outcomes. |
| Existing `EditingWorkspace.cs` / `NoteDraft.cs` | Minimal callback-free revision staging/accepted-version installation hook only; preserve all ordinary edit/history restore behavior. |
| Existing `SaveCoordinator.cs` / `SaveCoordinator.BackupMerge.cs` | Minimal resolution invalidation hook and shared recovery/publication admission; do not fork incompatible busy/owner policies. |
| New `src/MemoApp.Windows/MainWindow.BranchResolution.cs` | Explicit selection/preview/default-No confirmation and host/session lifetime binding. |
| Existing `BackupMergeWindow.cs` / `MainWindow.BackupMerge.cs` | Bounded durable viewer action with no initial selected-tip authority; independent clearing of every plaintext native field. Existing additive import and fresh-copy commands stay separate. |
| New `tests/MemoApp.ContractChecks/BranchResolutionChecks.cs` | Actual missing-API RED, DAG/candidate/auth/budget/failure/reopen contracts. |
| New `tests/MemoApp.WindowsChecks/BranchResolutionUiChecks.cs` | Genuine selection/display/confirmation/native reentry/lock tests. |
| Existing test `Program.cs` files | Parent-owned focused/full registration only. |

Proposed internal model APIs:

```csharp
PreparedBranchResolution PrepareBranchResolution(
    Guid noteId, Guid pendingTipRevisionId, Action<VaultSnapshot> validateCurrent);
bool ApplyPreparedBranchResolution(
    PreparedBranchResolution prepared, Func<bool> current, Action markApplied);
```

`PreparedBranchResolution` is closed, coordinator-owned and deeply detached: expected committed basis, SHA256 fingerprint, original target identity/version, old-current and selected-tip IDs, complete validated candidate, bounded remaining tips and a consumed flag. No UI or public snapshot/text factory can mint an applicable prepared handle. Reuse `RevisionBranchAnalysis.PendingBranchTips`, complete metadata structural equality, `MergeFingerprint`/stable-basis concepts and existing snapshot validators; do not change `RestoreRevision` to mean resolution.

Proposed owner/public APIs:

```csharp
BranchResolutionViewAuthority CreateBranchResolutionView(
    Func<bool> current, Func<bool> checkAccess);
Task<BranchResolutionPreview> PreviewBranchResolutionAsync(
    Guid noteId, Guid pendingTipRevisionId, long expectedPreviewEpoch,
    BranchResolutionViewAuthority view);
ConfirmedBranchResolutionToken ConfirmBranchResolution(
    BranchResolutionPreviewToken displayedToken);
Task<BranchResolutionResult> ResolvePendingBranchAsync(
    ConfirmedBranchResolutionToken confirmed);
```

The view/token constructors are closed to `SaveCoordinator`. View disposal revokes token authority and cancellation immediately. Each coordinator has a fresh session nonce; each preview has a fresh token/grant ID. Bind those identities, owner/view identity, actual note instance, captured NoteId/current RevisionId/exact selected tip, stable whole-basis fingerprint, generation/session epoch/preview epoch and genuine displayed token. A token from another host/note/coordinator/reopened session never becomes valid by copying IDs/version values. A confirmed token is single-use; consume/revoke on every attempted apply, including failed authority/source/budget checks. New selection/repreview, any relevant workspace change/AcceptPrepared, another recovery operation, lock, close or disposal revokes old confirmation. Cap active preview authority at one per coordinator and replace/revoke the previous preview explicitly.

Reuse `BackupMergeBranchComparison` as inert bounded display values if useful; it grants no authority. `BranchResolutionResult` carries `Disposition`, optional new RevisionId and bounded remaining tips; dispositions are `NotApplied`, `NoChange`, `AppliedAndSaved`, `AppliedDirty`. `NoChange` is restricted to a fresh owner-validated eligibility query where the exact selected stored revision is already an ancestor and no preview/apply token is issued; it has no backup/publication/Changed event. Missing/cross-note/nonmaximal/deleted/stale/token-reuse cases refuse or return NotApplied, never NoChange. A once-valid confirmed tip ceasing to be pending always invalidates that token; do not reinterpret its old confirmation as NoChange success. The resolution UI can simply disable ineligible actions instead of exposing NoChange.

## Atomic publication and settlement

Share the existing recovery admission (`IsBusy` plus a common owner recovery/publication guard) with branch import, selected restore and other writes; reject overlapping resolution/import/apply. A worker cannot reserve/release owner authority. Native owner access uses the trusted Dispatcher.CheckAccess policy, not SynchronizationContext identity.

Before invalidation, deep-own and validate the full candidate, verify retained discarded evidence/root, preallocate the complete replacement accepted-version dictionary, all predecessor/parent arrays and marker collections, and enumerate invalidation/observer handlers. New current parents are exactly ordered `[oldCurrent, selectedTip]`; old current becomes an exact history record. Prepare once, not during a callback. No validation, serialization, LINQ allocation, dictionary growth, cloning or capacity growth belongs in the final field installation.

Every live-authority test is pure state check → host predicate → pure state recheck. Bind complete original note membership/instances/versions, basis fingerprint, owner session/generation/source epoch and selected eligibility. Invalidation handlers run separately; after each, verify authority and openness again. Permit exactly one expected trusted coordinator revocation only during this particular prepared resolution's publication phase; unrelated invalidations/recursive attempts never borrow that permit. A throw, reentrant edit, AcceptPrepared, new/remove note, lock, selection switch, host closure or nested apply before installation leaves the old state intact apart from the separately authorized reentrant action.

Install all prepared draft/basis/history/schema/accepted-version state callback-free, then mark `applied` immediately before observers. Since content is preserved, do not invent a body/document conversion or increment a content-change counter merely to simulate text editing; edit/source authority still advances and attachment/OCR grants are revoked. Observers always see the complete new DAG. Observer failure/lock after installation, failed encryption/commit and externally changed committed ciphertext return AppliedDirty; never report a rollback or NotApplied after the applied boundary. Preserve the complete candidate for existing dirty/hidden recovery. Successful save/reopen returns AppliedAndSaved with the selected tip no longer pending.

Track the preservation task in existing coordinator settlement so lock conceals native text and revokes keys promptly, while close/dispose waits for actual ciphertext worker settlement before disposing its owner. Never invent a timeout as settlement. Early cancellation cannot make a still-running worker reusable; late write success retains the exact preserved cipher, without permission to publish a stale candidate.

## Review focus

1. Selected tips becoming ancestors/nonmaximal while preview or preservation is in progress: exact whole-basis authority must refuse stale publication (Task 1/2 tests).
2. Existing opaque documents, image references and OCR/file-link metadata duplicated into old-head history: preserve exact values and enforce whole historical budgets (Task 1 tests).
3. Native predicate/invalidation reentry and fallible allocations after the first field write: pure/callback/pure checks and preallocated whole installation are required (Task 1/3 tests).
4. Current ciphertext replacement and lock during prechange-copy I/O: source authority must end without incoming mutation; late cipher owner still settles (Task 2/3 tests).
5. Applied candidate followed by observer/commit failure or process restart: distinguish AppliedDirty, retain predecessors and avoid a second causal revision on retry (Task 2/3 tests).

## Task 1: Pure candidate and atomic model publication

**Consumes:** existing `RevisionBranchAnalysis.PendingBranchTips`, schema1–11 validators, exact rich/metadata preservation and stable committed-basis fingerprint.

**Produces:** `PreparedBranchResolution`, the two internal APIs above and callback-free revision staging.

- [ ] Write `BranchResolutionChecks` reflection missing-API tests against a pinned actual previous Core DLL, capturing DLL SHA256 and `/tmp/memo-o06-resolution-red.log`; use a direct DLL runner if other authors own Core source. Assert actual missing contract rather than treating a compiler error as RED.
- [ ] Add a synthetic A→B current and A→C pending fixture. Prepare one explicit C; assert original workspace untouched. After apply, new head content/metadata/document/refs exactly B, parents exactly `[B,C]`, exact B appended once, A/C retained, selected C no longer pending and unrelated D remains pending. A/B ancestors, an intermediate tip with a pending descendant, wrong note/current trash/deleted-tip/discarded/missing IDs and unsupported scope refuse unchanged.
- [ ] Add incoming-descendant case, multiple maximal tips with asymmetric times/UUID order, no common ancestor, and repeated/stale selected-tip identity; prove no automatic selection/arbitration. Preserve rich opaque v1/v2 SourceJson, plain/Markdown mode, file links/OCR metadata, immutable objects and other notes/device UI.
- [ ] Add exact 511→512 history success and 512→513 refusal; OCR current/history occurrence and aggregate overflow; otherwise-valid escaped full16MiB payload overflow. Assert whole byte-equivalent workspace on preflight refusal and no pruning/root adoption.
- [ ] Implement the prepared candidate and preallocated publication protocol above. Add current-predicate edit/AcceptPrepared/create/remove/clear, throwing invalidation, nested apply, observation of old fields during invalidation and full new fields afterwards. Test allocations/refusal before mutation, markApplied before observer failure, and no Changed for NoChange.
- [ ] Under the parent's coordinated build slot, build Core/ContractChecks and run `--branch-resolution-only`; capture real RED/GREEN build/test output. Request independent source review before owner/storage activation. Do not stage/commit/upload.

## Task 2: Coordinator, encrypted preservation and durable outcome

**Consumes:** Task 1 prepared APIs, existing authenticated current vault, `CaptureCommittedCopy`, `WriteMergeRecovery`, `ValidateImportedCandidate`, SaveAsync and actual settlement lifecycle.

**Produces:** owner/view/displayed/confirmed tokens, single-use resolution operation and explicit result disposition.

- [ ] Write owner-contract RED before the public APIs: two hosts, same IDs across reopened session, mutated/caller-fabricated tip, repeated confirmation/token reuse, foreign owner thread and selection/version/session/epoch changes must fail.
- [ ] Implement clean prepreview checkpoint with original request/version checks, one closed nonce-bound preview, actual displayed-token confirmation and shared recovery admission. Checkpoint failure/cancel yields no preview; confirmation default No performs no resolution or prechange copy. A dirty edit after preview requires a new preview, not an implicit save under old confirmation.
- [ ] Add real encrypted synthetic source fixtures and before/after cipher hashes. Resolve without requiring the former external backup file; prove the prechange `previous-*.vault` bytes exactly equal committed clean cipher. Verify full candidate under actual current root key and reject same root ID/different key. Replace `current.vault` before or during preservation or final check and assert no candidate publication when detected before installation.
- [ ] Inject CreateNew/flush/readback failure, slow copy, cancellation/host closure/lock during copy, throwing/stale predicate after await, and late write completion. Assert immediate authority/key/native concealment, eventual true settlement, preserved late cipher, no stale publication or worker access to coordinator/keys.
- [ ] Test observer throw/lock and encrypted prepare/commit failure after publication: AppliedDirty with exact two-parent complete candidate; retry/save/reopen retains the same new revision. Successful encrypted reopen shows current B content, exact B/C predecessors, selected C consumed by ancestry and D pending. Reuse or stale confirmed token creates no second revision. Preserve hidden recovery and all sticky schemas; no new schema fields.
- [ ] Run focused build/tests only in a coordinated slot, then independent security postreview. Parent runs consolidated fullCore and preparation checks once all sources pause. Keep P05/P06 status unimplemented.

## Task 3: WPF explicit local resolution

**Consumes:** Task 2 closed owner APIs and inert bounded comparisons; existing shared host/session concealment and synthetic native checks.

**Produces:** explicit one-tip selection, genuine displayed default-No confirmation and outcomes in durable branch viewer.

- [ ] Write actual WPF RED fixtures using the real comparison window and selection source. Initially no selected tip/button authority; choose a non-first tip explicitly, render its exact preview and confirm via an injected default-No-capable delegate. No confirmation callback runs before genuine displayed authority.
- [ ] Add the separate keep-current action, clear selected authority on selection/repreview, bind one host/session/target identity and disclose bounded excerpt/truncation. Keep existing same-ID preservation, fresh-copy recovery and ordinary RestoreRevision semantics distinct.
- [ ] Pin native setter throw/reentry during each field, selection edit during confirmation, nested view/resolve, workspace edit after display, source replacement/slow copy, cancellation, host close and lock. Assert no stale content publication; clear every plaintext field independently even when one setter fails, close/revoke immediately, and await actual cleanup settlement.
- [ ] Pin AppliedDirty message versus successful durable result, reopen comparison with selected tip absent/unselected tip present, original body/document/attachments/OCR exact, and no decode/path launch/network. Run cross-build separately from actual Windows runtime; record actual commands/source tree/results without claiming one from the other.

## Independent review gate and remaining decisions

Before production edits, the parent assigns an independent reviewer for exact original scope, two-parent DAG semantics and immutable predecessor preservation; selected live maximal-tip eligibility; current-cipher/current-key authority; source/view/session nonce and single-use binding; pure/callback/pure checks; shared nonreentrant admission and one revocation permit; callback-free preallocated publication; budgets/legacy exactness; actual worker settlement/key release; AppliedDirty/hidden/reopen behavior; WPF explicit selection/default-No and clearing.

No new important user decision is needed for this keep-current vertical: the parent explicitly selected its behavior, and each concrete causal acknowledgment is confirmed in the product. Choosing incoming content or a combined edit, acknowledging deletion versus edit, resurrecting discarded identity, changing recovery epochs, adopting a foreign root, or choosing/enrolling a transport provider would be a different important decision. Those operations remain outside this unit rather than triggering a repeated general design approval now.
