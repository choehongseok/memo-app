# 검증 기록

## 단계 0 기록
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

## 미실행/차단 (단계 0 시점)
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

## Windows x64 패키지 및 체크포인트
- Core/integration 검사 코드: ef5a8b71f3e05c8ae4da2c2330dba265e2a08f3f. 마지막 Core 변경 이후 모든 위 동작 검사 PASS.
- Window-level recovery 완료의 uiEpoch 가드/활성 복구 중 종료 보류 코드: 67fe9c5a777add6d3c4eda6821d7895472579ceb. `dotnet build MemoApp.slnx -c Release` Linux PASS 0 warning/error. 실제 Windows 이벤트 race는 미실행.
- `dotnet publish src/MemoApp.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/windows-x64-synthetic-trial` Linux PASS. SDK10.0.401/공식 NuGet runtime packs10.0.12, 직접 제품 PackageReference0.
- Windows AMD64 PE magic/machine 검사, zip CRC 및 vault/key/db/dump/runtime-user-data 파일 미혼입 검사 수행. Microsoft runtime/WindowsDesktop 라이선스와 third-party notices를 패키지에 복사. 추가 제품 라이선스/전문 취약점 감사 완료를 주장하지 않음.
- 원장 부분 검사 근거를 넣자 근거 없는 전체 검증됨 테스트가 실패. component/cross-build 근거를 full acceptance로 사용하지 못하도록 kind=feature-acceptance/PASS를 요구하는 검사를 추가. 기존 6 회귀 모두 PASS, 수용 조건 하향/검사 삭제 없음. 모든 관련 ID는 진행 중으로 유지.
- 원격 push/PR/Windows CI 실행은 Forbidden 차단 그대로. 실제 Windows 실행·GUI·OS 잠금·IME·다른 계정 복구·ACL·네트워크 관찰은 미실행. APK/실기기/모델 추론도 미실행. 실제 개인정보 사용 준비 완료 아님.

ZIP 결과: 63,192,131 bytes, SHA256 `744dec678cc0088b97056ab5511b7a7c4495b03650987464d430aa12480ff5a8`. CRC/PE AMD64/자료 파일 미혼입 PASS. Library 인계 저장 1회는 네트워크 오류로 실패. 해당 저장을 완료/접근 가능하다고 주장하지 않으며 재시도/다른 경로 전환하지 않음. cloud 로컬 파일과 검증 bundle 보존.

## 2026-10-06 Windows CI 복구
- 기존 run37503614819: Scope and safety checks에서 `원문 변경`, 나머지 build/계약/publish skipped. core.autocrlf=true 별도 checkout 재현: Git blob27,417 bytes/SHA256 29655ee9235ebcc3e056551e1aeaf3af26781f1b43a93b78cd585e285c48957d, checkout27,655 bytes/SHA256 b59c1e47fac393cd940b7a607a37cac7da5d15f2a798bac5a890aaf69bcafd65, CRLF238개. CRLF를 LF로 되돌린 bytes가 blob과 동일. 검사 기대값 변경 없이 .gitattributes에서 해당 원문만 -text 적용한 동일 checkout에서 원문/134 ID PASS.
- 7d13a12의 run37504500668: 실제 Windows blob/checkout 모두27,417 bytes/원래 SHA256 일치. 후속 `charmap codec can't decode byte 0x9d` 실패. UTF-8 원장 파일의 cp1252 decode에서 같은 종류 오류 재현. Python 검사와 회귀 파일 읽기에 UTF-8을 명시, 기존6 회귀/원장/민감정보/diff PASS. 제품 코드 변경/검사 완화 없음.
- a8915328d42db319504b4b457f58a64f8d454c7d의 https://github.com/choehongseok/memo-app/actions/runs/37504683119 성공(Windows Server2022, SDK10.0.401): blob/checkout 원래 byte/hash 일치, 134 ID/M06, Python6, 민감정보, build 경고·오류0, 모든 합성 계약 검사, self-contained Windows x64 publish 및 artifact 업로드. 계약 로그는 실제 암호 파일/복구/잘못된 비밀/writer, 변조19/절단7/엄격parser/파일오류/충돌/후보보존, 다른프로세스 writer/실제Kill4/평문·키 검사, generation/epoch/직렬저장/즉시잠금/실패 경쟁 PASS.
- Artifact ID11430249406,64,957,037 bytes,SHA256 5b079cee5e1071b4a0b9b6c488e75954e604f813168549cc667f783223e04b2c, expires2026-10-13. Windows GUI/IME/OS SessionLock/ACL/다른 계정 사용자 시나리오는 실행하지 않았다. 독립 코드 감사/전체134 기능 수용을 증명하지 않는다.

## 2026-10-07 코드116912f 조직/검색/휴지통/이력/백업
환경: 별도 cloud Linux Debian13 x86_64, Microsoft 공식 SDK10.0.401 SHA512검증설치, runtime10.0.12. NuGet 기본 cache는 read-only여서 /tmp 작업cache를 지정했다. 직접 제품 PackageReference 추가 없음.

- 원문134 ID/byte/hash/M06 검사 및 Python6 회귀 PASS
- 신규 검색/조직/버전2/백업 시험은 미구현 stub 실패를 실제 확인한 뒤 구현. rapid delete→restore 사건누락, history512 실패시 변이, backup완료전 Lock반환을 실패시험으로 재현하고 수정했다
- Core전체 실제 파일/19변조/7절단/프로세스Kill4/경쟁/키·평문 scan 회귀 PASS. 신규 엄격 schema2필수필드/metadata/unknown/version/foldercycle/reference/삭제-tombstone, encrypted restart/immutableTag, 실제legacy payload migration previous exact bytes와 pre-flush 실패원본보존 PASS
- revision metadata변경과 삭제/명시복원은 새revision; 빠른반복사건 유지. 한도거절시 draft/EditVersion/basis/head/content/deleted/history 불변 PASS
- hidden recovery는 최신 준비 basis와 invalid unsaved drafts/폴더/태그를 분리보존하고 수정해저장; invalid draft를immutable history로 넣지 않음 PASS
- 수동백업 최신저장→정확current ciphertext create-new/flush→별도root authenticate/open/save; 기존대상 overwrite거절/flush실패 원본유지/잠금중즉시concel+keyrelease/늦은성공거절/종료전cipher I/O대기 PASS
- dotnet build MemoApp.slnx -c Release -m:1: WPF포함4프로젝트 PASS 0 warnings/errors. 기본parallel solution 명령은 진단상 프로젝트 target시작전 오류0의실패였으며 direct project 및 single-node성공; 해결되지 않은환경특성으로 기록
- git diff --cached --check 및 추적파일 비밀/산출물 휴리스틱 PASS. 전문감사 아님
- WindowsChecks는 Linux교차compile만. WindowsGUI/IME/OS SessionLock/ACL/다른계정/다중모니터실기기 검사아님. 원격push승인차단으로 새WindowsCI실행은 아직 없음

## 2026-10-07 후속4af72d6 실제Windows/Core 검증
- 공식connector 저장(새인증/토큰읽기없이) 및 Git원격HEAD/tree 검증. 로컬4bc0f9d와 원격4af72d6 tree동일
- https://github.com/choehongseok/memo-app/actions/runs/37581724510 Windows Server2022 전체SUCCESS. 실제 WPF control/binding/search/folders/tags/trash/history/sharedsticky/lockUndo·grapheme count·custom order 제어검사PASS, 실제IME/OS SessionLock/물리드래그아님
- TXT UTF8BOM/UTF16LE·BE/UTF32·NUL·odd·malformed·pre-normalization 한도/소스보존hash/overwrite거절/부분flush실패/CreateNew중취소0payload/linkancestor; import알림편집/즉시Clear; 100notes/256title/65536body·history512·실제whole-byteboundary import/reorder 전체불변PASS
- portable 명시선택/unknownargs/기존기본root·linked프로그램/Data 거절PASS. 실제USB제거/권한제한/다른Windows계정미검증
- 기존cryptographic/mutation/processKill/generation/epoch/hiddenrecovery 회귀PASS; 원문134ID·Python6·safety·build0warning/error·publish/uploadPASS
- 실제diff 독립읽기검토에서5P1발견후수정. 검토자가 테스트를독립실행한것은아님. malicious concurrent reparse 교체의handle방어 및 이미시작한평문Write회수 보장없음


## 2026-10-07 a91ca5e 장치별 UI 실제 Windows 검증
- Linux Release 단일노드 solution/Core 전체 0경고/오류 PASS. strict authenticated v3 required/type/unknown/duplicate/emptyID/null/defaultImmutable/ranges/profile32/layout102; unknownnote/NaN/wholebytebudget 거절후 fullsnapshot/events/version 불변; UI만변경시 note revision/CreatedAt/ModifiedAt/history 불변; dirty draft basis 미승격/hidden UI전체복구 PASS
- 실제 v2→v3 migration exact previous ciphertext와 pre-flush 실패 original current byte보존 PASS; v1 기존회귀도 PASS. localUI UUID restart/concurrent8/smallcorrupt preserved/ancestor+leaf Linux symlink拒 PASS
- negative-origin/small-target/144·192DPI puregeometry가 provisional전체fit와 한 번 DIP→nativepixel 변환/clamp PASS
- https://github.com/choehongseok/memo-app/actions/runs/37587829442 실제 Windows Server2022 전체SUCCESS. app/check-host 같은 manifest와 native HWND PerMonitorV2 assertion PASS; native window restore/resize/store/arrange workarea, same-turn new+closed-open memo/widget→lock→encrypted reopen, explicit user-close versus conceal, fold unfoldedsize/position/topmost/opacity, native hook SC_MOVE/SC_SIZE/SC_MAXIMIZE codepath, hide/show, theme/font/scale history·clock 적용, oldcontroller guard/anotherprofile isolation PASS
- 물리 키보드/OS 메뉴/Snap·혼합DPI 실제모니터·IME/OS SessionLock/ACL/USB는 해당 자동시험이 증명하지 않는다. 처음3 UI correctness 지적은 독립 읽기 검토에서 발견후 수정됐고 검토자는 실행하지 않았다
- 원문byte/hash/134ID/Python6/비밀휴리스틱/build/publish/upload PASS. artifact11467587240,65026133bytes,SHA256 b57371769f8516719b5093e65d9e2769cb53d6bff589640f2561cc0948d27480,expires2026-10-14T07:32:16Z. 미서명 합성시험판이며 전문보안감사/전체제품수용이 아니다
