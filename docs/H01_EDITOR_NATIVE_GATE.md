# H01 이미지 문서 본문 편집기 — Windows 게이트

기존 전달 시험판은 `35e01a00`/artifact `11659571081`이며 이미지 문서 본문은 읽기전용입니다. 아래 후속 소스·검사 결과를 이 설치 파일에 소급하지 않습니다. 134 원문 ID 감사는 `CONTINUATION_ID_AUDIT.json`/`.md`를 따릅니다.

## 소스 1bb3b671의 실제 결과

[Windows 실행 157](https://github.com/choehongseok/memo-app/actions/runs/38025548981), job `114135519794`: 전체 실패 1. 빌드·전체 Core·고립 정리 및 기존 WPF/PDF/OCR/이미지·Main/Sticky 검사는 통과했습니다. 새 `rich-image-text-editor`는 `queued-composition`의 결합 assertion에서 실패했습니다. 그 이전 순서의 실제 주변 본문 편집·Undo/Redo·표·서식·체크목록·암호 저장/재실행·이미지 변조 거절·재진입 검사들은 통과했으며, 뒤의 setter/hide/lock 세 사례는 도달하지 못했습니다. 게시용 빌드·설치·업로드는 건너뛰었습니다.

이 결합 assertion은 원문 동일성·native graph 비움·Undo 비움을 한 번에 검사하므로 로그만으로 실패한 조건을 확정할 수 없습니다. 해당 fixture는 StartComposition과 CompleteComposition을 동기적으로 호출한 **뒤** 호스트를 폐기했습니다. 정상 완료가 먼저 적용됐을 가능성은 소스 기반 가설이며, 폐기 후 원문 부활 또는 제품 결함을 확인한 증거로 단정하지 않습니다.

## 검사 순서 보완

제품·Core·Main/Sticky 코드는 바꾸지 않고 검사만 보완했습니다.

- 정상 완료 후 폐기: 실제 focus와 동일 조합의 Start/Update/Complete·본문 반영을 확인합니다. 폐기 직전 원문/편집 버전/content 버전/preview epoch를 직접 고정하고, 폐기 뒤 그 값과 raw 이미지·암호 파일 보존을 요구합니다.
- 실제 queued 완료: Manager.CompleteComposition을 먼저 예약한 뒤 호스트 폐기를 끝내고 해당 task를 기다립니다. 실제 동일 조합 이벤트 횟수·순서·폐기 완료 이후 이벤트를 요구합니다. 폐기 중 Windows가 먼저 완료해 버리는 경우도 통과로 바꾸지 않습니다.
- 두 경우 모두 원래 graph와 현재 graph의 본문·blocks 제거, Undo 차단과 실패 시 task/조합/handler/key 정리를 유지합니다. 안전한 숫자·boolean 진단으로 실패한 조건을 구분합니다.

검사 소스 SHA256 `a4526731ecff915371ef0b28d5a1ffaf547bd324b446de6b1259ccd788cf106c`. 독립 읽기 전용 POST에서 추가 Critical/Important는 없었습니다. 이 검토는 native 실행 증거가 아닙니다. 새 native 결과 전 Main/Sticky 활성화는 계속 보류합니다. 물리 한글 IME·실제 사용자 수용은 별도입니다.

## be6e232b의 실제 후속 결과

[Windows 실행158](https://github.com/choehongseok/memo-app/actions/runs/38026235730), job `114137558089`: 새 `rich-image-text-editor` group 전체 통과. 정상 완료 control은 이벤트 Start1/Update2/Complete3/폐기4와 폐기 전 실제 source 변화·편집/content/epoch 각+1, 폐기 후 정확한 상태 보존을 확인했습니다. 실제 예약 완료는 Start1/Update2/폐기3/Complete4, 폐기 전 원문 변화 없음·각 변화0, 폐기 완료 후 이벤트와 원문 동일성·원래/현재 graph의 blocks/text0·Undo 차단을 확인했습니다. 뒤의 setter/hide/lock 사례도 통과했습니다. 합성 TextCompositionManager 증거이며 물리 OS IME 수용은 아닙니다.

전체 실행은 별도 기존 `ocr-linked-runtime`의 실행 파일 점유 IOException 한 건으로 실패했습니다. 이 group은 당시 stack을 기록하지 않아 정확한 단계와 원인은 확정하지 않습니다. 다른 native OCR 제품·pending races·network 관찰·PDF와 Core는 통과했으며 패키지/설치/업로드는 건너뛰었습니다. 다음 회귀에는 기존 8KiB 제한 stack 기록을 해당 group에도 적용합니다. 점유 검사를 완화하거나 임의 재시도·제품 수정을 넣지 않습니다.

독립 PRE 조건에 따라 Main/Sticky 연결 구현을 시작합니다. 동일 Dispatcher의 실제 편집 transaction/native event 종료 전 새 owner 생성은 지연하며, 폐기 여부와 실제 종료를 구분합니다. 원래 source 권한은 즉시 폐기하고 source 없는 단일 waiter와 generation으로 새 owner 요청만 허용합니다. 교차 창의 중첩 Dispatcher 처리·동일 own editor Undo·선택 유지·접기/숨김/잠금·오래된 이미지 버튼을 실제 native 검사로 확인해야 합니다. 현재 공개 사용자 호스트는 여전히 읽기전용입니다.

## Task3 호스트·합성 corpus 연결 소스

메인/고정 창에 빈 composite를 정확한 슬롯에 먼저 설치하고 source/context를 재확인한 뒤 내부 편집기와 이미지 action child를 연결합니다. Dispatcher 공통 phase가 실제 transaction/native event 종료를 기다리며, source 없는 weak-root waiter가 최신 owner 생성 요청만 전달합니다. 선택 변경은 loading 중에도 권한을 폐기하고, 정렬 Move는 실제 선택 객체와 own Undo를 유지해야 합니다. 오래된 버튼은 note/document/alt 참조와 handler를 폐기하며 잠금·숨김·접기 때 두 child와 graph/pixel을 각각 정리합니다.

`rich-image-text-host` 26 native 사례와 기존 product regression을 등록했습니다. `ocr-synthetic-corpus`는 원본 한 이미지의 고정 4조건을 실제 고정 엔진에 연결하고 변형 CER를 진단으로만 보고합니다. native 정확도 일반화 또는 CPU/RSS 측정 증거는 아닙니다. 교차 빌드는 0 warning/0 error로 통과했으나 독립 host POST 및 새 Windows 실행은 아직 대기입니다. 이전 35e01a00 설치 파일에는 이 변경이 없습니다.

독립 host POST에서 Main `Editor.DataContext`와 Sticky `DataContext`를 다른 note로 바꿨다가 돌아오는 실제 전환이 오래된 callback 권한을 유지할 수 있는 Important가 확인됐습니다. 현재 identity predicate만으로 완료된 away/back 이력을 알 수 없어, 두 root의 실제 DataContextChanged 때 기존 owner를 동기 폐기하고 exact association이 돌아온 뒤 fresh owner만 생성하도록 수정합니다. native 두 사례와 좁은 POST를 통과하기 전 소스 게이트 게시를 보류합니다.

Main/Sticky 실제 DataContextChanged의 즉시 단조 폐기·exact association factory/queued guard와 native 두 사례를 추가했습니다. 좁은 독립 POST는 추가 Critical/Important 없이 통과했으며 수정 후 교차 빌드도 0 warning/0 error입니다. 실제 Windows 26사례·corpus·전체 회귀는 다음 소스 게이트에서 확인합니다.

## 5d0fe3ad 실제 게이트159

[Windows run159](https://github.com/choehongseok/memo-app/actions/runs/38027695906), job114141949939: 전체 FAIL1, new host의 17번째 `old-buttons` 준비에서 Enter 뒤 image block index 이동 assertion 실패. 앞선16 사례는 도달·통과했고 오래된 버튼 동작과 이후9 사례는 미도달입니다. 원문 endpoint·focus·실제 명령 effect 진단이 없으므로 입력 준비 문제인지 제품 거절인지 확정하지 않습니다. 실제 Run 내부 caret·focus·CanExecute와 native/source 변화 증거를 추가하고 index 이동·raw image·stale 버튼 거절 oracle을 그대로 유지합니다.

기존 standalone editor/PDF/OCR/Core/cleanup 통과. 이전 OCR 실행 파일 점유 오류는 이 실행에서 재발하지 않았으나 앞선 오류 원인 규명을 뜻하지 않습니다. Corpus4 조건 실제 통과: normalized scalar errors0/30, raw errors6/42와 whitespace12→18 차이. 단일 합성 이미지 변형이며 broad accuracy·CPU/RSS 보장은 아닙니다. 패키지·설치·업로드는 건너뛰었습니다.

검사만 보완했습니다. 원래 Main editor focus·Run 내부 collapsed caret·명령 guard/CanExecute·실제 native 이벤트와 정확한 block/index+1·source/version 변화·동일 graph/placeholder·raw image 보존을 필수로 확인한 뒤 기존 오래된 버튼 oracle을 실행합니다. 독립 POST와 수정 후 교차 빌드0w0e 통과. 제품/Core/workflow는 같고 native 재실행 전 원인·전체 통과는 확정하지 않습니다.

## 6413559e 실제 게이트160

[Windows run160](https://github.com/choehongseok/memo-app/actions/runs/38028305456), job114143749976: 전체 FAIL1, case22 child-content-awayback. 실제 Enter/stale 버튼은 native TextChanged1/명령1·block4→5/image index1→2/edit+1·원문 변화·동일 editor/graph/placeholder를 확인했고, 표시 취소·setter/source/conceal도 통과했습니다. 뒤 child-editor/MainDC/StickyDC/lock은 미도달입니다. 기존 Core/PDF/OCR/corpus/editor는 통과했지만 패키지·설치·업로드는 건너뛰었습니다.

실제 stack의 Panel.GetVisualChild index1/count0와 소스의 visibility callback→Dispose→Children.Clear 경로가 구조 재진입을 확인합니다. [공식 WPF UIElement.cs v10.0.0](https://raw.githubusercontent.com/dotnet/wpf/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs)는 이벤트 뒤 cached child count로 순회합니다. 이는 loaded10.0.12 binary 동일성 주장이 아닙니다. 권한·원문·native text·Undo·caption·pixel은 즉시 폐기하고 구조 분리만 fixed native unwind 뒤 source-free weak 작업으로 진행하는 보완을 검토 중입니다. 실패한 bootstrap cleanup이 새 refresh wake까지 취소하는 소스 경로도 fresh root 요청 및 postIdle 새 owner 검사로 보완합니다. 아직 수정/native 완료 증거는 없습니다.

## Run160 native walk correction — source review, Windows pending

The fixed Dispatcher phase now counts both all-operation depth and native walk depth. Main Editor uses its actual DockPanel type; both StructuredHost mounts use fixed ContentControl subclasses. Owned host/editor/view/panel property and visual-parent/children boundaries retain native depth through base processing. Top-level Window property boundaries remain; Window.OnVisualParentChanged is sealed, so two impossible overrides caused crossbuild CS0239 and were removed. Main/Sticky are shown as top-level windows.

Retirement immediately drops source/session/current/action references and handlers, clears exact owned FlowDocument/Undo/body/caption/pixels, and defers only structural Panel/Content removal through weak static generation-checked work. Exact retired-mount comparison protects a replacement. Generic refresh coalesces one weak request and redraws only after native unwind; staged and published owned text are registered for redaction. Fresh authority still waits general quiescence. Pure-invalid bootstrap requests fresh current root generation; unexpected constructor/native exceptions remain fail-closed without retry loops.

27 host cases retain immediate sensitive-state oracles and require structural emptiness after actual unwind/Idle. Actual DataContext/child visibility callbacks pump nested dispatcher messages and require no cached-child detach or fresh editor before unwind; DataContext controls preserve an explicitly substituted mount. Setter/source controls require a fresh writable current editor after Idle. The generic full-view control uses two existing closed Main editor table-cell text edits with +1/+2 version/own-continuation witnesses, source-free request coalescing, full prior text redaction and exact current body/image nodes after unwind. Generic schema-v2 SetRichDocument denial is unchanged. Independent source POST found no additional Critical/Important; reviewer did not execute tests. New actual Windows gate remains required.
