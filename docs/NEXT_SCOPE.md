# 후속 구현 원장 (범위 보존)

현재 119개 구현·부분 연결/13개 미착수/N09 차단1/M06 정규화1로 총134개이며 전체 ID 완료·사용자 수용은 0이다. 상태의 기준은 [FEATURES.json](FEATURES.json), 남은 독립 작업은 [FEATURE_GAP_AUDIT.md](FEATURE_GAP_AUDIT.md) 및 [FEATURE_GAP_AUDIT.json](FEATURE_GAP_AUDIT.json), 실행 근거는 [STATUS.md](STATUS.md)다. 원문134 ID·M06·27,417바이트와 원문 해시를 유지한다.

## 현재 검증과 산출물 범위

현재저장2163093/[Windows38008067402](https://github.com/choehongseok/memo-app/actions/runs/38008067402)는 전체FAIL이다. selected-body Enter·grayPNG·H01publication/product PASS, unchangedAcceptPrepared pixel-clear만FAIL→동기workspaceevent수정/새gate대기. 후속H02 boundedDIB·기존PDFauthority·inertRasterPDFCore를 분리검증하며 renderer/UI·O06 branchmerge는 다음독립계획이다.

후속 소스 `af297201`의 [Windows gate38004389975](https://github.com/choehongseok/memo-app/actions/runs/38004389975)는 전체 FAIL이다. Excel 매핑·schema9 persistence·기존 Enter boundary·빈 항목 Undo/Redo는 PASS였고, selected-body Enter fixture의 focus 누락으로 다음 검사가 FAIL했다. focus 수정·H01 이미지 문서·H08 gray PNG 새 Windows 검사는 아직 대기다.

이전 소스 `5db5d8a`의 [Windows gate38002826135](https://github.com/choehongseok/memo-app/actions/runs/38002826135)는 전체 FAIL이다. native/build/fullCore 및 원래 checklist Enter source-version·lock boundary는 PASS였으나 추가 Excel 재열기 fixture, 백업 정책 미저장 날짜 기대 fixture, 빈 체크 항목 Undo exact-source 검사가 FAIL했다. 후속 암호 파일 복사 재열기·정책 fault fixture·Undo 원문 복원을 수정하고 시트/열 매핑·설치 실행 network 관찰 검사를 추가했다. 새 Windows 결과는 다음 소스 gate에서 구분한다.

기존 최종 시험판 `d8bccaab`의 [Windows37995989631](https://github.com/choehongseok/memo-app/actions/runs/37995989631) 및 artifact `11647312201`은 보존한다. 그 시험판은 실제 OCR·schema8 자동 휴지통 정리를 포함하지만 이후 schema9 백업 정책·Word canonical 서식·Excel scalar/매핑·체크목록 Enter/Undo 보완을 포함한다고 표시하지 않는다. 기존 시험판 안내는 [WINDOWS-TRIAL-2026-10-09.md](WINDOWS-TRIAL-2026-10-09.md), 사용자 확인 목록은 [USER-TESTS.md](USER-TESTS.md)다. 동일 소스/검사의 CI·산출물 업로드를 반복하지 않는다.

## 이미 연결한 기능과 남은 경계

| ID | 현재 상태와 남은 작업 |
|---|---|
| A09·E10 | 최근 메모·저장 검색조건의 기기별 암호 UI 기록과 Windows 합성 검사가 연결됨. 신규 미구현 기능으로 재작업하지 않으며 실제 사용자 수용은 별도다. |
| E08 | 원본불변 최대512자 검색 문맥 강조와 Windows 합성 검사 연결됨. 실제 사용자 수용 미확인이다. |
| H03 | 암호 첨부 패널의 단일 로컬 이미지 FileDrop과 실제 routed Windows 검사 연결됨. 물리 drag 수용 미확인이다. |
| H07 | 기기 소유 파일 경로 metadata·복구 보존·명시 열기 권한 및 Windows37963072226 검사 연결됨. 원본 파일 암호화·경로 경쟁 방어를 제공하지 않으며 실제 외부 앱/깨진 링크 수용은 남는다. |
| L01·L02·L03·L05·L06 | 시작 등록·트레이·세션 수명과 설치 실행 검사가 연결됨. 실제 로그인/로그아웃·Explorer·물리 사용자 수용은 별도다. |
| D11 | 기본off 백업 선행 휴지통 정리와 Windows 합성 검사 연결됨. 기존 시험판은 schema8이며 후속 schema9 호환은 새 gate와 구분한다. |
| N02 | 제목/본문 평문 PDF와 Windows37980813931 검사 연결됨. 전체 Unicode shaping·서식/표/그림·rendered Markdown 보존 및 실제 viewer 수용은 남는다. |
| N04 | canonical 글자·목록·체크 상태·표와 inert 링크 주소를 DOCX로 연결함. 그림·rendered Markdown 및 실제 Word viewer 수용은 남고 새 Windows gate 대기다. |
| N07 | 명시 시트·제목/본문 열·첫 행 제외와 기존 템플릿 선택, SHA256 원본 연결·전체 후보 거절을 구현함. af297201 실제 매핑/modal lock/재열기 검사는 PASS다. 날짜는 raw serial이며 수식·.xls는 지원하지 않는다. |
| N08 | 단일 strict UTF8 .md 제목/raw 본문 어댑터와 Windows 합성 검사가 연결됨. 실제 Obsidian 생성 파일의 버전·바이트/해시 근거는 미확보다. 전체 vault·플러그인·첨부를 보편적 필수 범위로 확대하지 않는다. |
| O01·O02·O03 | schema9 암호 저장 정책과 root identity 검사를 연결함. af297201 실제 재실행/OFF/설정 경쟁/예약·실패/복구 검사는 PASS다. |
| O04 | 현재 세션 보관 한도 설정·가득 찬 경우 중단을 연결함. 원장의 재실행 유지·보관 정리 미완료를 유지하며 백업 자동 삭제는 별도 정책 결정 전 실행하지 않는다. |
| T12 | 실제 로컬 한글/영문 OCR·별도 plain 암호 적용·검색·재실행 Windows 검사 연결됨. 일반 입력 확대·정확도 corpus·자원/통신 관찰·추가 수명 경계는 남는다. |

## 계속 진행할 독립 작업

1. 현재 Windows gate에서 C10 빈 항목 Undo/Redo 원문 복원, N07 매핑·원본 변경·modal 잠금, O01~O03 persisted 정책과 설치 실행 owning-PID 통신 관찰을 확인한다. 실패는 기능/fixture 원인을 분리해 고치며 strict scaffold·원본 보존·권한 조건을 완화하지 않는다.
2. H01/H02 문서 내부 이미지와 일반 clipboard Bitmap/DIB 입력을 bounded·명시 입력으로 연결한다. H08/T12의 일반 이미지 형식 확대, 합성 입력 corpus와 느린 실행/선택·epoch·잠금 경계 검사를 진행한다. 이미 연결한 H03 drop을 미구현으로 취급하지 않는다.
3. H09 첨부에 연결된 암호 OCR 파생 정보와 검색 조건, O06 동일 ID 충돌 보존 merge 및 삭제/복구 epoch 정합성을 구현한다. 네트워크 동기화와 구분해 독립 계약·검사를 진행한다.
4. N02 전체 Unicode shaping·rich/Markdown 출력, N04 그림·rendered Markdown을 보완한다. N08은 알려진 앱 버전이 실제 저장한 비민감 합성 파일의 출처·해시·예상 매핑을 확보하고 검증한다. 공식 형식 문서나 릴리스 사실만으로 생성 버전 호환을 주장하지 않는다. 확인된 403/restricted 접근은 우회하지 않는다.
5. P01/T12 통신 관찰·자원/정확도 등 독립 검사를 진행하고 실제 오프라인·IME·모니터/DPI·OS 수명·viewer 수용은 사용자 기기 확인으로 별도 기록한다. 물리 수용 대기로 독립 구현·검사를 중단하지 않는다.
6. 미착수13개는 L09·L10, P05·P06, Q03·Q04·Q06·Q07·Q08, T13·T14·T15·T16이다. 업데이트 게시자 인증/rollback·실패 복구, 변경 단위 E2EE 동기화·충돌/기기 범위, Android 원문 보존 편집, 실제 로컬 음성·AI 엔진/모델 계약을 진행한다. 오프라인 계약/fixture 작업과 실제 서명 신뢰·기기 등록·SDK 약관·모델 접근 결정을 분리한다.

Android SDK 약관은 이미 전달한 응답 대기를 유지하며 승인 전 설치/수락하지 않고 반복 질문하지 않는다. 공식 음성·AI 모델 경로의 확인된 403은 우회하지 않는다. N09는 알려진 S메모 앱 버전의 공개 또는 전적으로 합성된 파일 근거가 필요한 별도 차단1로 유지한다. 외부/유료 서비스나 계정을 임의 추가하지 않는다. 저장·암호화·복구·동기화·업데이트는 별도 보안 검토를 거치며 실메모·키·토큰·사용자 백업을 저장소/로그/CI에 넣지 않는다.
