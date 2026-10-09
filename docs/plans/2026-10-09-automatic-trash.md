# D11 암호 백업 후 휴지통 자동 비우기 Implementation Plan

> **For agentic workers:** use superpowers:executing-plans inline; one independent storage review precedes implementation. Existing development authorization supplies execution approval; do not restart whole-product design.

**Goal:** 사용자가 현재 해제 세션에서 명시적으로 켠 정책에 따라 오래된 휴지통 메모를 새 암호 백업을 만든 뒤 현재 자료에서 제거한다.
**Architecture:** 기존 schema1..7의 정확한 bytes/거절 규칙은 유지하고 schema8에 NoteId/RevisionId/Parents만 갖는 제한 contentless revision witnesses를 추가한다. 제거 대상의 현재 본문·이력은 새 암호 백업에 보존하고 현재 snapshot에서는 본문/이력 내용을 제거하며 원래 삭제 tombstone의 ID/revision/parents와 전체 과거 revision DAG를 보존한다. 실제 object ciphertext/previous/pending/기존 백업은 자동 삭제하지 않는다. 정책은 기본off·잠금/재실행off·현재 세션만이며 7/30/90일 선택과 외부 로컬 백업 폴더/100개 cap을 명시 확인한다.
**Tech Stack:** 기존 .NET10/WPF/Core/SaveCoordinator.BackupAsync, 새 외부 서비스/의존성 없음.
**Spec:** docs/SOURCE_PROMPT.txt D11/M01..M05/P05..P06, docs/FEATURES.json D11, docs/PRODUCT_SPEC.md 보안·복구/동기화 계약.

## Global Constraints

- 원문27417B/hash·134ID·M06·기존 serializer1..7/암호 포맷을 보존한다.
- 실제 사용자자료를 지우지 않는다. 구현/CI는 합성 자료만 쓰며 제품 삭제는 explicit opt-in 뒤에만 수행한다.
- 자동 비우기는 secure erase가 아니다. 암호 previous/백업/객체가 남음을 안내한다.
- contentless tombstone100 한도는 유지한다. 무승인 삭제기록 GC/동기화 해제/키교체를 추가하지 않는다.
- 자동 백업/비우기 폴더·정책은 잠금에서 지우며 통신하지 않는다.

## Review Focus

- 백업 실패/가득 찬 폴더에서는 purge를 시작하지 않는다(실제 I/O fixture).
- 백업 중 복원·삭제 재진입·잠금 또는 기존 dirty 변경은 stale purge를 거절한다(version/epoch/result).
- 일괄 제거는 전체 candidate preflight 뒤 상태를 먼저 commit하고, observer 예외에서도 다른 제거 대상의 closure/clear를 끝낸다.
- metadata/window/recent reference를 제거하고 active note/history/첨부 object/root/source ciphertext를 그대로 보존한다.
- 시간대·동일 경계/미래 시각·기간 외 항목·재실행 정책off/백업복구와 삭제기록100 cap을 검사한다.

### Task 1: bounded atomic current-trash removal

**Files:** src/MemoApp.Core/Storage/VaultSnapshot.cs; src/MemoApp.Core/Storage/SnapshotSerialization.cs; src/MemoApp.Core/Storage/SnapshotValidation.cs; src/MemoApp.Core/Storage/EncryptedVault.cs; src/MemoApp.Core/Storage/AttachmentEnvelope.cs; src/MemoApp.Core/Editing/EditingWorkspace.Trash.cs; src/MemoApp.Core/Editing/NoteCollection.cs; tests/MemoApp.ContractChecks/AutomaticTrashChecks.cs; tests/MemoApp.ContractChecks/Program.cs.
**Interfaces:** consumes existing Capture()/VaultEnvelope.Validate()/AcceptPrepared() and NoteDraft.Close(); produces internal `Guid[] EligibleTrash(DateTimeOffset cutoff)` and `Guid[] PurgeTrash(Guid[] expected, DateTimeOffset cutoff)` on EditingWorkspace. The second validates exact current eligibility set and returns applied IDs; no filesystem/clock selection/backup authority is fabricated in Core.
- [ ] Add missing-method RED tests for strict UTC cutoff, exact current eligibility, original active note/history invariance, retained closed draft scalar clear, no removal before preflight, empty noop, invalid/duplicate/101 IDs, stale expected set refusal, contentless tombstones and no body history.
- [ ] Add silent unregister/reset publication to NoteCollection and implement complete candidate preflight before atomic basis/collection staging; preserve all opaque attachment objects. Every removed draft must close even if observer throws. Caller can identify applied IDs after notification error.
- [ ] Check existing exact schema1..7 serializer fixtures and encrypted save/reopen after purge; restore selected backup as fresh copy, preserve original deletion marker.
- [ ] Run targeted/full Core and record actual RED/GREEN; commit after independent pre-implementation storage review.

### Task 2: explicit session policy and actual backup gate

**Files:** src/MemoApp.Windows/MainWindow.AutomaticTrash.cs; src/MemoApp.Windows/MainWindow.xaml; src/MemoApp.Windows/MainWindow.xaml.cs; tests/MemoApp.WindowsChecks/AutomaticTrashUiChecks.cs; tests/MemoApp.WindowsChecks/Program.cs.
**Interfaces:** internal `ConfigureAutomaticTrash(string directory,int days,Func<bool> confirm)`; internal async `RunAutomaticTrashAsync(DateTimeOffset now,IAtomicVaultFiles? backupFiles=null)`; private `ClearAutomaticTrashViews()`.
- [ ] Write actual WPF missing-command RED; default off, cancel confirmation/picker, protected/network/link destination, 7/30/90 days only, lock callback/native setter stale configuration refusal.
- [ ] Add Korean controls beside backup settings; fixed default No confirmation describes body/history removal and encrypted copies retained. Hook existing timer after save/backup, one work at a time and daily/session attempt cap; no background work while locked.
- [ ] Capture scalar expected IDs/revisions/editversions before BackupAsync; require successful current backup, exact snapshot generations/epoch after await, then PurgeTrash and SaveAsync. Save failure truthfully preserves backup and dirty applied state; no retry silently discards copies. Independent backup/file cap stops before removal.
- [ ] Test actual encrypted backup contains removed rich/Markdown/plain plus source attachment/history; actual current saved state lacks note but keeps tombstone. Test failed/blocked backup, lock while backup, collection callback and current mutation, stale/native callback, restore fresh copy/restart defaultoff.
- [ ] Run Windows full gate once on changed final source; no intermediate artifact upload.

### Task 3: evidence, trial and continuation

**Files:** docs/STATUS.md; docs/VERIFICATION.md; docs/FEATURES.json D11; README.md; docs/USER-TESTS.md.
- [ ] Record actual source/run evidence and independent review separately; partial progress only, no whole completion/user acceptance claims.
- [ ] Explain current-session policy, backup capacity and retained copies/tombstone cap. Preserve remaining Android/sync/speech/AI scope and exact current blockers.
- [ ] Verify source134/M06/safety/diff, then continue independent remaining requirements; only a final authorized trial upload, no main merge/release.

## 독립 사전 검토 후 확정

- 기존 schema1..7의 contentless tombstone은 parents[]만 허용하고 history는 현재 noteID만 허용하므로 기존 포맷 안에서 purge 후 causal ancestry 보존이 불가능하다. 별도 백업은 현재 저장소의 causal evidence를 대체하지 않는다. 초기 새GUID/빈parents 안은 폐기한다.
- schema8 `StoredDiscardedRevision(NoteId,RevisionId,Parents)` 배열을 추가한다. 제목/본문/문서/metadata/첨부/시각은 포함하지 않는다. 현재삭제revision+해당note의 모든history 관계를 그대로 복사하고 기존 tombstone identity/parents를 변경하지 않는다. 기존 contentless legacy marker의 ancestry는 추정하지 않는다.
- witness는 tombstoned·현재note에 없는NoteId만 허용하고 각tombstone/witness latest의 동일GUID/parents를 검사한다. 전체revision GUID 중복·cross-note parent·missing parent·cycle·parents8·메모당513개(기존history512+current1)·전체history+witness10100개(기존history10000+삭제current100)·tombstones100·file16MiB를 candidate전체에서 확인한다. 현재 live note/history/root/opaqueobjects는 보존한다.
- 모든 schema1..7 writer에서 새field를 생략하며 JSON/typed validation은 예전 schema에 witness를 거절한다. schema8 명시 활성화 및 workspace/vault/hidden/backup/recovery downgrade guard를 추가한다. 새암호primitive/key/authsettings는 도입하지 않는다.
- 먼저 SaveAsync를 성공/dirtyfalse/권한동일로 정착한 뒤 ID/revision/editversion/epoch를 캡처하고 BackupAsync 이후 전부 다시 검사한다. dirty Capture 준비GUID를 안정된 증거로 쓰지 않는다.
- purge candidate/collection/reference state를 먼저stage하고 각제거draft.Close를 독립 시도한다. Changed 알림은 coordinator dirtygeneration을 반드시 올리고 기타 observer예외를 모은 뒤 보고한다. appliedID 판단과dirtybackup보존·자동반복금지는 UI에서 확인한다.
- 사전 독립 검토는 실제소스 읽기이며 구현/실행/전문감사가 아니다. 사용자에게 새 결정을 요청할 변경이 아니라 기존 삭제충돌·재접속 부활 방지 명세를 보존하는 저장 구현 판단이다.
