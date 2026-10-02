# 단계 1 첫 암호 메모 흐름 — 구현 전 독립 검토 요청

기준: be6dbed, cloud `/workspace/memo-app`, `development/stage-1`. 사용자 최신 요청으로 단계 1 개발 승인. 단계 0 원문/134개 범위와 M06 해석 유지. 작성자 자체 검토만 수행, **독립 검토 결과 없음**. 이 문서의 암호/키/파일 저장 코드는 아직 구현하지 않는다.

## 첫 수직 단위
A01/A02/A04/A06/A07/A08/A10/C14, B01/B13, S01/S02. Windows 관리창 왼쪽 분류/오른쪽 메모, 제목/일반 본문, 별도 포스트잇이 같은 편집 상태를 공유한다. 잠금/해제, 변경 후 자동 저장, 저장중/실패/저장됨, 재실행 복원까지 연결한다. 제목·본문·시각·메모 목록·기기/리비전 자료 모두 암호 저장. 고급 서식/삭제/폴더/검색/백업 UI는 다음 단위이며 최종 범위에서 빠지지 않는다.

## 검토를 요청하는 구체안
1. 처음에는 DB 대신 단일 암호 snapshot+암호 이전본을 사용한다. 전체 vault를 하나의 트랜잭션으로 저장한다. 문서/첨부 리비전별 저장 확장은 이후 독립 작업. 시험판 제한은 파일 16 MiB 및 최대 100개 일반 텍스트 메모로 명시하고 한도 초과를 오류로 처리한다. 이것은 최종 범위 축소가 아니다.
2. BCL RNG로 vaultKey와 snapshot DEK 각 32 bytes 생성. snapshot마다 DEK를 새로 만들고 AES-256-GCM(12-byte nonce, 16-byte tag)으로 payload 암호화. DEK는 vaultKey로 AES-GCM wrap. nonce는 매번 RNG 12 bytes, 동일 vaultKey wrap은 최대 2^20회 이후 회전/중단이 필요하다. 사용 한도·백업 replay·다중 기기 nonce 문제는 검토자가 확인해야 한다. 키/nonce 독자 알고리즘 금지.
3. 첫 시험판의 다른 PC 복구/이동은 **무작위 32-byte 복구 비밀**을 사용한다. base64url로 사용자에게 한 번 표시하고 사용자 확인으로 생성 완료. 이 비밀로 vaultKey를 AES-GCM wrap하며 파일에 평문 비밀/vaultKey를 저장하지 않는다. 비밀번호 기능은 미구현으로 표시; 약한 비밀번호를 복구 키처럼 쓰지 않는다. Argon2id 라이브러리·벤치마크 전까지 비밀번호 기반 보호를 넣지 않는다. 비밀을 잃으면 복구 불가.
4. Windows 자동 계정 접근은 Microsoft ProtectedData CurrentUser(DPAPI)로 별도 vaultKey wrap하는 방안. 공식 패키지 버전/라이선스/보안 상태 확인 후 최소 의존성으로 추가. 복구 비밀 경로가 있으므로 DPAPI가 유일한 복구 수단이 아니다. Hello 및 유휴 잠금은 별도 실제 Windows 검증이 필요하다. DPAPI 없이 비밀을 설정파일에 저장하는 대안은 금지.
5. plaintext JSON payload: schemaVersion/deviceId/notes(noteId, revisionId, parents, UTC created/modified, title, plain text, device-only scope) + revision history/tombstone 준비 필드. 서식 문서는 아직 생성/편집하지 않고 미지원 schema/mode는 **쓰기 금지**로 처리한다. 무손실 모델을 완성했다고 주장하지 않는다.
6. envelope version/vaultId/snapshotId/sequence/algorithm IDs는 고정 순서·길이의 바이너리 AAD로 인증한다. recovery wrap AAD에 vaultId/version/purpose, DEK wrap 및 payload AAD에 vaultId/snapshotId/sequence/purpose를 넣어 용도 교체·다른 파일 key mixup을 거부한다. parse 전 magic/version/최대 길이, unwrap/auth 후 JSON version/UUID/관계/필드 길이를 검증한다. 임의 GUID 파일경로 없음. 알 수 없는 envelope/schema는 현재 파일을 수정하지 않고 차단.
7. 저장: 사용자 AppData의 전용 vault dir(실제 자료는 repo 밖) → 동일 dir 임시파일을 restrictive 접근으로 생성 → **암호문만** 쓰기 → Flush(true) → File.Replace로 main을 교체하면서 암호 previous 보존. 첫 생성은 Move. failure injection으로 temp/write/flush/replace 실패 및 프로세스 종료 전후 시험. current 성공 교체 전 저장됨을 표시하지 않고 기존 메모를 삭제하지 않는다. OS/디스크 write cache의 완전 내구성 보장은 별도 한계다.
8. load: main 형식/키/AEAD/schema 전체 성공 전 UI에 메모를 공개하지 않는다. main 변조/손상 시 previous가 있다는 사실만 안내하고 자동 overwrite/복원은 하지 않는다. 복구는 별도 사용자 동작으로 사본 검증·원본 보존 후 적용한다. 유효한 오래된 파일 전체 replay는 AEAD만으로 검출하지 못하므로 이 한계를 기록한다. sync 삭제 부활 해결은 stage3 epoch/tombstone 검증 전에는 완료가 아니다.
9. 앱/OS 잠금: 편집 입력 중지, 저장 성공 확인(실패면 실패 상태 유지 후 사용자의 처리 선택), 포스트잇/목록/제목/본문을 비우고 키 접근 종료. 화면만 가리는 가짜 잠금 금지. 로그/오류에 title/body/key/path 내용 없음. managed memory/관리자/감염된 OS·해제된 화면 완전 보호 불가.
10. 통신/외부 API/텔레메트리 없음. 테스트는 합성 메모/임시 디렉터리·런타임 생성 키만 사용, 성공/실패 모두 키를 출력하지 않음. 테스트 후 파일 삭제, repo/CI bundle에는 사용자 vault/keys 없음.

## 예정 핵심 파일과 책임
- `src/MemoApp.Core/Editing/NoteDraft.cs`, `EditingWorkspace.cs`: 세션 중 편집·공유 상태, 저장 실패/변경 버전 관리. 암호/파일/통신 없음.
- `src/MemoApp.Core/Storage/EncryptedVault.cs`: 검토 후에만 envelope/AEAD/key wrap/파일 트랜잭션 구현.
- `src/MemoApp.Windows/KeyAccess/WindowsKeyAccess.cs`: 검토 후에만 DPAPI wrap/복구 키 UI 연결.
- `src/MemoApp.Windows/MainWindow.xaml(.cs)`, `StickyNoteWindow.xaml(.cs)`: 편집 세션 투영 및 잠금/실패/재실행 UX.
- `tests/MemoApp.ContractChecks/EditingChecks.cs`: 독립 메모리 편집/다중 창 상태 시험.
- 검토 후 `VaultChecks.cs`: 실제 암호 파일 새 인스턴스/잘못된 키/변조/비정상 종료/쓰기 실패/평문 누출 시험.

## 검토 질문
복구 비밀만 사용하는 초기 경로·DPAPI 역할이 위협 모델을 만족하는가? 랜덤 nonce/DEK wrap 사용 한도·AAD/파서가 적절한가? snapshot 교체/previous 보존·rollback 한계가 첫 시험판에 충분한가? 첫 실제 저장 전에 수정해야 할 항목과 허용 범위를 기록해 달라.

검토 결과란: 대기. 부모 작업의 별도 검토 결과/날짜/기준 commit을 받은 후 문서에 기록하고 승인된 설계만 구현한다. 이 결과는 사용자 개발 승인을 다시 요청하는 절차가 아니다.

## 독립 설계 검토 결과 — 2026-10-02
부모 작업이 별도 검토자에게 54ae10c 설계 문구를 전달해 독립 검토함. 범위는 설계 텍스트만, 코드 열람/실행/감사 아님. 조건부 구현 진행 허용. 작성자는 이를 독립 코드 검토/Windows 검증으로 주장하지 않음.

필수 반영 조건: 즉시 모든 창 비노출/입력·늦은 async 차단; 실패 잠금 상태 분리; DPAPI 같은 계정 접근과 독립 앱 인증 구분 및 자동 접근 기본 off; 단일 writer·직렬 저장·generation/sessionEpoch·base snapshot 검사; corrupt main 복구에서 정상 previous 보존 및 replace 실패 후 후보 분류/쓰기 중단; CSPRNG nonce와 키별 2^20 wrap 한도(실패/재시도 포함), rollback 한계; 정확 32-byte 복구 비밀 재입력/다른 계정 없이 복구; envelope/JSON 엄격 길이·깊이·중복·관계·EOF 검사. 변조/절단/쓰기·flush·replace 실패/process crash/동시 writer/잠금 race/복구/평문 누출을 실제 시험.

구현 범위 구체화: 첫 버전 DPAPI 자동 접근은 아예 구현하지 않고 off/미제공으로 표시한다. 해제·재시작 해제는 32-byte 복구 비밀만 사용한다. Hello도 미제공/미검증. 외부 패키지 0개. 복구 비밀의 생성 후 정확 재입력 확인 전 파일 생성 금지. 암호 current/previous에는 recovery-wrapped vault key가 들어가 파일 복사+비밀로 복구 가능.

writer 세션마다 새로운 vault key/epoch를 생성해 이후 snapshot에 recovery key로 wrap한다. 실패/재시도는 현재 세션의 wrapCount를 누적하고 한도에서 쓰기를 중단한다. 인증된 카운터의 rollback/프로세스 종료 시 실패 시도 소실은 증명 불가임을 기록하며 세션 재개 때 fresh key epoch를 사용한다. 명시 복구도 fresh epoch로 새 snapshot에 전환한다. AEAD는 유효한 과거 전체 snapshot replay를 검출하지 못한다.

독립 검토 근거: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope , https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew , https://tsapps.nist.gov/publication/get_pdf.cfm?pub_id=51288 .
