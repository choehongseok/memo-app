# 134 ID 후속 작업 감사 — 시험판35 기준

이 표는 전달한 시험판 `35e01a00`/artifact `11659571081`의 남은 범위를 기록합니다. 이후 H01 작업의 성공을 이 시험판에 소급하지 않습니다. 정확한 구현·근거·승인/접근 경계는 `CONTINUATION_ID_AUDIT.json`의 같은 ID를 따릅니다.

119 진행·13 미착수·N09 차단1·M06 정규화1이며 전체 기능 완료 및 사용자 수용을 표시하지 않습니다. 자동 검사 성공과 실제 한글 IME·사용자 Windows·Office 뷰어 수용은 구분합니다. 대표 분류가 사용자 시험이어도 새 코드 작업이 불가능하다는 뜻은 아닙니다.

## H01 재개와 보존

이미지 v2의 주변 서식 본문을 기존 이미지 참조·raw node·순서·중복·라벨을 바꾸지 않고 편집하는 작업입니다. 이미지 자체 편집·이동·크기 조절·자동 표시를 추가하지 않습니다. 기존 명시적 이미지 추가/위치 제거/표시 경로는 별도 거래이며 주변 본문 편집 시 표시 픽셀은 폐기됩니다.

이전 WIP는 PDF/OCR 최종 게이트에 집중하면서 컴파일·GREEN·보안 POST·Main/포스트잇 연결 전에 중단되어 설치 ZIP에서 제외됐습니다. archive `195b77408ec8c354d716ff2822e253749b01f3e3`, ref `archive/h01-paused-20261010`, patch SHA256 `e2518a449bdc06cdadea9792f3b7181268082b175eef59c9e1d4ea808f886e91`을 폐기하지 않고 보존합니다. base82와 현행35의 제품·검사 트리는 같고 업로드 marker만 달랐습니다. 독립 recovery PRE 후9파일을 한 번 복구했습니다.

실제 cf7 native placeholder/Undo/합성 composition 선행검사는 통과했습니다. 새 Core 원문권한·단일 own-transition·재진입·이력/OCR 한도·root·잠금/폐기 참조해제, 새 WPF placeholder 변조/명령/IME/Undo/수명 검증, 독립 POST, Main/포스트잇 동시편집과 원문·암호자료 save/reopen 검증이 남았습니다. 이 단계가 끝나기 전 기존 사용자 호스트의 v2 읽기전용 제한은 유지합니다.

## 진행 가능한 순서

H01 기존 승인 계획을 먼저 진행합니다. 이후 합성 OCR/이미지 자원·정확도 검사, 다음 PDF 문서 수직 기능 등 원문 요구와 승인 설계를 따라 진행할 수 있습니다. 동기화·업데이트·음성/AI의 오프라인 합성 계약 검사는 실제 provider/배포자/모델 통합과 구분하며, 저장·암호화·복구·동기화·업데이트 변경 전에 독립 보안 PRE를 수행합니다. 후보 목록은 새 아키텍처가 이미 승인·구현됐다는 뜻이 아닙니다.

Android SDK 약관 대기, 실제 인증/서명 신뢰 결정, 공식 모델 접근403, N09 형식 fixture 부재는 기존 차단을 유지합니다. 우회·외부/유료 서비스·main 병합·공개 release를 추가하지 않습니다.

## 원문 ID별 남은 작업

| ID | 원문 항목 | 원장 | 후속 분류 | 남은 조건 |
|---|---|---|---|---|
| A01 | 새 메모 만들기 | 진행 중 | physical-user | 새 합성 메모 생성·목록/편집 선택의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A02 | 메모 수정 | 진행 중 | physical-user | 실제 한글 IME 마지막 조합·공유 포스트잇 편집의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A03 | 메모 삭제 | 진행 중 | physical-user | 합성 메모 삭제·암호 휴지통 확인의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A04 | 메모 자동 저장 | 진행 중 | physical-user | 마지막 IME 조합 직후 종료·재실행 보존의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A05 | 메모 복사·복제 | 진행 중 | physical-user | 서식/Markdown/첨부가 있는 합성 메모 복제·원본 유지의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A06 | 메모 제목 사용 | 진행 중 | physical-user | 제목 수정과 재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A07 | 제목 없이 본문만 사용하는 간단 메모 | 진행 중 | physical-user | 빈 제목 메모의 표시/저장의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A08 | 여러 메모 동시에 열기 | 진행 중 | physical-user | 여러 실제 포스트잇의 동일 메모 공유 편집의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A09 | 최근 사용한 메모 보기 | 진행 중 | physical-user | 최근 목록의 재실행·잠금 비노출의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| A10 | 메모 작성일·수정일 표시 | 진행 중 | physical-user | 사용자 시간대와 날짜 표시 확인의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B01 | 메모를 바탕화면에 작은 창으로 띄우기 | 진행 중 | physical-user | 실제 바탕화면 포스트잇 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B02 | 재실행 후 메모 위치 기억 | 진행 중 | physical-user | 해당 모니터에서 창 위치 재실행 복원의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B03 | 메모 창 크기 기억 | 진행 중 | physical-user | 창 크기 재실행 복원의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B04 | 메모별 색상 변경 | 진행 중 | physical-user | 포스트잇별 색상과 재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B05 | 메모 투명도 조절 | 진행 중 | physical-user | 실제 모니터 투명도 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B06 | 항상 위에 표시 | 진행 중 | physical-user | 다른 실제 앱 앞에서 항상 위 동작의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B07 | 메모 접기·펼치기 | 진행 중 | physical-user | 서식/첨부 포스트잇 접기/펼치기와 복원의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B08 | 메모 최소화 | 진행 중 | physical-user | 작업표시줄 최소화/복원의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B09 | 여러 메모 한 번에 숨기기·표시하기 | 진행 중 | physical-user | 전체 포스트잇 숨김/표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B10 | 메모 화면 위치 자동 정렬 | 진행 중 | physical-user | 실제 여러 모니터에서 자동 배열의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B11 | 여러 모니터에서 메모 위치 기억 | 진행 중 | physical-user | 물리 혼합DPI·모니터 제거/재연결·키보드 Snap의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B12 | 특정 메모를 바탕화면에 고정 | 진행 중 | physical-user | 실제 OS 이동/크기조절 요청 중 위치 고정의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| B13 | 바탕화면 포스트잇 없이 메인 프로그램 안에서만 사용 | 진행 중 | physical-user | 포스트잇을 닫은 메인창 전용 사용·재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| C01 | 글꼴 변경 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C02 | 글자 크기 변경 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C03 | 굵게 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C04 | 밑줄 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C05 | 취소선 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C06 | 글자색 변경 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C07 | 형광펜·배경색 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C08 | 글머리표 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C09 | 번호 목록 | 진행 중 | physical-user | Actual Korean IME and physical selection/caret/keyboard acceptance remain; old generic Windows-build/control blockers are stale after successful whole Windows CI. No concrete missing behavior for this named baseline was found. |
| C10 | 체크박스 목록 | 진행 중 | physical-user | Relevant actual Windows checks PASS; physical IME/keyboard acceptance remains. |
| C11 | 표 삽입 | 진행 중 | physical-user | Physical table typing/navigation acceptance remains. UI inserts only 2x2 at document end; configurable dimensions/insert-at-caret/row-column management are feasible improvements, but original C11 only says table insertion, so they are not grounds to claim the baseline absent. No merged/nested table support claimed. |
| C12 | 링크 삽입·클릭 | 진행 중 | acceptance-baseline | Current35 whole Windows/native gate passed the existing implemented baseline. Physical Korean input/display/explicit-action user acceptance remains unconfirmed; obsolete C10/whole-gate pending statements are closed. |
| C13 | 실행취소·다시실행 | 진행 중 | physical-user | Physical keyboard and Korean composition acceptance remain. Ledger generic statements about missing WPF automation and temporary sticky state are obsolete; real Undo/Redo and clear regressions already exist. |
| C14 | 서식 없는 단순 텍스트만 사용 | 진행 중 | physical-user | Physical IME/user acceptance remains. Android preservation is a later Q dependency, not evidence that Windows plain mode is unimplemented. Old GUI-unverified ledger language is stale after actual WPF gates. |
| C15 | Markdown 지원 | 진행 중 | acceptance-baseline | Current35 whole Windows/native gate passed the existing implemented baseline. Physical Korean input/display/explicit-action user acceptance remains unconfirmed; obsolete C10/whole-gate pending statements are closed. |
| D01 | 폴더 만들기 | 진행 중 | physical-user | 한글 폴더 생성·재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D02 | 하위 폴더 만들기 | 진행 중 | physical-user | 하위 폴더 생성·상하위 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D03 | 메모를 폴더 간 이동 | 진행 중 | physical-user | 폴더 간 이동과 재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D04 | 태그 사용 | 진행 중 | physical-user | 한글/Unicode 태그 추가·검색의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D05 | 메모별 색상 분류 | 진행 중 | physical-user | 메모 색 분류·검색/목록 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D06 | 중요 메모 표시 | 진행 중 | physical-user | 중요 표시·검색/재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D07 | 즐겨찾기 | 진행 중 | physical-user | 즐겨찾기 필터·재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D08 | 메모 고정 | 진행 중 | physical-user | 목록 고정 그룹 정렬·재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D09 | 보관함 | 진행 중 | physical-user | 보관함 이동·목록/검색의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D10 | 휴지통 | 진행 중 | physical-user | 휴지통 삭제/복원·원문 보존의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D11 | 휴지통 자동 비우기 | 진행 중 | physical-user | 설정 기본off/취소/잠금 해제; 사용자 원본 자동삭제 시험 불필요의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D12 | 여러 메모 일괄 이동·삭제 | 진행 중 | physical-user | 여러 합성 메모 선택·이동/삭제 확인의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D13 | 이름순 정렬 | 진행 중 | physical-user | 한글·빈 제목의 이름순 정렬 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D14 | 최근 수정순 정렬 | 진행 중 | physical-user | 실제 편집 후 수정순 정렬의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D15 | 작성일순 정렬 | 진행 중 | physical-user | 작성순 정렬 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| D16 | 사용자 지정 순서로 직접 배열 | 진행 중 | physical-user | 사용자 순서·목록고정 그룹의 재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E01 | 전체 메모 검색 | 진행 중 | physical-user | 전체 검색 결과와 잠금 비노출의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E02 | 제목 검색 | 진행 중 | physical-user | 제목 검색·본문 제외의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E03 | 본문 검색 | 진행 중 | physical-user | 본문 검색·제목 제외의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E04 | 폴더별 검색 | 진행 중 | physical-user | 상하위 폴더 검색의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E05 | 태그별 검색 | 진행 중 | physical-user | 태그 검색 Unicode 표기의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E06 | 기간 지정 검색 | 진행 중 | physical-user | 사용자 시간대 기간 경계 검색의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E07 | 중요 메모만 검색 | 진행 중 | physical-user | 중요 메모 검색의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E08 | 검색 결과에서 해당 문장 강조 | 진행 중 | physical-user | 제목/본문/첨부 이름 첫 일치 문맥의 실제 강조 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E09 | 여러 조건 조합 검색 | 진행 중 | physical-user | 폴더·태그·기간·중요 조건 조합의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| E10 | 자주 쓰는 검색조건 저장 | 진행 중 | physical-user | 저장 조건의 정확 적용/불가 조건 보존·재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| H01 | 메모에 이미지 붙이기 | 진행 중 | independent-code | Approved editable-v2 surrounding-text implementation/review/test integration remains; archived nine-file H01 work is separate paused WIP, not current source or artifact. Physical Korean IME/user acceptance remains. Resizing, general image formats and editor inline paste/drop are optional further extensions. |
| H02 | Ctrl+V로 이미지 붙이기 | 진행 중 | independent-test | Current35 raw-PNG and CF_DIB 24/32 BI_RGB clipboard paths passed. Clipboard producer/physical acceptance remains. DIBV5/general formats/editor-inline paste are optional extensions, not missing the bounded attachment Ctrl+V baseline. |
| H03 | 이미지 드래그앤드롭 | 진행 중 | physical-user | Physical Explorer drag/drop acceptance remains. Multiple files and inline insertion absent, but original H03 does not explicitly demand batch drop; common local image dropping baseline is present. Preview/display limitation belongs to H08/H01 rather than claiming drop unimplemented. |
| H04 | PDF 첨부 | 진행 중 | physical-user | Physical filepicker/user acceptance remains. Parsing or editing document contents is not required by these IDs; normal encrypted byte-preserving attachment baseline is implemented. Fixed size/count limits are disclosed, not missing format adapters. |
| H05 | 한글·Word·Excel 등 파일 첨부 | 진행 중 | physical-user | Physical filepicker/user acceptance remains. Parsing or editing document contents is not required by these IDs; normal encrypted byte-preserving attachment baseline is implemented. Fixed size/count limits are disclosed, not missing format adapters. |
| H06 | 첨부파일 클릭해서 열기 | 진행 중 | physical-user | Actual associated Windows PDF/HWP/Office application opening/physical acceptance remains. Launcher initiation is tested but external real application rendering is not. No missing autonomous core open flow found. |
| H07 | 파일 경로만 연결 | 진행 중 | physical-user | Physical associated-app and broken-path user acceptance remain. Original external file encryption and hostile replacement/race prevention are explicitly not claimed or implied by path-only linking. Core path/open failures are already handled; no need to ask for source data. |
| H08 | 이미지 미리보기 | 진행 중 | independent-test | Current35 bounded RGB/RGBA/gray/gray-alpha PNG preview/native pixel and invalidation paths passed. Supported-format synthetic corpus and physical viewing acceptance remain; JPEG/palette/interlace/ICC/EXIF/APNG extensions are optional. |
| H09 | 첨부파일 검색 | 진행 중 | independent-test | Current35 linked native OCR, schema11 per-note authority/search and final package/install passed. Broader synthetic input usefulness/resource/accuracy checks and user acceptance remain; no universal document-body search requirement. |
| L01 | Windows 시작 시 자동 실행 | 진행 중 | physical-user | Actual Windows login/startup execution and user acceptance; code is present, ledger progress is not missing implementation. |
| L02 | 시스템 트레이 상주 | 진행 중 | physical-user | Physical tray/Explorer restart acceptance. Durable remembered tray choice is absent but not explicitly required by SOURCE_PROMPT L02; optional improvement, not a proven blocker. |
| L03 | 창을 닫아도 트레이에서 계속 실행 | 진행 중 | physical-user | Physical hide/reopen and OS shutdown acceptance; session-only choice is explicit, no automatic persistence currently. |
| L04 | 완전히 종료하는 메뉴 | 진행 중 | physical-user | User acceptance only for the implemented exit flow; durable exit backup setting belongs O02. |
| L05 | 트레이 우클릭으로 새 메모 | 진행 중 | physical-user | Physical tray click acceptance only. |
| L06 | 트레이에서 최근 메모 바로 열기 | 진행 중 | physical-user | Physical menu acceptance only; recent persistence is already implemented, stale generic ledger must not imply otherwise. |
| L07 | 포터블 실행 | 진행 중 | physical-user | Removable-device/ACL/physical trust-dialog acceptance, not missing portable routing. |
| L08 | 일반 Windows 설치형 | 진행 중 | physical-user | Own-account installation/trust dialog/ACL acceptance and production publisher signing; MSI is not required by the feature name. |
| L09 | 자동 업데이트 | 미착수 | independent-code | 배포자 인증 서명·변조/rollback 거절·원자 적용/실패 복구를 갖춘 업데이트가 미구현. 수동 기본과 자동 opt-in 설정을 함께 보존한다. unsigned trial/hash manifest는 publisher 인증이 아니다. |
| L10 | 수동 업데이트 | 미착수 | independent-code | 배포자 인증 서명·변조/rollback 거절·원자 적용/실패 복구를 갖춘 업데이트가 미구현. 수동 기본과 자동 opt-in 설정을 함께 보존한다. unsigned trial/hash manifest는 publisher 인증이 아니다. |
| M01 | 수정 이력 저장 | 진행 중 | physical-user | Ordinary history usability acceptance; stale ledger saying only schema2/Core is inaccurate. |
| M02 | 과거 버전 보기 | 진행 중 | physical-user | Physical history selection/display acceptance. |
| M03 | 과거 버전으로 되돌리기 | 진행 중 | physical-user | User acceptance of restore semantics; not missing restore code. |
| M04 | 삭제한 메모 복원 | 진행 중 | physical-user | User acceptance only; purge is not undoable without retained backup and is disclosed. |
| M05 | 변경 이력 날짜별 비교 | 진행 중 | physical-user | Physical date/diff usability acceptance; automatic coverage already exists. |
| M06 | 변경 이력이 필요 없음 | 정규화로 대체됨 | normalized | Normalized replacement only: M01–M05 retained. Never delete/re-number M06 or implement a conflicting no-history behavior. |
| N01 | TXT 내보내기 | 진행 중 | physical-user | Physical user/editor opening acceptance remains. TXT naturally cannot preserve style/attachments, so disclosed projection is adequate for named TXT export; opaque unknown rich documents are refused rather than silently flattened. Generic old Windows/filepicker blockers are stale for automated paths. |
| N02 | PDF 내보내기 | 진행 중 | independent-code | Approved plan still has richer formatted/table/Markdown/authenticated image PDF verticals beyond plain validated title/body projection; actual reader/user acceptance remains. Emoji/wrap/page-edge baseline is no longer pending. |
| N03 | Excel 내보내기 | 진행 중 | physical-user | Physical Excel/LibreOffice rendering acceptance remains. Formatting and attachment export absent, but spreadsheet title/body mapping is a reasonable baseline for original generic Excel export; original does not specify Excel document-format roundtrip. Do not label it format-preserving; optional rich-preserving exports can be developed without new user permission. |
| N04 | Word 내보내기 | 진행 중 | optional-independent-code-and-acceptance | Microsoft Word/user acceptance remains. Rendered Markdown is an optional inferred enhancement; current default literal-source projection is valid and preserves original raw text. |
| N05 | 여러 메모 일괄 내보내기 | 진행 중 | physical-user | Physical output/viewer acceptance remains. It is inaccurate to call N05 TXT-only: Office/PDF capture take note collections. Content-fidelity restrictions remain under N02/N04 and image work; original batch behavior is present. A single original-preserving transfer already exists separately as N10. |
| N06 | TXT 가져오기 | 진행 중 | physical-user | Physical filepicker/Korean user acceptance remains. Legacy encodings not accepted, but no universal encoding requirement in original; strict refusal avoids silent corruption. Existing stale generic Windows automation blockers are not current code blocks. |
| N07 | Excel 가져오기 | 진행 중 | physical-user | Explicit sheet/title/body column/first-row skip and known-template choice, SHA-bound source, whole-candidate rejection and independent encrypted-copy reopen actual Windows38004389975 relevant groups PASS. Whole gate failed unrelated selected checklist fixture. Date remains raw serial; formulas/.xls unsupported; physical Excel acceptance unverified. |
| N08 | 다른 메모 프로그램 자료 가져오기 | 진행 중 | access-blocked-evidence-and-acceptance | Raw strict UTF8 .md adapter and current native Markdown import tests passed. Actual public fully synthetic fixture saved by a known producer/version remains unavailable; official format/release docs alone do not supply producer-byte provenance. Existing official route403 is not retried/bypassed. Whole-vault/plugins/all other programs are not universally required. |
| N09 | S메모 자료 가져오기 | 차단됨 | blocked-format | 공식 구버전 안내는 syncmemo_data의 RTF/STF 개별본문을 설명하지만 STF는 서식일 수 있고 2015 PC4.0 공지는 형식변경을 명시한다. 특정 버전의 인코딩·제목/폴더/시각/서식/첨부 매핑 및 완전합성 versioned fixture가 없어 S메모 호환 어댑터를 검증할 수 없다. 실제 사용자자료/실행파일은 요구하거나 실행하지 않는다. |
| N10 | 전체 자료를 한 파일로 이전 | 진행 중 | physical-user | 실제 두 PC/USB 암호 사본 이전·별도 비밀로 복구의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| O01 | 자동 백업 | 진행 중 | physical-user | Current35 persistent schema9 per-device authenticated policy/root/restart/OFF/failed reservation and final package passed. User-PC backup lifecycle acceptance remains. Optional destructive old-backup rotation is not required by the disclosed full-stop capacity policy. |
| O02 | 종료 시 백업 | 진행 중 | physical-user | Current35 persistent schema9 per-device authenticated policy/root/restart/OFF/failed reservation and final package passed. User-PC backup lifecycle acceptance remains. Optional destructive old-backup rotation is not required by the disclosed full-stop capacity policy. |
| O03 | 하루 한 번 백업 | 진행 중 | physical-user | Current35 persistent schema9 per-device authenticated policy/root/restart/OFF/failed reservation and final package passed. User-PC backup lifecycle acceptance remains. Optional destructive old-backup rotation is not required by the disclosed full-stop capacity policy. |
| O04 | 백업 보관 개수 설정 | 진행 중 | physical-user | Capacity1..100 full-stop retention and encrypted policy persistence are implemented and current native gate passed; stale session-only ledger reason is incorrect. User acceptance remains. Automatic rolling deletion is optional destructive policy, not presently authorized data deletion. |
| O05 | 수동 백업 | 진행 중 | acceptance-baseline | Current35 whole Windows/native gate passed the existing implemented baseline. Physical Korean input/display/explicit-action user acceptance remains unconfirmed; obsolete C10/whole-gate pending statements are closed. |
| O06 | 백업 파일 선택 후 복구 | 진행 중 | independent-code | Current35 same-root same-ID live branch union and keep-current two-parent causal resolution plus whole-copy recovery-secret migration passed. Broader explicitly selected overwrite/alternate merge resolution/key-epoch/deletion-lineage cases remain separate recovery design/code; no resurrection/head promotion/network sync may be inferred. User acceptance remains. |
| O07 | 복구 전 현재 상태 자동 보존 | 진행 중 | physical-user | Physical recovery acceptance; regression evidence must stay linked to real files rather than generic stale ledger. |
| O08 | 백업 미리보기 | 진행 중 | physical-user | Physical picker/read-only preview acceptance. Full rich/image rendering is not required by the minimal plain preview contract and must not be mislabeled entirely missing. |
| O09 | 특정 메모 선택 복구 | 진행 중 | physical-user | Physical selection/default-No/apply acceptance only; same-ID overwrite/merge belongs O06 rather than silently treating fresh-copy restoration as all merge modes. |
| P01 | 한 PC에서만 사용 | 진행 중 | acceptance-baseline-with-observation-limits | Physical offline/user acceptance remains. Observer is bounded PID+creation TCP/UDP v4/v6 sampled rows only; no packet/DNS/delegated-process/between-sample/future-traffic/full firewall guarantee. Existing observer coverage is implemented, not pending. |
| P02 | 집 PC와 다른 PC에서 사용 | 진행 중 | physical-user | Actual second-PC execution/account wrap/acceptance; DPAPI wrap is separately missing code, not a prerequisite for current recovery-secret transfer. |
| P03 | USB로 데이터 직접 이동 | 진행 중 | physical-user | Physical USB copy/removal/error handling acceptance; this is not missing all transfer implementation. |
| P04 | 파일 하나를 복사해 다른 PC에서 계속 사용 | 진행 중 | physical-user | Physical other-device acceptance only for implemented recovery-secret path; metadata-only object references are not mistaken for a complete file because objects are included. |
| P05 | 여러 PC 자동 동기화 | 미착수 | independent-code | 변경 단위 E2EE 자동 동기화·기기별 범위·동시/오프라인/삭제 충돌·중복/재시도·기기 등록/해제/키 교체/복구 상호운용이 미구현. 전체 DB 파일 overwrite는 구현으로 인정하지 않는다. |
| P06 | PC별 서로 다른 메모 일부 유지 | 미착수 | independent-code | 변경 단위 E2EE 자동 동기화·기기별 범위·동시/오프라인/삭제 충돌·중복/재시도·기기 등록/해제/키 교체/복구 상호운용이 미구현. 전체 DB 파일 overwrite는 구현으로 인정하지 않는다. |
| Q03 | 휴대폰에서 메모 작성·수정 | 미착수 | independent-code | Android 편집/저장 시 Windows 표·서식·이미지·미지원 노드를 보존하는 편집 UI와 암호 저장이 미구현. SDK 사용 약관 승인 질문은 이미 pending이며 답변 전 SDK 설치/약관 수락을 진행하지 않는다. |
| Q04 | Android 앱 | 미착수 | independent-code | 실제 Android 앱·Kotlin/AGP/Compose/API 호환 버전 고정·APK build이 미구현. SDK 사용 약관 승인 질문은 이미 pending이며 답변 전 SDK 설치/약관 수락을 진행하지 않는다. |
| Q06 | PC·휴대폰 자동 동기화 | 미착수 | independent-code | 변경 단위 E2EE 자동 동기화·기기별 범위·동시/오프라인/삭제 충돌·중복/재시도·기기 등록/해제/키 교체/복구 상호운용이 미구현. 전체 DB 파일 overwrite는 구현으로 인정하지 않는다. |
| Q07 | 휴대폰 사진 촬영 후 메모 추가 | 미착수 | independent-code | 명시 사진 촬영/취소·권한 및 사진/EXIF의 암호 저장이 미구현. SDK 사용 약관 승인 질문은 이미 pending이며 답변 전 SDK 설치/약관 수락을 진행하지 않는다. |
| Q08 | 휴대폰 알림 | 미착수 | independent-code | 잠금화면 제목/본문 비노출 운영 실패·충돌 알림이 미구현. SDK 사용 약관 승인 질문은 이미 pending이며 답변 전 SDK 설치/약관 수락을 진행하지 않는다. |
| R01 | 바탕화면 달력 | 진행 중 | physical-user | 실제 달력 날짜 탐색·기기별 창 복원의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| R02 | 바탕화면 시계 | 진행 중 | physical-user | 실제 시계 표시·기기별 창 복원의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| S01 | 왼쪽 폴더·오른쪽 메모 구조 | 진행 중 | physical-user | 왼쪽 폴더/오른쪽 목록 키보드 사용성의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| S02 | 메모 카드 형태 | 진행 중 | physical-user | 메모 카드의 실제 글꼴/줄바꿈 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| S04 | 다크모드 | 진행 중 | physical-user | 사용 모니터의 다크모드 대비와 재실행의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| S05 | 글자 크기 전체 조절 | 진행 중 | physical-user | IME/글꼴 혼합 문서 전역 크기 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| S06 | 화면 배율 조절 | 진행 중 | physical-user | 물리 OS DPI와 앱 배율 독립 동작의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| T08 | 드래그앤드롭으로 메모 순서 변경 | 진행 중 | physical-user | 물리 drag·filter 밖 순서/고정 그룹 유지의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| T09 | 메모 글자 수 | 진행 중 | physical-user | 한글·결합문자·emoji grapheme 글자 수 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| T10 | 전체 메모 개수 | 진행 중 | physical-user | 활성/보관/휴지통/검색 개수 표시의 물리 사용자 수용 미확인. 기존 Windows 제어를 일괄 미검증으로 표시하지 않는다. |
| T12 | OCR로 이미지 글자 추출 | 진행 중 | independent-test | Varied public synthetic Korean/English accuracy corpus, CPU/RAM/time measurements and actual user acceptance remain. Generic pending backend races prove UI lifetime, not native-engine provenance. Unsupported image formats optional. |
| T13 | 음성으로 메모 작성 | 미착수 | independent-code | 한글 로컬 음성 인식·명시 녹음·텍스트 미리보기/적용의 실제 엔진/모델 설치·추론이 미구현/미검증. 후보 조사와 mock은 기능 완료가 아니다. 공식 모델/release 경로 접근 403은 재시도나 우회하지 않는다. |
| T14 | AI 요약 | 미착수 | independent-code | 한글 로컬 AI 요약의 실제 엔진/모델 설치·추론이 미구현/미검증. 후보 조사와 mock은 기능 완료가 아니다. 공식 모델/release 경로 접근 403은 재시도나 우회하지 않는다. |
| T15 | AI 문장 다듬기 | 미착수 | independent-code | 로컬 문장 다듬기·미리보기/명시 적용·원본 이력의 실제 엔진/모델 설치·추론이 미구현/미검증. 후보 조사와 mock은 기능 완료가 아니다. 공식 모델/release 경로 접근 403은 재시도나 우회하지 않는다. |
| T16 | AI에게 내 메모 내용 질문 | 미착수 | independent-code | 사용자가 허용한 해제 메모만 로컬 검색·근거 메모 표시·자료 내 지시 불실행의 실제 엔진/모델 설치·추론이 미구현/미검증. 후보 조사와 mock은 기능 완료가 아니다. 공식 모델/release 경로 접근 403은 재시도나 우회하지 않는다. |
