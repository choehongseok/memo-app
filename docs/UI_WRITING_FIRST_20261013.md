# Windows 작성 중심 UI 후보 — 2026-10-13

**상태: 구현·Linux 검사·Windows 교차 빌드 완료, 기능 보존 검증 미완료 후보. 기존 시험판의 완전한 교체본·배포 가능 완료가 아니다.**

## 기준과 작업 경계

- 저장소 choehongseok/memo-app. 요청한 recovery/n02-20261012 커밋 `8a3400dabd1b306fce926c56ab51b32272d16e6f`를 실제 fetch/cat-file로 확보했다. 초기 checkout work/be6dbed는 변경하지 않았다.
- 별도 worktree `/workspace/memo-ui`, 브랜치 `ui/writing-first-20261013`에서 작업했다. 원격 push, PR, 배포, workflow 실행, 사용자 PC 접근 없음. 합성 검사 자료만 사용했다.
- 제품 변경은 MainWindow.xaml/.xaml.cs, StickyNoteWindow.xaml/.xaml.cs, StructuredNoteEditor.cs의 5개 Windows 파일이다. Core·암호화·키·잠금 정책·schema11/MEMOV002·Android 소스·134 ID 원장은 변경하지 않았다.
- 기능별 매핑과 선행 계획: [작성 중심 UI 계획](superpowers/plans/2026-10-13-writing-first-ui.md).

## 컨셉 PNG 직접 열람: 실패

요청된 `libfile_9580eba2293481918e381d53e25d6689` / `메모앱_작성중심_컨셉디자인.png`를 최신 Library materialization 흐름으로 현재 환경의 `/workspace/design`에 준비했다. workspace_path 없이 다운로드 전송이 반환됐으며 현재 helper로 파일 확보에 실패했다. 읽기전용 연결 진단 결과 **Tunnel connection failed: 403 Forbidden**이었다. 로컬 파일이 존재하지 않아 직접 이미지 열람·시각 대조를 수행하지 못했다. 우회·외부 환경 경로 사용 없음. 승인된 화면 설명과 소스 기능 매핑으로 구현했다. 전달된 PNG 3장은 소스 구조 설명 그림이며 실제 WPF 화면/컨셉 이미지 복제물이 아니다.

## 실제 적용 및 보존 근거

| 경로 | 새 UI | 근거와 미검증 경계 |
|---|---|---|
| 새 메모 | 기본 새 포스트잇 + 본문 포커스; 메모 메뉴에서 관리창 전용 작성 | 기존 Workspace.CreateNote/OpenSticky 재사용. 실패 시 다른 선택 메모를 열지 않는 동일 try 범위. native focus 검사 추가·컴파일, 실행 미완료 |
| 저장 상태·실패 | 관리창 Notice 유지; 모든 포스트잇 SaveState가 기존 SaveCoordinator.Status 관찰 | Changed 실제 저장 중/저장됨/실패 문자열 표시. 닫기 시 구독 해제. 합성 flush 실패/암호문 보존/잠금 후 ciphertext 후보 native 검사 추가·미실행. Core 실패 회귀는 실제 통과 |
| 잠금 | 복구코드 우선, 새 자료/문제 복구 별도 Expander | 기존 Unlock/Create/Inspect/Recover/Import/ExportPending과 비밀 폐기 유지. 잠금 시 포스트잇 폐기와 본문/Undo 정리 유지 |
| 닫기·삭제·복원 | 창 제목에 닫기는 숨김/삭제 아님; 메뉴 휴지통으로/메모 복원은 글자 유지 | 기존 window placement close와 Workspace delete 분리. close observer fault/메모 보존 native 검사 추가·미실행 |
| 항상 위·위치 고정·목록 고정 | 포스트잇 항상 위 체크 아이콘; 더보기 위치 고정/본문 접기; 관리창 목록 고정 | 서로 다른 기존 binding/Placement 메서드 보존. 아이콘 툴팁/접근성 이름/선택 상태, 키보드 포커스 제공 |
| 검색·폴더·태그·날짜·정렬·최근·저장 검색·일괄 | 관리창 3열 유지, 분류 우선; 상세/일괄은 펼침 | 원래 이름 있는 컨트롤/핸들러 모두 보존. 전체 Click 집합/컨트롤 이름 집합 보존 source 검사 통과 |
| 백업·복구·전체 이전·분기 비교 | 백업·복구·이전 메뉴; 자동 설정은 분류 아래 Expander | 기존 권한/선택/상태 enable 게이트와 busy/confirmation/file-picker 경로 유지. 용량 초과 Notice/확인 경고 유지 |
| 휴지통 자동 정리 | 백업 설정 안 기존 명령 유지 | 설명을 실제 동작으로 수정: 백업 후 오래된 본문·이력 제거, 첨부·삭제 관계·백업 유지, 완전 삭제 아님, 기본 꺼짐/잠금·재실행 해제 |
| TXT/Markdown/Excel 가져오기, TXT/PDF/Excel/Word 내보내기 | 명확한 가져오기/내보내기 메뉴 | 같은 핸들러/이름/IsEnabled. 기존 native setter reentrancy 검사 대상을 이동된 MenuItem으로만 수정 |
| 첨부·이미지·경로 연결·OCR | 양쪽 편집기의 첨부·이미지·OCR Expander | 기존 AttachmentPanel과 lifetime/preview/epoch/revocation 유지. 접근 경로 source/compile 보존, 실제 Windows/OCR 런타임 미검증 |
| 서식·문서 모드 | 서식 도구 펼침, 모드/분류/색은 별도 펼침 | 원래 toolbar/state/ConvertMode와 원본 이력 보존. 서식 버튼 keyboard focus 활성화. 실제 IME/선택/키보드 실행은 Windows 검증 필요 |
| 꾸미기·날짜/시계·트레이 | 색은 분류·색·문서 모드; 투명도는 포스트잇 더보기; 다크/글자/배율/창 정렬/표시/위젯은 보기 메뉴; 트레이/시작/종료 파일 메뉴 | 기존 ColorPicker, UiPreferences, Opacity/Topmost/Position bindings와 기기별 layout 저장 유지. 후속 개선을 새로 구현하지 않음 |

## 이번 세션의 실행 검사

| 검사 | 최종 결과 | 증거 |
|---|---|---|
| 원문·134 ID/이름·M06·범위 불변 | PASS | verify_preparation.py; 원문 27,417 byte와 SHA256 확인 |
| Python 전체 | **85 PASS, skip 0**, 15.824초 | memo-ui-reviewed-python.log; 최초 77, UI 첫 단계 82, 독립 검토 수정 후 85로 갱신 |
| Core 전체 | **88 PASS**, exit 0 | memo-ui-final-core.log; 이전 기록의 숫자를 재사용한 것이 아님. 수정 후 Core/계약 소스가 byte-identical이므로 반복 실행하지 않음 |
| OCR cleanup fault 단독 | PASS, exit 0 | memo-ui-final-cleanup.log; 실제 합성 자식 종료 실패의 후보 zeroing/admission 보존. OCR 제품 런타임 성공 증거는 아님 |
| 전체 솔루션 교차 빌드 | 경고 0·오류 0, 9.77초 | memo-ui-reviewed-build.log; 독립 검토 수정 후 수행 |
| 최종 추가 native 실패 검사 컴파일 | 별도 WindowsChecks 빌드 결과 참조 | memo-ui-final-native-checks-build.log; UI focus/fold/observer fault/실제 flush failure/잠금/candidate preservation 그룹 포함 |
| win-x64 자체 포함 publish | 성공 | memo-ui-reviewed-publish.log; Windows 실행/설치 성공과 구분 |
| diff·추적 파일 민감정보/금지 산출물 | 검사 후 source commit에 기록 | check_commit_safety.py; 실자료 없음. 전문 보안 감사 아님 |

첫 Core 시도는 SDK를 절대 경로로 실행했지만 검사 자식 `dotnet`이 PATH에서 발견되지 않아 중단됐다. PATH를 고쳐 기준 전체 및 최종 전체가 통과했다. 첫 UI 빌드의 XAML Header 중복은 제거했고, 추가 검사 cleanup의 nullable SetField 컴파일 오류는 reflection SetValue로 수정했다. 위 최종 결과에 이 실패들을 통과로 포함하지 않았다.

## 독립 검토 1회와 수정

별도 gpt-6.1-sol/high 검토자가 읽기전용 전체 diff를 검토했다. Critical 0 / Important 4 / Minor 1을 보고했다.

- 배율 1.75에서 외곽 스크롤 접근 회귀: viewport를 기준으로 유한 크기 Grid를 계산하고 외곽 양방향 스크롤을 복구했다. source RED→GREEN; native 최대 배율/스크롤 접근 검사 컴파일·미실행.
- 닫기 상태 Text setter 예외가 정리를 가로막음: observer/권한 폐기를 먼저 하고 본문 정리 뒤 상태 setter를 각각 보호했다. native DP fault/구독 해제/본문 폐기 검사 컴파일·미실행. 실제 native RED→GREEN 미실행으로 남긴다.
- 접힌 창에서 footer 공간 부족: footer를 DockPanel 첫 번째 Bottom 요소로 두어 먼저 공간을 배정하고, fold 시 보조 도구를 접는다. source RED→GREEN; native 위치/높이 검사 컴파일·미실행.
- 서식 버튼 키보드 접근: Focusable 활성화, source RED→GREEN; 모든 명령의 실제 keyboard focus native 검사 컴파일·미실행. 물리 키 입력은 미완료.
- Minor의 별도 백업 preview RestoreButton DP descriptor가 잘못 MenuItem으로 바뀐 부분은 Button으로 되돌렸다. 기존 검사 의미 보존을 위해 수정했다. 별도 재검토는 요청하지 않았다.

## 남은 Windows 검증·패키징

Linux에서 새 WindowsChecks 실행을 시도했으나 `Microsoft.WindowsDesktop.App` 실행 프레임워크가 없어 시작하지 못했다. 실제 GUI/native 그룹은 **0개 실행**이다. 과거 Windows 86개/기타 실행 숫자를 이번 결과로 사용하지 않았다.

미실행: 실제 WPF 실행·잠금/해제·저장 성공/실패·여러 메모창·닫기/삭제/복원·재실행·기기별 창 복원·한글 IME/조합·혼합 DPI/배율·키보드 메뉴/서식·첨부/경로/이미지·백업/복구/분기·TXT/Office/PDF·OCR·실제 설치/포터블·네트워크 관찰·앱 생성 PDF viewer. 새로운 `--writing-ui-only`와 기존 전체 WindowsChecks는 허가된 별도 Windows 환경에서 실행해야 한다. 사용자 PC 승인은 만료됐으므로 이 작업에서 사용하지 않았다.

**OCR 엔진 재사용 확인:** 지정 Git tree와 현재 workspace에는 검증된 tesseract.exe/models.zip/native-build.json 묶음이 없다. Library의 기존 시험판/Windows 묶음/바이너리 명칭 및 memo-app 검색에서도 재사용할 검증된 Windows 묶음을 식별하지 못했다. 기존 기록에 Actions Forbidden이 명시돼 해당 자료를 재조회/다운로드하거나 우회하지 않았다. build_native_ocr.py는 MSVC Windows 전용으로 현재 Linux에서 기존 방식으로 생성할 수 없다. 메뉴/소스 연결은 보존되나 이번 후보 ZIP에는 OCR 실행 엔진이 **포함되지 않으며 OCR 제품 실행이 가능하다고 주장하지 않는다.**

## 전달물과 후속

`/workspace/deliverables/memo-ui`에 기능 보존 검증 미완료 win-x64 후보 ZIP, 이력 bundle/patch, 기능 매핑·검사 보고서, Windows 검사 소스/교차 빌드 출력, 로그, 소스 설명 PNG 3장, 파일 SHA256 목록을 보존한다. 후보는 새 합성 전용 폴더로 분리된 포터블 방식만 이후 검증하며, 기존 설치/자료 폴더의 완전한 교체본으로 전달하지 않는다.

후속 색상·투명도·배경 이미지 요구: 기존 6색 메모/다크 모드/글자·배율/포스트잇 투명도와 기기별 opacity 저장은 소스에 구현돼 접근을 유지했다. 사용자 지정 팔레트/세밀한 꾸미기 개선은 이번에 추가하지 않았다. 메모 배경 이미지 선택 기능은 이 기준 소스에서 확인되지 않으며 rich 문서의 본문 이미지 기능과 별개다. 후속 범위는 팔레트·투명도 UX/범위 요구 확인 및 배경 이미지 별도 설계·호환성 검토이며 이번 UI 검증을 대체하지 않는다.
