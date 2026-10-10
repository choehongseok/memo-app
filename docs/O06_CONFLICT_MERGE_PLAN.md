# O06 Offline Same-ID Conflict Preservation Plan

> **For agentic workers:** Implement task-by-task only after the independent preimplementation security review below. Use the execution method assigned by the parent; checkboxes describe future work and are not completion evidence.

**Goal:** Let a user explicitly retain a selected encrypted backup's same-ID live-note branches in the current vault's immutable history, without replacing the current note or automatically resurrecting deleted data.

**Architecture:** Reuse authenticated local backup reading, the current attachment-root authority, existing revision DAG storage, and atomic workspace publication. The first vertical keeps the current head, imports the selected causal history and source head under their original IDs, and displays durable pending branches derived from the stored DAG. It introduces no network synchronization, new crypto protocol, database format, or schema version.

**Tech stack:** Existing .NET 10 Core/WPF, encrypted vault envelopes, version-aware snapshot codecs and bounded local file workers. No new dependency/provider/account/payment/authentication enrollment.

**Spec:** `SOURCE_PROMPT.txt` O06/O07/O08/O09/P05/P06 and final clarification P01–P06; `PRODUCT_SPEC.md` recovery/backup and sync contracts; `FEATURE_GAP_AUDIT.md` O06/P05/P06; `plans/2026-10-09-backup-preview.md`, `plans/2026-10-09-selected-backup-recovery.md`, `H01_INLINE_IMAGE_PLAN.md`.

**Status:** Design-only future phase, separate from the parent's H01 Windows gate, H02 input and N02 PDF authority work. Source was inspected on 2026-10-10; no builds, tests, network calls, commits or uploads were performed for this plan. Independent preimplementation review is **not yet performed**. This document is author self-review, not independent review or security approval.

## Existing implementation and precise gap

- `EncryptedVault.BackupPreview.cs` authenticates the whole same-vault backup with `DecryptOwned`, disposes temporary root ownership, and returns a detached snapshot only to the Core import path. `ValidateImportedCandidate` authenticates imported objects with the actual current root key.
- `EditingWorkspace.ImportBackup.cs` implements O09 **fresh copies**: new NoteId/RevisionId, empty parents, live state and recovery timestamp; selected prior history is not imported. Existing current history/UI/objects/root are retained. Its organization/object conflicts fail before publication.
- `MainWindow.SelectedBackup.cs` binds preview to SHA256, selected IDs, session/source epoch and view lifetime; bounded workers receive path/token and ciphertext only. Save failure after publication retains the complete dirty batch.
- `VaultSnapshot.cs`/`SnapshotValidation.cs` support one current note per NoteId and immutable historical branches. History must belong to a retained note; every causal parent must exist for the same note. The validator does not require every historical revision to be an ancestor of the current head. This is sufficient for retained-live-note branch preservation, but does not itself label or resolve conflicts.
- D11 schema8 `DiscardedRevisions` are contentless immutable witnesses. `DiscardedEvidence.RequirePreserved` forbids recreating a note with a contentless tombstone's NoteId. Current trash and revision restoration create a fresh revision with the current revision as parent; they are not two-parent conflict resolution.
- Full encrypted transfer preserves the whole snapshot on a fresh local root and has separate synthetic tests. It is not a merge into an active workspace.
- Envelope `Epoch` is a random encryption writer/recovery-session UUID. It is **not** a monotonic recovery event, tombstone acknowledgment, or causal order. `Sequence`, timestamps and UUID ordering cannot safely rank deletion, restore or competing heads. No persisted recovery-event reconciliation model exists.

O06 remains partial; O09 fresh-copy recovery stays available under its existing name. P05/P06 remain unimplemented network-sync features. This plan neither restarts their approved design nor chooses a provider.

## Global constraints and first vertical boundary

1. Public/synthetic fixtures only. Never read real notes/backups or write keys, secrets, titles/content or private paths into repository/log/CI. Preserve original prompt bytes and all 134 IDs/M06 normalization.
2. Selected IDs must exist as retained **live** notes in both current and authenticated backup. A current or incoming deleted note, contentless tombstone, discarded witness for a selected ID, or backup-only ID rejects the whole selection. Report the unsupported category plainly; do not fall back silently to fresh copies, overwrite or resurrection. Unselected backup state is not applied.
3. Keep current notes byte-for-byte semantically unchanged, including RevisionId/parents/CreatedAt/ModifiedAt/content/exact rich SourceJson/metadata/attachment IDs. Preserve current DeviceId, all UiDevices/preferences/windows/recent/saved searches/automatic backup policy/root binding, tombstones/discarded evidence and all retained objects. Do not import backup device UI or rebind its local file-path-link profile IDs. Historical file-path links stay inert original metadata; backup recovery never opens their paths.
4. Same-ID notes require equal CreatedAt and Scope (currently only `device-only` is supported). A mismatch rejects rather than choosing one. A source head converted to `StoredRevision` loses no unique supported note field because these two values are checked against the preserved current note.
5. Existing bounds stay: 100 notes, 10,000 history records and 512 per retained note, 100 tombstones, 10,100 combined history/discarded records, 513 discarded witnesses per discarded note, 8 parents, 100 folders/tags, 16 attachment refs per record, 128 immutable objects, 4 MiB/object, 8 MiB retained attachment plaintext, 16 MiB encrypted file. No pruning to make merge fit.
6. Preserve exact frozen legacy schema1–10 interpretation/writing and existing envelope formats. A new merged snapshot can require a supported higher payload schema for imported features, but must never downgrade current state. Raise to 10 before validating incoming document-v2 history; never reinterpret opaque document-v1 JSON or rewrite raw rich/Markdown source.
7. Attachment-bearing imports require an already anchored current root with matching root ID **and successful actual current-key authentication** for every candidate object. A different root/key is rejected; no root adoption, re-encryption or key rotation. A legacy current vault without a root may import only attachment-free supported branches; this flow must not create a root or bypass H01's legacy opaque-reference refusal.
8. No image decode, external path/URL launch, OCR, thumbnails or plaintext temp/cache on preview/import/conflict comparison. Document-v2 references stay record-local, backed by that revision's AttachmentIds and authenticated immutable PNG objects.

## Deterministic branch-preservation contract

Construct a detached candidate from `Workspace.Capture()` on its owner context. Include **all** selected source revisions for each selected note, including historical side branches; do not import only its current head and then invent or omit its parents. Convert each selected source current head to its full-content historical representation when it is not already represented by the current note or history.

Compare global RevisionId identities before union. For an existing ID, require equal NoteId, ordered parent list, timestamp, title, text, mode, document schema and exact SourceJson, complete supported metadata (compare arrays structurally), and ordered attachment IDs. A matching head/history representation deduplicates; any mismatch rejects the whole operation. Do not use C# record equality on array-backed records as content equality. Cross-note revision collisions also reject. Do not manufacture a merge revision at import time.

Keep current head unchanged even if incoming is a descendant. An old incoming ancestor is retained/deduplicated history and never becomes current. An incoming revision not reachable by walking parents from current is a **pending branch**; compute the maximal tips of that nonancestor subgraph for display. This covers incoming-descendant and concurrent/incomplete-common-ancestor cases without claiming which is newer. Branch identity and content survive restart through existing history records, not plaintext markers or a transient UI flag. Historical ancestors of current are ordinary history, not pending conflicts. User edits of current keep pending branches visible until a later explicit causal resolution consumes them.

Only bring folder ancestors/tags/immutable objects referenced by selected incoming heads **and history**, together with their required dependencies. Equal existing IDs deduplicate; different meaning/parent/object bytes reject. Existing folder/tag name collisions under the current rules and any total budget excess reject. Retain all current unreferenced encrypted objects. Full backup validation happens before selection filtering, so hostile unselected objects are not a validation escape.

This first vertical preserves alternatives and offers bounded comparison; it does not offer automatic head promotion or a resolution button. Existing history restoration must be described as a new edit, not conflict resolution: it parents only current, so the source branch remains pending. Future explicit resolution needs a new revision whose parents include both current and the exact selected pending tip, complete predecessor preservation, and independent review; it must not be approximated by deleting a history record or changing the old RestoreRevision meaning.

## Ownership and proposed interfaces

Proposed interfaces are future contracts, not APIs claimed to exist. Final names can follow repository conventions; security boundaries and fields must remain equivalent.

- Create `src/MemoApp.Core/History/RevisionBranchAnalysis.cs`: bounded `PendingBranchTips(StoredNote current, IReadOnlyList<StoredRevision> history) -> Guid[]`, stable UUID sorting for display only. Inputs already pass full snapshot validation; output carries no plaintext or authorization.
- Create `src/MemoApp.Core/Editing/EditingWorkspace.MergeBackup.cs`: internal `PrepareBackupMerge(VaultSnapshot backup, Guid[] selected, Action<VaultSnapshot> validateCurrent) -> PreparedBackupMerge` and `ApplyPreparedBackupMerge(PreparedBackupMerge prepared) -> BackupMergeResult`. Candidate handle is detached/session-owned, never UI-owned; it stores the exact current fingerprint and complete validated candidate. Apply rechecks fingerprint, stages the whole history/organization/object batch, then accepts once and emits one Changed notification. No partial history union under callbacks.
- Create `src/MemoApp.Core/Editing/SaveCoordinator.BackupMerge.cs`: owner-bound `PreviewEncryptedBackupMerge(byte[] cipher, Guid[] selected, long expectedPreviewEpoch) -> BackupMergePreview`, and `MergeEncryptedBackupAsync(byte[] cipher, Guid[] selected, string expectedSourceSha256, long expectedPreviewEpoch) -> Task<BackupMergeResult>`. Preview exposes bounded scalar counts/selected IDs/pending-tip IDs only; it does not return snapshots, keys, historical attachment cipher or a caller-mutable prepared candidate.
- Preview authority must include exact workspace generation/content fingerprint **as well as** AttachmentPreviewEpoch/session epoch. The existing attachment epoch alone is not proof that a preview's whole current workspace is unchanged. Implementation may use an internal single-use owner token rather than a public fingerprint. Any edit, root preparation, lock, view close, reselection or callback reentry invalidates it.
- `BackupMergeResult` distinguishes NoChange, AppliedAndSaved, AppliedDirty and NotApplied; it includes imported revision count and pending tip IDs/count, never exception content. Do not infer applied state from changed NoteIds because this operation changes history under existing IDs.
- Create `src/MemoApp.Windows/MainWindow.BackupMerge.cs`; extend existing preview window with an explicitly separate **same-ID branch preservation** action and confirmation. Reuse `BackupFileReader` cipher-only workers, source SHA256 and view revocation. A small dedicated pending-branch list/compare view may reuse bounded `HistoryWindow` comparison; owner session/source generation must be checked after every native setter and close callback. Branch labels must not suggest timestamp ordering is causal ordering.

No changes to network Sync or new account/device enrollment interfaces belong in this phase. Keep O09's APIs and behavior unchanged.

## Prechange encrypted preservation and failure boundary

Before any apply, fully authenticate and validate the candidate; preview is strictly read-only. After explicit merge confirmation, checkpoint current dirty edits through the existing serialized SaveAsync path and require success. Revalidate the workspace/source/selection after every await. Capture the now-committed exact premerge ciphertext through the existing owned committed-copy API and persist a CreateNew encrypted premerge recovery copy before publication. Add a narrowly scoped internal storage helper for a fresh supported candidate name in the owned vault root; it must use the current writer authority, bounded cipher-only detached I/O, actual flush, and prohibit current/source/previous/pending replacement. Do not use a worker that closes over vault/session/root key. Preserve the premerge copy on later failure; no automatic rotation/deletion in this phase.

The internal checkpoint can legitimately advance persistence state and emit callbacks. After it completes, recompute/revalidate the prepared candidate against the exact same confirmed note/history state; a source change requires a new preview/confirmation. Do not carry a stale pre-checkpoint mutable snapshot across an await. Until publication, no incoming revision enters the workspace. Checkpoint/backcopy failure leaves current semantic content and dirty edits intact; persistence may have advanced solely to save existing user edits, and the result must say so honestly.

Atomic publication means every affected branch/dependency is installed and accepted before any observer notification; one Changed event and no intermediate history visible. After publication, save/observer/UI failure retains the complete merged dirty batch plus prior edits and reports AppliedDirty rather than pretending rollback. Lock before publication rejects; lock during notifications sees and freezes either the entire old workspace or the entire prepared batch. The branch/history view must revoke, independently clear and close before lock/session termination; native setter failure cannot skip clearing another plaintext field. Do not promise managed/native memory copies are perfectly erased.

## Required independent review before future implementation

`AGENTS.md` explicitly requires separate security review before storage/encryption/recovery/sync implementation. Assign an independent reviewer who did not author this design; record design reviewed, findings/disposition and whether any code or tests were actually run. Do not treat this self-review or prior O09/H01 reviews as O06 approval.

Review must settle: exact revision equivalence and collision behavior; pending-branch derivation across reopen/edit; history-only references/root-key mismatch; public DTO and owner-token lifetime; encrypted premerge checkpoint/backup fault semantics; lock/native callback publication; sticky schema1–10 compatibility; refusal of discarded/deleted/recovery ambiguities. Critical/Important findings block the affected implementation until addressed. This is the repository's review requirement, not a request to repeat user authorization.

## Future tasks and synthetic verification contracts

### Task 1 — Pure DAG union and pending-branch projection

**Files:** new `RevisionBranchAnalysis.cs`, `EditingWorkspace.MergeBackup.cs`; tests `tests/MemoApp.ContractChecks/BackupMergeChecks.cs`; runner route `--o06-merge-only` in its `Program.cs`.

- [ ] Write actual RED fixtures for A→B/current and A→C/backup; union stores A/C once, leaves B/current exact, reports C/pending after serialization/reopen. Incoming A→B→C preserves B/current and reports C/pending; incoming A/current B deduplicates with zero head change. Repeated same backup adds zero revisions and emits no workspace Changed. Imported history-side branch tips are included.
- [ ] Add RED collision cases: same RevisionId/different SourceJson whitespace or escapes, metadata/array/parent order/object ref/timestamp; different NoteId sharing RevisionId; CreatedAt/Scope mismatch; folder/tag same ID different meaning or name collision. Every rejection asserts exact whole before/candidate/source invariance.
- [ ] Implement structural identity comparison, bounded full causal union and stable pending-tip projection. Never rank by time/sequence/epoch. Candidate validation includes all parent references, cycles, limits and serialized payload budget before mutation.
- [ ] Verify target GREEN, including current edits after import and existing RestoreRevision leaving pending C visible. Verify current lineage/history remains byte-exact and no branch is silently pruned to meet 512/history/payload limits.

### Task 2 — Authentication, encrypted preservation and atomic application

**Files:** new `SaveCoordinator.BackupMerge.cs`, narrow internal helper in Storage; existing snapshot/envelope validation reused without loosening. Tests `BackupMergeLifecycleChecks.cs`, existing `SelectedBackupChecks.cs`, `WholeMigrationChecks.cs`, schema10/hidden checks as relevant.

- [ ] Write RED for same vault/root ID with a different actual root key; historical-only attachment and docv2 references; missing/nonlocal/non-PNG image reference; tampered/truncated/oversized/wrong-secret/other-vault/future-schema input; history-only immutable object collision. Reject before apply, preserve current key/root and all current objects.
- [ ] Write RED for current/incoming retained deletion, edit-versus-delete, legacy contentless markers, schema8 discarded witnesses and source revision colliding with a current discarded identity. Selected unsupported cases reject the entire batch; an unrelated current deletion/tombstone/witness remains exactly preserved in a successful live-note merge.
- [ ] Write RED for dirty checkpoint failure; backcopy create/flush failure; source/workspace change and lock while each await is blocked; reentry lock/throw during publication; merged save prepare/write/replace failure. Before apply, incoming data is absent; after apply, the entire merged batch and dirty edits remain. Premerge recovery copy decrypts to the exact confirmed current state and original backup bytes remain unchanged.
- [ ] Implement owner token/generation checks, full candidate current-root authentication, premerge encrypted preservation and observer-safe history publication. Require explicit applied result tracking; no NoteId-count heuristics.
- [ ] Verify actual frozen schema1–10 compatibility and exact prechange ciphertext, opaque-v1 SourceJson, Markdown raw source, immutable attachment bytes, history/docv2 references, current UiDevices/backup root-binding preservation, hidden lock recovery, restart and unchanged O09 fresh-copy behavior. Encrypted header epoch changes do not authorize head replacement or delete reconciliation.

### Task 3 — Windows merge action and durable branch comparison

**Files:** new `MainWindow.BackupMerge.cs`; existing backup preview/history views; tests `tests/MemoApp.WindowsChecks/BackupMergeUiChecks.cs` and runner dispatch.

- [ ] Write RED actual WPF cases for explicit distinct O09/new-copy and O06/same-ID actions; cancellation; SHA256 replacement; selection change; whole-workspace edit during confirmation/checkpoint; user close; lock during cipher read; slow preservation write; native setter reentry/throw. No stale view may publish plaintext or apply incoming history.
- [ ] Implement bounded preview counts and explicit confirmation that current stays current and imported alternatives appear as pending history branches. Reuse inert text comparison; keep automatic rendering, file launch and image decode off. Expose AppliedDirty accurately and prohibit duplicate/reentrant merge commands.
- [ ] Verify actual Windows synthetic same-ID merge→save→lock→reopen→pending comparison and current/source exact preservation. Linux cross-build is not Windows execution/user acceptance.

### Task 4 — Review, evidence and bounded completion claims

- [ ] After implementation, independently review changed storage/Core/UI boundaries and address Critical/Important findings, then run `python3 tools/verify_preparation.py`, relevant target checks and `dotnet run --project tests/MemoApp.ContractChecks -c Release`. Run the existing full Windows gate for the resulting source when assigned by the parent. Inspect diff/sensitive-data/artifacts and original 134-ID invariants.
- [ ] Update O06 evidence and `FEATURE_GAP_AUDIT` with actual commit/environment/result only. Keep user acceptance unconfirmed and P05/P06 unimplemented. Record deleted/contentless/backup-only selections, causal resolution, recovery-event reconciliation and network sync as independent remaining work. No upload/commit/publish is authorized by this document itself.

## Deferred contracts and important decisions

Full selected backup-only note/history import is useful but separate from this smallest same-ID vertical. Retained deletion/conflict branches, contentless discarded-ID restoration and recovery-event reconciliation require an explicit model compatible with D11: durable causal restore event versus fresh-ID restoration, preserved contentless evidence, and authenticated recovery lineage distinct from random crypto epochs. Review a schema extension only if required; preserve exact1–10 writers/readers and old-client write refusal. Do not relax discarded evidence retention just to make same-ID resurrection fit.

No new important user choice is needed for this additive preservation vertical: current content and all local device state stay authoritative, and unsupported selections fail visibly. Before a future phase permits replacement of current, resurrection of discarded IDs, deletion policy changes, or transport/account/provider enrollment, identify the concrete user decision then. Network P05/P06 will still require immutable change transport, operationId retry deduplication, device-only upload exclusion, key/device enrollment and revocation/epoch policy with a separate independent security review; an offline merge or whole-file overwrite is not that implementation.
