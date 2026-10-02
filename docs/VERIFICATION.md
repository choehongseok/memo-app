# 단계 0 검증 기록
2026-10-02, cloud Linux x86_64, Python 3.12.14. gh GraphQL repo view/REST branches 모두 Forbidden; 우회 없음. Microsoft SDK 10.0.401(runtime 10.0.12) SHA512 검증 설치. 직접 제품 NuGet 패키지 0개. 기준 HEAD 없음.

## 실행 결과 — 코드 commit `1bb83db62676eea447881a7e574a187a6a708d42`
검사 SDK 실행 경로는 `/workspace/memo-tools/dotnet/dotnet`, `DOTNET_CLI_HOME=/tmp/memo-dotnet-home`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`. 아래 명령의 dotnet은 이 SDK를 뜻한다.

| 명령 | 환경/결과 | 증명 범위 |
|---|---|---|
| `python3 tools/verify_preparation.py` | Linux PASS | 원문 27,417바이트/SHA256, 134 ID/이름, M06, 상태/범위 |
| `python3 -m unittest discover -s tests -p 'test_*.py' -v` | Linux 6/6 PASS | 실제 누락/중복/이름변경/M06 변경/거짓 완료 입력 거부 |
| `dotnet run --project tests/MemoApp.ContractChecks -c Release` | Linux PASS | 메모리 JSON 계약의 한글/표/첨부참조/미지원 노드 보존 |
| `dotnet build MemoApp.slnx -c Release` | Linux 교차 빌드 PASS, 0 warning/0 error | Core·검사·WPF 컴파일만, Windows 실행 아님 |
| `dotnet list MemoApp.slnx package --include-transitive` | 세 프로젝트 모두 No packages | 직접/전이 NuGet 제품 패키지 없음; SDK/참조팩 별도 |
| XML/YAML parse 및 workflow trigger/runner 검사 | Linux PASS | 구문과 설정만, runner 실행 아님 |
| `git diff --cached --check` | PASS | staged 공백 오류 없음 |
| `python3 tools/check_commit_safety.py` | PASS | 추적 파일 확장자/대표 비밀 패턴 휴리스틱, 전문 감사 아님 |

커밋 이후 동일 코드에서 공통 검사/전체 빌드를 다시 실행하여 인계한다. Windows 실실행 결과는 여전히 없음.

첫 계약 검사 exit 134: JSON의 공백까지 GetRawText로 비교한 검사 오류. 진단은 IDs/리비전/참조/scope 동일, raw=False·semantic=True를 확인했다. 수정은 JsonElement.DeepEquals로 구조·값 비교하며 title/mode/device/schema/deleted도 검사한다. 내용 보존 수용 조건을 낮추지 않았다. 수정 후 재실행 PASS. 실패 시 시험 내용은 합성 자료만이며 개인정보/키 없음.

한 차례 실행 transport 연결 끊김으로 긴 파일 생성 명령은 시작되지 않았다. 재접속으로 파일 목록 확인 후 누락 파일만 생성했고 명령은 다시 정상 실행됐다. 제품 자료 손실 없음.

빌드 파일은 `src/MemoApp.Windows/bin/Release/net10.0-windows/MemoApp.Windows.dll` (컴파일 골격). 설치 가능한 Windows 제품 패키지/실행 검증 근거로 제출하지 않는다. Windows CI 설정 존재는 실행 성공 근거가 아니다.

## 미실행/차단
- Windows runner: GitHub 접근 차단으로 실행/설정/한도 미확인. GUI/Hello/한글 IME/다중 모니터 미실행.
- Android APK/에뮬레이터/실기기: SDK/adb 없음, 단계 4 구현 없음.
- 저장/재실행/비정상 종료/디스크 실패, 암호/잠금/백업/복구/이력/동기화: 제품 구현 없음.
- 로컬 OCR/음성/AI 모델/추론/네트워크 관찰: 단계 5 구현 없음.
- S메모 형식/수입: 공개 백업 안내만 조사, 내부 포맷 fixture 미확보.
- 독립 보안 검토/전문 감사 없음. 설계 자체 검토만 수행, 구현 전 별도 검토 필요.
- 원격 commit/push/PR, 설치형/포터블 완성 패키지/APK/배포 서명 없음.

## 단계 1 독립 편집 단위 — 코드 commit `0c63a0a0cba23a37a3febf8993df2ee70d933d88`
- 사용자 재요청에 따른 gh repo view 단 1회: Forbidden. 추가/대체 접근 없음. 원격 작업/Windows CI 보류.
- 편집 시험을 먼저 작성하고 미구현 CreateNote에서 exit 134 NotImplementedException을 확인했다. 실제 NoteDraft/EditingWorkspace 구현 후 동일 시험 PASS.
- `dotnet run --project tests/MemoApp.ContractChecks -c Release`: 기존 계약 왕복 + 실제 새 메모/다중 편집 참조/한글 제목·본문/수정 시각·버전/변경 이벤트/다른 메모 독립성/동일값 중복변경 방지/세션 Clear 후 기존 참조 편집 거부 PASS.
- `dotnet build MemoApp.slnx -c Release`: Linux WPF 교차 빌드 PASS, 0 warning/error. MainWindow·StickyNoteWindow 바인딩을 컴파일했으며 실제 화면/GUI/IME 실행을 시험한 것은 아니다.
- 원장 검사는 단계 0 전용 상태 강제에서 단계별 상태 허용으로 바꿨다. 근거 없는 검증됨 거부/사용자 미수용/M06 정규화/134 ID·이름 유지 검사는 보존. 6개 Python 회귀 및 원문 checksum 대조 PASS.
- 기본 제품 실행은 작성 차단. --editing-preview는 명시적 비영속 화면 평가 모드, 저장·암호·재실행 복원 시험판이 아니다.
- 암호/키/파일 저장/실제 복구는 아직 구현 없음. STAGE1_SECURITY_REVIEW.md 독립 검토 결과를 기다린다. 독립 검토 수행을 주장하지 않는다.

## 독립 설계 검토 조건 반영 후 첫 암호 저장 단위
2026-10-02, cloud Linux x86_64 / SDK 10.0.401. 부모가 별도 검토자의 설계 문구 검토 결과를 전달함(54ae10c 기준, 코드 열람/실행 아님). 조건은 STAGE1_SECURITY_REVIEW.md에 기록. 전문 보안 감사/독립 코드 검토 완료를 주장하지 않음.

`dotnet run --project tests/MemoApp.ContractChecks -c Release` 실제 동작 PASS:
- 암호 파일 생성/편집 snapshot/새 인스턴스 복원, 잘못된 비밀/재입력 불일치/잘못된 43자 인코딩 거부, DPAPI 없는 파일 복사·다른 임시 root 복구.
- envelope 19개 영역(AAD/header/nonce/wrap/tag/payload) 변조와 7개 절단, trailing/16 MiB 초과 파일 거부. 인증된 중복 JSON property·지원하지 않는 mode·null history·integer overflow·깊이 초과 거부. 중복 UUID·잘못된 부모·cycle·live/tombstone collision·메모/제목/본문 한도 거부. wrap/sequence 한도 쓰기 차단.
- 실제 파일 create/write/flush 전후/replace 전후 오류 주입, 결과 후보 3개 분류 및 이후 쓰기 중단. 외부 base 변경 자동 overwrite 거부. corrupt/missing main 명시 복구에서 기존 정상 previous 파일 바이트 보존.
- 별도 프로세스 writer 거부, flush/replace 전후 4개 child 프로세스 실제 Kill. Linux exit 137 확인. main에서 기존 또는 새 인증 snapshot 복원 가능. stdout/stderr의 합성 내용/복구 비밀 미노출 확인.
- current/previous/pending 파일의 합성 평문 본문/복구 비밀 raw bytes 누출 검사. 실패 보류 암호문 사본이 실제 해독 가능한지 확인. 로그 파일을 생성하는 제품 코드는 없음.
- 저장 I/O를 실제 pause한 동안 즉시 Conceal/입력 참조 폐기 및 키 해제, session epoch로 늦은 UI 갱신 차단, 이전 generation 저장 완료가 새 dirty를 없애지 않는지 확인. 직렬화한 최신 변경 재실행 복원 및 암호 이력 보존. 잠금 중 쓰기 실패는 암호문 보류·키 종료, 암호화 전 실패는 숨겨진 평문 복구 대기·키 보유로 구분. 정확 비밀로 숨겨진 편집을 재개하고 수정/저장/잠금 왕복.

`dotnet build MemoApp.slnx -c Release`: Linux 교차 빌드 PASS, 경고/오류 0개. Windows 생성/해제/편집/포스트잇/자동 저장/잠금/후보 검사·명시 복구/암호문 사본 화면을 컴파일했음. Windows 실행, GUI 바인딩·IME·OS lock event·다른 Windows 계정·ACL 동작은 미검증.

시험 중 실패: 프로세스 실행기가 apphost에도 dll 인자를 추가해 child가 worker 분기로 들어가지 못하고 timeout. 자신이 생성한 worker tree를 종료한 뒤 host 종류별 인자를 수정, 동일 4 crash 시험 PASS. ReadOnlyObservableCollection 이벤트는 INotifyCollectionChanged를 통해 구독하도록 컴파일 오류 수정. Windows Path using 누락 수정 후 교차 빌드 PASS. 검사를 제거하거나 수용 조건을 낮추지 않았음.
