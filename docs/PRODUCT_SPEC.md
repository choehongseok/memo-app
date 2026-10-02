# 메모앱 제품 명세 — 단계 0

## 목적과 명세 권위
개인정보를 포함한 S메모를 대체하는 한국어 Windows 앱이다. 중앙 관리창·독립 바탕화면 포스트잇이 중심이고 Android, 여러 PC, 종단간 암호화 동기화, 로컬 OCR·음성·AI까지 최종 범위에 포함한다. 핵심 메모는 계정·서버·인터넷 없이 사용한다. 이번 이관 작업은 **단계 0 개발 준비만**이며 제품 기능 구현을 시작하지 않는다.

원선택과 운영 지시는 [SOURCE_PROMPT.txt](SOURCE_PROMPT.txt)에 누락 없이 보존했다(27,417바이트, SHA256 `29655ee9235ebcc3e056551e1aeaf3af26781f1b43a93b78cd585e285c48957d`). 문서 간 충돌 시 원문과 이후 명시적 사용자 변경을 우선한다. 기능별 추적·상태·수용 조건은 FEATURES.json의 134 ID가 유일한 원장이다. STATUS는 진행, VERIFICATION은 실행 근거, README는 사용 안내만 담당한다.

## 선택 해석
- B01~B12 독립 창과 B13 관리창 전용은 메모별 선택. B12 위치 고정, B06 항상 위, D08 목록 고정은 독립이다.
- 서식/일반 텍스트/Markdown 모드를 모두 지원한다. 변환 손실을 미리 알리고 원본 리비전을 보존한다. Android 편집 시 표·서식·이미지·미지원 노드를 조용히 제거하지 않는다.
- 설치형과 포터블 모두 필요하다. 업데이트는 수동 기본이며 자동 방식은 선택적이다.
- M06은 `정규화로 대체됨: M01~M05 유지`. 삭제·재번호 없이 원선택 기록에 남긴다. 다른 항목은 제외하지 않는다.
- P01 로컬 전용, P02~P06 선택적 기기 사용. 메모 범위는 device-only 또는 synced. 창 위치·DPI·배율은 기기별 자료다.
- F/G/I/J/K 묶음과 iPhone은 범위 밖. 일반 편집 Ctrl+C/V/Z만 제공하며 클립보드 수집은 없다.
- R01은 날짜 확인 달력. Q08은 동기화 실패·충돌 등 운영 알림이며 잠금화면 메모 내용을 표시하지 않는다.
- H09 기본은 첨부 이름·확장자 등 메타데이터 검색과 T12 OCR 연계. 모든 파일 본문 검색/PDF·한글 편집을 주장하지 않는다.
- N08/N09는 확인한 앱·버전·형식만 어댑터로 지원한다. 미확인 S메모 형식은 차단 상태를 명시하고 독립 개발을 계속한다.

## 기술 선택과 실제 가용성 (2026-10-02)
Windows: C# + .NET 10 LTS + WPF. SDK **10.0.401**, runtime release **10.0.12**, `net10.0-windows`, 언어 버전은 SDK 기본 C# 14. global.json은 rollForward=disable, preview 금지. Linux 공통 로직은 net10.0. .NET 8/9는 지원 종료가 가까워 신규 기반으로 선택하지 않았다. WinUI/Avalonia 전환은 현재 필요가 입증되지 않았으며 Windows 기본 후보를 유지한다. WPF는 Windows 전용이며 Linux에서 실행할 수 없다.

공식 [지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy), [출시 메타데이터](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) 확인: 해당 메타데이터는 active, eol-date=2028-11-14로 표시한다. [출시 발표](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/)는 지원 종료를 2028-11-10으로 설명하므로 지원 종료 시점 전 정책을 재확인한다. [WPF 안내](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)의 Windows 전용 제약을 적용한다.

SDK는 Microsoft 공식 바이너리를 SHA512 확인 후 `/workspace/memo-tools/dotnet`에 설치했다. NuGet.Config는 공식 nuget.org만 허용한다. 현재 제품/검사 프로젝트의 직접 PackageReference는 **0개**이며 검사는 SDK/BCL 기반 console harness다. 워크플로 외부 action은 커밋 SHA로 고정한다. WPF 참조팩은 공식 NuGet에서 복원한다. 새 의존성은 정확한 버전·전이 의존성·보안 공지·라이선스를 확인하고 lockfile을 추가해야 한다. SDK 갱신은 문서·global.json·CI를 함께 수정한다.

Android: 네이티브 Kotlin + Jetpack Compose 방향을 선택한다. .NET WPF 공유 UI 대신 JSON 계약/시험 벡터로 상호운용한다. [Compose](https://developer.android.com/compose), [Keystore](https://developer.android.com/privacy-and-security/keystore) 공식 문서 확인. Android SDK/adb/에뮬레이터는 현재 없다. Kotlin/AGP/Compose/API 수준의 정확한 버전은 단계 4 착수 전 실제 Android toolchain 설치·빌드와 공식 호환표 확인이 선행되어야 하며 현재 고정했다고 주장하지 않는다. Android 범위는 유지된다.

배포: 이후 Windows win-x64 독립 실행 시험판 zip(포터블) 및 설치형을 분리한다. ARM64는 실행 환경 확인 후 지원 목록에 반영한다. 설치형 포맷·배포 서명 인증서·Android 서명키는 이번 골격에 포함하지 않는다. 이번 CI의 DLL/EXE는 런타임 의존 개발 골격이며 설치 가능한 완성 시험판이 아니다. 자동 업데이트·공개 배포는 하지 않는다.

## 모듈 경계
UI(Windows/Android), Documents, Notes/Organization, Storage, Cryptography/KeyAccess, Search, Attachments, History, Recovery, ImportExport, Sync, LocalIntelligence, Lifecycle/Updates를 분리한다. Core의 DocumentContract는 **직렬화 계약 골격만**이며 저장·암호화·검증기·동기화 구현이 아니다. Windows UI는 준비 안내만 표시한다. 메모 기능이 있는 것으로 보이게 하는 가짜 버튼은 없다.

Storage는 검증된 암호화 envelope만 받는 계약으로 설계한다. UI/검색/AI는 잠금 해제된 메모리 자료에만 접근한다. 통신 모듈은 기본 비활성이고 저장 계층이 네트워크 클라이언트를 갖지 않는다. 단계 0에는 어떤 실행 중 네트워크·저장 코드도 넣지 않는다.

## 문서·데이터 계약 v1
Windows 편집기 내부 파일을 원본으로 쓰지 않는다. UTF-8 JSON의 버전 있는 블록 문서를 기준으로 rich/plain/markdown을 구분한다. rich는 paragraph/list/checklist/table/link/image 등 노드와 runs/marks를 갖는다. 각 노드의 알 수 없는 필드는 opaque로 보존하고 편집 불가능한 노드는 읽기 전용으로 유지한다. 이를 지원하지 못하는 클라이언트는 저장 자체를 차단한다. WPF FlowDocument는 투영이며 보존용 원본이 아니다.

메모: schemaVersion, noteId(무작위 UUID), revisionId, parentRevisionIds(병합 DAG), deviceId, title, mode, content, attachmentIds, scope, deleted. 완성 저장 모델에는 createdAt/modifiedAt(UTC 표시용, 충돌 순서 판단의 유일한 기준 아님), folderId, tagIds, color, favorite, pinned, archived, orderKey를 추가한다. 폴더·태그도 UUID/버전/리비전을 갖고 암호화한다. 현재 C# 계약의 작은 필드 집합은 전체 DB 스키마 완성을 의미하지 않는다.

첨부: 무작위 attachmentId, 메모 관계, 이름·MIME·길이·암호화된 청크 참조·검증 해시를 암호화 manifest 안에 둔다. 파일 경로 연결은 별도 reference이며 외부 원본 암호화를 의미하지 않는다. 이력은 immutable revision, 휴지통은 deleted 상태와 tombstone. 기기별 창 좌표·DPI·잠금 설정은 별도 device record이며 서버가 다른 기기에 강제 반영하지 않는다.

DB schemaVersion/document schemaVersion/envelopeVersion/syncVersion은 별개다. 새 버전은 사전 암호화 백업 → 사본 마이그레이션 → 무결성/참조 검증 → 원자 교체. 미지원 버전은 쓰기 차단, 읽기 가능 여부를 명시한다. DB 기술은 메타정보 평문 누출을 피하기 위해 **암호화된 개별 객체+암호화 manifest 저장소**를 우선 설계한다. SQLite를 채택하려면 WAL·인덱스·temp까지 암호화와 복구를 입증해야 하며 일반 SQLite 평문 DB를 선행 구현하지 않는다. 실제 파일 저장 포맷/원자 트랜잭션 구현은 단계 1 검토 대상으로 남는다.

## 보안 설계와 구현 전 검토
보호 대상: 제목·본문·폴더·태그·첨부·미리보기·검색 인덱스·이력·휴지통·백업·OCR·음성·AI 파생 자료. 위협: 분실한 디스크/USB/기기, 악의적 동기화 저장소, 변조된 백업/첨부/업데이트, 잠금 상태 노출. 해제된 화면·감염된 OS·관리자 메모리 접근의 완전 방어는 범위가 아니다.

결정 방향: 유지보수되는 플랫폼 AEAD(예: .NET AesGcm/Android AES-GCM)와 안전한 RNG를 사용한다. 자체 암호 알고리즘·독자 보안 프로토콜 구현 금지. [Microsoft API 문서](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0)는 같은 키에서 nonce 재사용을 금지한다. 후보 envelope는 version, keyId/epoch, objectId, nonce, ciphertext, tag로 구성하며 타입·버전·ID·리비전은 AAD에 인증한다. 외부 노출 필드는 무작위 ID·길이·알고리즘 버전 최소치만 허용한다.

각 객체/첨부는 무작위 데이터 키를 사용하고 vault key로 wrap한다. 수정·청크 암호화 nonce 정책, 키별 사용 한도·회전, 다중 기기 키 충돌 방지, AAD 인코딩·키 계층 및 키 wrap 방식은 독립 보안 검토 후 고정한다. 현재는 **암호 코드·키·암호 파일을 만들지 않았다**. 공개 암호 표준을 선택했다는 사실만으로 자체 동기화 프로토콜 안전성을 주장하지 않는다.

vault key와 보호 수단을 분리한다. [Windows DPAPI CurrentUser](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)는 기기/계정 접근용 wrap이며 유일한 복구 수단이 아니다. Hello는 잠금 해제 사용자 확인 역할·키 접근 연동을 실제 검증해야 한다. Android Keystore wrapping key는 사용자 인증 조건과 하드웨어 지원 수준을 기록한다. 생체인증 UI만 띄우고 평문 키를 남기는 구현은 금지한다.

복구: 독립 무작위 복구 비밀로 암호화한 vault-key 패키지를 사용자가 오프라인 보관하도록 설계한다. 비밀번호 보호를 제공하면 검증된 Argon2id 구현·salt·매개변수·버전·장치별 지연/메모리 측정이 필요하다. 아직 Argon2 라이브러리/수치를 고정하거나 검증하지 않았다. 새 PC/재설치에서 복구 비밀로 복호화 → 새 계정 DPAPI/Keystore wrap → 합성 메모/첨부/이력 확인 → 잘못된 비밀·변조 실패 시험을 통과하기 전 저장 기능을 완료로 표시하지 않는다. 비밀번호와 복구 비밀을 모두 잃으면 복구를 보장하지 않는다.

잠금: OS/app/유휴 잠금 시 메모 창·검색·미리보기·알림·AI context를 비우고 키 세션을 닫는다. 검색 인덱스는 초기에는 해제된 세션 메모리만 사용하며 평문 임시 인덱스를 만들지 않는다. 디스크 캐시가 필요하면 독립 암호 객체로 취급한다. 관리형 메모리/OS 캐시 완전 삭제는 보장하지 않는다. 진단 로그에는 ID·내용·키 대신 비민감 오류 코드만 남긴다.

신뢰 경계: Markdown 스크립트/매크로/외부 URL 이미지 자동 실행 금지. 첨부·수입·백업의 파일 수·총 크기·압축 비율·경로/중복·형식 제한, symlink/zip-slip 방어, staging 검증이 필요하다. 외부 파일 열기/평문 내보내기는 대상과 캐시 잔류 위험을 알린 뒤 수행한다. 업데이트는 배포자 서명·출처·버전/rollback 정책 확인, 실패하면 차단. 체크섬은 서명 대체가 아니다. 개발키와 배포키를 분리한다.

현재 보안 문서는 작성자 자체 검토만 했다. 저장·암호화·복구·동기화·업데이트 구현 전 별도 검토 1회가 필수다. 독립 검토를 수행했다고 기록하지 않는다. 검토 없이 후보 nonce/KDF/키등록 규칙을 보안 구현으로 확정하지 않는다.

## 복구·백업 계약
전체 백업은 암호화 단일 컨테이너에 문서·첨부·이력·tombstone·암호화 키복구 패키지와 인증 manifest를 포함한다. preview도 암호 해제 후 메모리에서만 제공한다. 자동/종료/일간/수동 백업은 정책 scheduler, 보관 개수는 성공 백업 확인 후 적용한다.

복구 모드: (1) 덮어쓰기 — 현재 상태 암호화 보존 후 교체, (2) 병합 — UUID/리비전 비교·충돌본 보존, (3) 다른 기기 이전 — 복구키 확인·새 deviceId·새 기기 wrap. 공통: 입력 크기 제한 → staging 복호화/인증 → 전체 참조·버전·첨부 검증 → 사전 snapshot → 원자 적용. 실패하면 현재 자료가 남는다. 선택 복구는 대상만 적용하고 비선택 자료를 보존한다.

오래된 백업은 이전 revision을 현재로 덮지 않는다. 삭제 tombstone과 복구 epoch를 비교하고 사용자 명시적 복원은 새 revision/복원 사건으로 기록한다. 삭제 정보를 폐기하려면 기기별 인지/폐기 정책을 먼저 검증한다. 백업을 복구했다는 이유로 삭제 메모가 자동 부활하지 않게 한다.

## 동기화 계약 v1 (설계, 구현 없음)
운영 중 DB를 공유 폴더에서 덮어쓰지 않는다. 암호화 object/revision/첨부 청크 단위 push/pull, immutable revision DAG, tombstone, 기기 cursor/ack, epoch, idempotency operationId로 설계한다. 재시도/중복 전달은 같은 operationId로 중복 적용하지 않는다. 오프라인 동시 수정은 parent 비교로 분기하고 양쪽 내용을 conflict revision으로 보존한다. 수정 대 삭제도 충돌로 남기며 last-write-wins로 침묵 삭제하지 않는다. 시간만으로 우선순위를 정하지 않는다.

device-only는 업로드 큐에 들어가지 않는다. 기기 설정은 로컬에 둔다. 동기화 서버는 복호화 키·평문을 받지 않는다. 서버가 관찰 가능한 메타데이터는 계정/접속 주소·시각·객체 개수·크기·전송 빈도·무작위 참조 관계이며 내용을 숨겨도 메타데이터가 사라지는 것은 아니다.

새 기기는 기존 신뢰 기기의 명시적 확인 또는 오프라인 복구 비밀로 등록한다. 검증된 인증 키 교환/기기 인증 라이브러리를 선정하고 시험 벡터·중간자 방어·등록 확인 절차를 독립 검토한다. transport TLS만으로 종단간 암호화를 대체하지 않는다. 기기 해제는 서버 접근 폐기+새 key epoch+남은 기기 wrap 갱신을 포함하고 폐기된 기기 사건을 거부한다. 이미 받은 키/복호화 자료를 회수할 수 있다고 주장하지 않는다.

가상 서버 합성자료 통합시험부터 시작한다. 실제 저장소 연결·운영 계정·공급자·요금은 미선정이며 유료 가입/외부 전송은 하지 않는다. protocol schema와 인증 구현은 별도 검토 후 세분화한다.

## 가져오기 조사
[공식 S메모 백업 안내](https://www.smemo.co.kr/html/backup_restore.html)는 exe/zip 백업과 복구 시 기존 자료 덮어쓰기 가능성을 설명한다. 이것은 내부 문서/DB 스키마 확인 근거가 아니다. 이번에는 실행파일·실제 사용자 자료를 내려받거나 실행하지 않았다. N09는 미착수이며 내부 버전/형식 미확인이 선행 차단이다.

다음 조사: 공개 형식 문서·공개 비민감 fixture 확보 → 형식/버전 식별 → 읽기 전용 어댑터 → 경로/압축/손상 방어 → 원본 해시 보존 및 note 수·한글·서식·첨부 대조. fixture가 없으면 개인정보 업로드를 요구하지 않고 다른 기능을 계속한다. exe 백업을 실행해 변환하지 않는다. 지원 가능한 TXT/Excel 수입과 S메모 지원은 별도로 보고한다.

## 로컬 OCR·음성·AI
T12~T16은 로컬 엔진이 기본, 외부 API 필수 의존성 없음, 실패 시 클라우드 대체 전송 금지. 엔진·모델 버전/해시/라이선스/용량·RAM·CPU/GPU 조건·한글 성능은 실제 실행 가능한 기기에서 확인 후 선정한다. 이번에는 모델 다운로드/설치/추론을 하지 않았다. 모델 설치도 사용자 설정으로 분리한다.

AI 질문은 허용한 잠금 해제 메모만 검색하고 근거 note/revision을 표시한다. 문서 내 명령은 자료로만 취급한다. 파일/외부 통신 도구 접근을 제공하지 않는다. 문장 다듬기·요약 반영은 미리보기/사용자 선택/이력 보존. 잠금·제외 메모의 embedding/OCR/음성 파생물도 같은 권한·암호화 경계를 따른다.

## 단계와 종료 조건
0: 현재 저장소/정책 확인, 문서·134 ID·계약·보안 설계, 빌드/검사 골격, S메모 조사 착수. 현재 원격·Windows 실행과 보안 상세 검토는 차단/미실행이며 완전 종료라고 주장하지 않는다.
1: 암호 저장·자동 저장·재실행/장애 복원, 중앙 창/포스트잇, 폴더·검색·이력·휴지통·암호 백업의 작은 Windows 완성 흐름. 첫 저장 전에 별도 보안 검토.
2: 선택 Windows A~E/H/L/M/N/O/R/S/T08~T10, 수동 PC 이전, 표·Markdown·첨부·설치형/포터블 완성.
3: 여러 PC E2EE 자동 동기화·기기 전용 메모·등록/해제·수정/삭제/복구 충돌 시험. 가상 서버와 실제 운영 결과 구분.
4: Android 작성·수정·사진·운영 알림·암호 동기화. APK/에뮬레이터/실기기 구분.
5: 실제 로컬 OCR·음성·요약·다듬기·근거 기반 질문 엔진 연결 및 성능/통신 검증.
6: 134 ID 전수 대조, 데이터/암호/복구/동기화 회귀, 취약점/라이선스, 실행 파일·APK·사용 설명 정리. 주요 미검증/정상 사용 차단이 남으면 최종 완성본이 아니다.

검증 matrix: 자동 저장/강제 종료/디스크 실패/다중 창, 한글 입력/문서 왕복/다중 모니터/DPI, 잘못된 키/변조/새 PC/선택 복구/평문 누출/잠금 노출, 동시·오프라인/중복/해제/복구 재동기화, Android 문서/사진/로컬 추론/네트워크 차단. 기능 원장의 실제 test_evidence에 해당 커밋·환경·명령·결과가 있어야 검증됨이다. Windows GUI/Hello/실기기는 CI 빌드와 별개다.

## 변경 기록
2026-10-02: 이번 범위를 단계 0으로 제한(이관 요청). 원문 최종 제품 범위는 그대로 유지. 지원 기간과 Windows 기본 후보를 근거로 .NET 10/WPF 선택. 코드 의존성 없이 계약/검사 골격만 작성. 중요 보안 선택은 자체 검토한 후보이며 구현 전 별도 검토 필요. GitHub Forbidden으로 원격 정책/현재 HEAD/Windows CI 가용성을 확인하지 못함.
