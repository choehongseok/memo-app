# N02 rich v1 paragraph/run 표시용 PDF delta plan

> **For agentic workers:** 후속 구현은 `superpowers:executing-plans`와 실제 RED→GREEN을 사용한다. 이 문서는 계획만이다. 독립 PRE, 부모의 별도 구현 배정·빌드 조정 전에는 코드·검사·제품 활성화를 변경하지 않는다.

**Goal:** 기존 표시용 PDF에 canonical rich v1의 paragraph/run 서식만 추가한다. 선택 전체가 닫힌 지원 profile에 속할 때만 완성 PDF를 반환하며, 미지원 문서를 평문 projection으로 바꾸지 않는다.

**Architecture:** 기존 UI source authority 안에서 선택 전체의 정확한 원문을 bounded 불변 DTO로 캡처한다. UI dispatcher의 기존 `TextFormatter`/페이지 renderer를 제한된 run properties로 확장하고, 기존 `PdfRasterDocumentBuilder`에 한 페이지씩 RGB를 전달한다. 마지막 파일 worker는 기존 완성 `PreparedTextExport`/path/token/backend만 받는다.

**Tech Stack:** 현재 .NET/WPF/BCL, `RichDocumentCodec`, `TextTransfer`, `TextFormatter`, `DrawingGroup`, `RenderTargetBitmap`, 기존 고정 RGB PDF writer. 새 dependency·font·decoder·서비스·schema는 없다.

**Spec:** `SOURCE_PROMPT.txt` N02의 원문은 “PDF 내보내기”이며 plain 전용 제한은 없다. paragraph/run fidelity는 원래 rich 요구와 원본 보존 요구를 함께 읽은 제한된 구현 범위다. `N02_VISUAL_PDF_PLAN.md`의 canonical rich v1 후속 단위를 구체화하며 전체 N02 완료나 원문의 별도 픽셀 동일성 요구를 주장하지 않는다.

## Global constraints

- 기존 검색 가능한 `PdfTextTransfer`, 표시용 plain 출력, 기본 Text 모드, 기본 No 평문 확인, vault 외부 새 로컬 `.pdf`/`CreateNew`를 유지한다. UI는 지원 rich paragraph 서식 범위와 raster의 검색·복사 텍스트 부재/외부 OCR 가능성/96 DPI 한계를 명시한다.
- 허용 선택은 plain 또는 아래 rich v1 profile이다. 하나라도 list/checklist/table, rich v2(이미지 없는 v2도 포함), Markdown, opaque/unknown 문서·필드, unsupported run이면 **선택 전체를 거절**한다. 건너뛰기·부분 출력·자동 plain/Text fallback은 없다. 이미지/첨부 bytes를 읽거나 source root를 활성화하지 않는다.
- 100개 서로 다른 live note, title 256 UTF-16 units, body/projection 65,536 units, rich source UTF-8 1 MiB/문서, codec의 depth 16/nodes 1,024/runs 4,096/token 16,384 한도를 그대로 적용한다. 선택 전체의 검증된 `TextTransfer` payload 합계와 exact rich JSON 합계를 각각 16 MiB 이하로 검사한다. checked 계산과 전체 preflight 뒤에만 native 페이지 준비를 시작한다.
- 기존 A4/96 DPI/794×1123 px, 40 pt margin, 최대 256쪽/최종 16 MiB 및 압축 중 xref/trailer 예약 한도를 유지한다. 한 페이지 Pbgra32와 RGB만 유지한다. `Finish` 복사 때 기존 문서 buffer와 최종 buffer가 잠시 공존하는 예산도 그대로다.
- 원래 `StyledDocument.SourceJson`, projection/body, 객체·ref, OCR, 현재/history revision, 암호 파일을 수정하지 않는다. 저장·schema·SaveCoordinator 권한 API를 새로 만들지 않는다. DTO 유효성은 원래 session 권한의 대체 증거가 아니다.
- WPF native/GC/GPU 사본의 완전 삭제를 보장하지 않는다. 소유 managed raster/압축/후보 byte는 성공·거절·취소·예외에 zero/dispose한다. 완성 PDF는 실제 파일 write가 종료될 때까지 소유하며 lock으로 key release를 지연하지 않는다.

## 닫힌 source/style profile

`Mode == rich`, `SchemaVersion == 1`, `RichDocumentCodec.Inspect(...).Supported`, 검증된 projection과 `NoteDraft.Text`의 ordinal 일치를 모두 요구한다. root는 canonical nodes만, 각 node는 paragraph/type/runs만 허용한다. 각 run은 text/bold/underline/strike/fontFamily/fontSize/foreground/background만 허용한다. `link` 필드가 있는 run도 첫 단위에서는 전체 거절한다. 단순 URL·HTML처럼 보이는 원문 text는 inert 문자로 그릴 수 있지만 hyperlink/action/HTML 실행으로 만들지 않는다.

Effective defaults는 rich body의 Segoe UI 14 DIP, normal weight, 검정 foreground/transparent background, 장식 없음, left/LTR, paragraph 후 4 DIP이다. 명시 size는 기존 8..96 DIP만 허용한다. 제목은 기존 plain 제목 style와 구분 규칙을 유지한다. plain-only 선택은 기존 `RenderPlainAsync` 경로 그대로다. mixed 선택도 하나의 writer에서 순서대로 그리며 plain note의 기존 배치를 보존한다. live editor의 theme/zoom/폭을 캡처하거나 반영하지 않는다.

지원 style은 bold, underline, strike, 고정 family/size, codec이 허용한 hex foreground/background뿐이다. 색은 닫힌 `SolidColorBrush`로 생성·freeze하며 alpha는 흰 페이지 위에서 합성한다. italic, alignment, custom typography/effect, font URI, 임의 XAML/FlowDocument 역직렬화는 추가하지 않는다. 텍스트·색·장식을 임의로 대체해 성공시키지 않는다.

paragraph 전체 원문을 먼저 연결하여 `StringInfo` grapheme 경계와 effective style span을 계산한다. grapheme 내부 run 경계는 **동일 effective style이면** formatter용으로만 합칠 수 있다. 서로 다른 style이면 cluster를 임의 분할하거나 한 style로 덮지 않고 전체 거절한다. 기존 max cluster 128 UTF-16 units, Unicode/RTL/bidi/ZWJ/VS/modifier 거절 정책과 native에서 입증된 단일 emoji/combining 범위를 유지한다.

CRLF/CR→LF와 tab→4 spaces는 선언된 배치 변환만이다. 연결한 paragraph에 원래 source offset/style mapping을 유지하여 run 경계의 CRLF를 두 newline으로 만들지 않는다. CRLF 내부 충돌 style도 거절한다. NFC/NFD 변환·trim·truncation은 없다. 빈 paragraph, 연속/후행 공백과 마지막 newline을 검증하며 모든 formatter line의 source 소비는 양수다.

## Native font 및 페이지 ink 계약

허용 family는 이미 승인된 Windows native `Segoe UI`, `Malgun Gothic`, `Segoe UI Symbol`, `Segoe UI Emoji`에 한정한다. 현재 regular face gate는 plain 경로에 그대로 남긴다. rich용 별도 닫힌 face manifest는 family/실제 face/weight/stretch/style/simulation/승인된 Windows Fonts 파일 origin을 함께 고정한다. bold variant는 실제 대상 Windows의 설치 face를 독립 검사하여 PRE가 승인한 항목만 추가한다. 예상 파일명·가족 metadata만으로 허용하거나 설치 사용자 font를 받아들이지 않는다. 승인 face가 없거나 fallback이 요청 style을 보존하지 못하면 전체 거절한다. font 다운로드·embedding·추가 배포·라이선스 권한 추정은 없다.

각 `TextLine.GetIndexedGlyphRuns()`의 glyph 0, source/character coverage, cluster map, surrogate, actual resolved face와 requested effective style을 감사한다. glyph 수와 UTF-16 길이를 1:1로 가정하지 않는다. styled span마다 audit하며 Regular fallback이 bold를 잃은 경우도 실패다. 실제 font 검사는 caption/property 이름만으로 성공 처리하지 않는다.

기존 glyph ink만으로 underline/strike/highlight를 덮을 수 없다. line을 닫힌 임시 `DrawingGroup`에 원점 기준으로 그리고 glyph ink와 전체 drawing bounds를 합쳐 decorations/background까지 계산한다. baseline/advance·음수 top·아래 돌출과 trailing whitespace를 함께 고려한다. 최소 line advance는 기존 24 DIP이며 큰 size/ink는 line 높이를 늘린다. complete line ink가 페이지 폭·유효 높이보다 크면 잘라 그리지 않고 거절한다. 현재 page에 안 맞으면 draw 전에 다음 page로 이동한다. 단락 gap과 마지막 line도 256쪽 계산에 포함한다.

실제 DrawingVisual의 grayscale text rendering 정책과 premultiplied channel≤alpha 검증/white RGB 합성을 유지한다. 픽셀 alpha를 무조건 255로 요구하여 정상 native 글자를 거절하지 않는다. line/break/group/page/bitmap 수명을 제한하고 32 line 이하 batch와 페이지 yield 전후에 current/token을 확인한다. 모든 페이지의 native tree/bitmap을 누적 보관하지 않는다.

## Authority와 failure 경계

기존 `PdfOperationAuthority`를 mode 선택 이전부터 사용하고 확인·picker·source capture·native setter·yield·후보 반환·file write 전후에 pure current 검사를 유지한다. 원래 session/workspace, 정확한 selected note instance/membership/order, EditVersion와 source epoch를 묶는다. 편집, 동일 version `AcceptPrepared`, 선택 A→B→A, lock/conceal/close는 export별 linked token을 영구 취소한다. 캡처 전후의 current 검사가 exact immutable source와 살아 있는 원래 authority를 결합한다.

source DTO는 text/style/raw source 값만 가진다. NoteDraft/workspace/session/key/UI/owner callback 필드를 갖지 않는다. renderer의 `current`는 UI 준비 단계에서만 사용하고 파일 worker로 전달하지 않는다. 이미 보유한 admission/busy slot은 실제 준비 또는 파일 worker가 종료되기 전에 재사용하지 않는다. catch/finally에서 취소 요청만으로 실제 작업 종료를 주장하지 않는다.

늦은 font/glyph/ink 거절, 257쪽, 최종 압축 overflow, callback 예외, 취소 모두 builder를 fault/dispose하고 전체 candidate를 버린다. 정상 첫 note/page를 이미 만들었어도 `CreateNew`를 호출하지 않는다. 완성 후 write I/O 실패에는 부분 평문 파일이 남을 수 있다는 기존 안내를 유지한다. blocked `CreateNew`가 lock 뒤 반환해도 canceled token 확인 전 plaintext write를 하지 않는 기존 계약을 보존한다.

## Review focus

- run 내부/경계의 combining 및 CRLF를 style/source와 다른 문자로 그리지 않는가: Task 1의 offset/profile 검사와 Task 2 독립 styled oracle.
- bold fallback/simulation·사용자 font origin이 허용 family 이름 뒤에 숨지 않는가: Task 2 actual face manifest와 native negative tests.
- decoration/highlight/큰 size의 ink가 wrap/page 경계에서 잘리지 않는가: Task 2 drawing bounds 및 page별 RGB oracle.
- 선택 마지막 note 실패가 앞부분 파일을 남기지 않는가: Task 1 전체 preflight, Task 2 fault/zero, Task 3 `CreateNew` count 0.
- stale/selection round trip/lock 및 실제 file settlement가 기존 owner를 우회하지 않는가: Task 3 authority/reentry/worker field graph 검사.

## Task 1 — inert profile capture와 Core 계약

**Files:** 새 `src/MemoApp.Core/Transfer/RichParagraphPdfSource.cs`, 새 `tests/MemoApp.ContractChecks/RichParagraphPdfSourceChecks.cs`. 부모가 Contract `Program.cs`만 등록한다.

**Interfaces:** internal `RichParagraphPdfCapture.Capture(IReadOnlyList<NoteDraft>, CancellationToken)` → `ImmutableArray<RichParagraphPdfSource>`. Source는 Title/Text/Mode/OriginalDocument와 immutable Paragraphs를 가진 값 DTO다. `RichPdfParagraph`는 Runs, `RichPdfRun`은 Text/Style, `RichPdfRunStyle`은 effective Bold/Underline/Strike/FontFamily/FontSize/Foreground/Background 값이다. OriginalDocument는 exact raw source 보존 증거이며 native 역직렬화 입력이 아니다. caller thread/source authority는 기존 host가 담당한다.

- [ ] 배정 당시 고정 Core DLL의 byte 수/SHA와 실제 missing API RED를 남긴다. 새 selector `--pdf-rich-paragraph-capture-only` 등록은 부모에게 요청한다. frozen DLL 검사는 새 profile API 부재를 증명하며 Windows rendering RED로 주장하지 않는다.
- [ ] valid plain+rich, multiple styles/empty paragraphs, exact raw JSON/projection preservation을 검사한다. metadata/OCR/ref/history/workspace 및 실제 synthetic encrypted source bytes가 캡처 전후 동일해야 한다.
- [ ] v2 zero-image 포함/list/table/checklist/Markdown/unknown/link/잘못된 projection, late unsupported selected note, 중복/closed/deleted note, source/aggregate byte·run·Unicode/cluster 한도를 whole refusal로 검사한다. default와 명시 동일 style는 cluster 합치기가 가능하고 다른 style cluster/CRLF는 거절해야 한다.
- [ ] existing codec/TextTransfer 검증과 모든 checked aggregate budget을 먼저 적용하고 최소 DTO를 구현한다. 내부 검증은 권한 mint가 아니다. source를 정규화·repair·수정하지 않는다.
- [ ] 조정된 focused GREEN을 기록한다. 전체 Core는 부모가 source pause 후 한 번 실행한다.

## Task 2 — styled native formatter와 독립 oracle

**Files:** 새 `src/MemoApp.Windows/PdfVisualRenderer.RichParagraphs.cs`, narrow 기존 `PdfVisualRenderer.cs` partial/shared page engine seam, 새 `tests/MemoApp.WindowsChecks/PdfRichParagraphChecks.cs`. 기존 plain font policy/golden 출력과 PDF writer는 보존한다.

**Interfaces:** `RenderParagraphsAsync(IReadOnlyList<RichParagraphPdfSource>, Func<bool> current, CancellationToken, Action<byte[]>? ownedBuffers = null)` → `Task<PreparedTextExport>`, UI dispatcher only. optional observer는 cleanup test seam이며 production default null이다. 기존 builder/raw page/완성 후보 API는 변경하지 않는다.

- [ ] actual Windows에서 styled reference RED를 먼저 확보한다. reference는 fixture가 수동 지정한 native WPF TextBlock/Run/style로 따로 구성하며 제품 style parser/mapper/audit/compositor를 oracle에 재사용하지 않는다. 같은 fixed page/line 정책·grayscale를 적용하되 실제 independent RGB 비교를 수행한다. nonwhite count만으로 서식 fidelity를 증명하지 않는다.
- [ ] 승인 regular와 실제 승인 bold face, 색/alpha highlight/underline/strike, size 8/96, mixed Hangul/Latin/combining/simple emoji, same-style split grapheme, 공백/탭/CRLF, wrap/마지막 line/page boundary를 fixture로 검증한다. native glyph coverage와 decoration/background ink를 각각 단언한다.
- [ ] unsupported bold fallback/simulation/rogue origin/unknown font, conflicting grapheme style, unsupported sequence, 너무 넓거나 높은 line ink를 whole refusal로 확인한다. regular plain 정책을 느슨하게 하여 통과시키지 않는다.
- [ ] 제한된 `TextRunProperties`/paragraph source, strict face policy, complete drawing bounds와 한 페이지 render를 구현한다. page별 copied RGB를 독립 PDF stream inflate 결과와 exact 비교하고, 최종 PDF는 독립 viewer의 96 DPI raster와 reference로 다시 확인한다. RGB exact 비교와 viewer tolerance는 구분하며 실패를 숨기려고 tolerance를 넓히지 않는다.
- [ ] real populated BGRA/RGB/후보를 observer로 추적하여 완료·중간 cancel·후반 거절·observer throws에 zero를 확인한다. 32-line yield 중 source cancel, 마지막 note font 실패, page/output cap 실패에서 전체 결과 없음과 실제 native 작업 settlement를 확인한다.
- [ ] 기존 plain/emoji/combining/page/RGB 및 fixed writer 회귀를 유지한다. native face manifest와 독립 viewer 합성 결과를 독립 POST reviewer에게 넘긴다. Linux build 성공을 native PASS로 기록하지 않는다.

## Task 3 — 기존 명시 route 통합과 gates

**Files:** narrow `MainWindow.VisualPdfExport.cs`, `PdfExportModeChoiceWindow.cs` 안내, 기존/new Windows checks. 부모가 runner/ledger/status/packaging만 갱신한다.

- [ ] 선택 전체 capture와 renderer를 기존 authority route 안에 연결한다. mode Text default/기본 No/외부 CreateNew/실제 write await를 유지한다. plain+지원 rich paragraph만 가능하고 list/table/v2/Markdown은 전체 거절한다는 안내를 테스트한다.
- [ ] valid first note+invalid last note, renderer 실패/취소/overflow에서 file backend `CreateNew` 0을 단언한다. 원본 암호 파일/문서/metadata 불변을 비교한다. text PDF 회귀와 plain-only visual pixel 회귀를 유지한다.
- [ ] confirm/picker/native setter/yield reentry, source edit/same-version AcceptPrepared/selection round trip/lock/close, blocked CreateNew late return을 실제 callback/작업으로 검사한다. 잠금 즉시 key release, file worker compiled graph의 owner/UI/key 없음, prepared byte의 write 중 생존과 actual settlement 뒤 zero를 확인한다.
- [ ] 후속 focused 명령은 `dotnet run --project tests/MemoApp.ContractChecks -c Release -- --pdf-rich-paragraph-capture-only`; actual Windows native group은 `pdf-rich-paragraph-export`로 부모 runner에 등록한다. 부모가 조정한 full Core/전체 Windows/설치 산출물 gate와 기존 134 ID preparation 검사를 통과시킨다. 명령·SHA·exit·환경·합성 PDF/reference 산출물을 실제 증거로 남긴다.
- [ ] 독립 PRE→구현→POST→actual native/viewer/host gate 순서를 지킨다. H01 host/corpus 선행 gate가 닫히기 전 제품 활성화나 이 계획의 구현 배정을 앞당기지 않는다. table/Markdown/v2 image fidelity는 별도 단위로 남긴다.

## 승인·현재 상태

현재 결과는 계획 문서뿐이며 새 API/검사/native support는 아직 없다. 원래 승인된 N02 범위의 제한된 후속 단위이고 새로운 서비스·font 배포·저장 형식·사용자 중요 결정을 요구하지 않는다. routine UI 범위 안내는 구현에서 명시한다. actual bold face allowlist와 styled native/viewer oracle은 독립 PRE 및 후속 구현 gate이며 추정으로 완료 처리하지 않는다.

독립 read-only PRE(2026-10-10): 계획 Critical/Important 없음, 조건부 clear. H01/corpus gate 종료·actual Windows bold face inventory/origin/unsimulated approval·independent styled raster/viewer/plain regression이 구현 활성화 조건이다. 현재 PdfOperationAuthority.Current의 count+Contains는 exact ordered selection 증거가 아니므로 새 단위에서는 captured workspace/source identity와 ordered SequenceEqual 및 실제 reorder/round-trip 거절 검사를 구현해야 한다. 이 기록은 구현/빌드/native 성공이 아니다.

2026-10-10 선행 actual H01/corpus gate: 2c6ae370 / Windows run16338031611352 전체 SUCCESS(27host/잠금/전체Core/native/패키지/실제 설치 포함, uploadSKIP). Task1 및 test-only font inventory 활성화. Focused missing-API RED: `dotnet run --project tests/MemoApp.ContractChecks -c Release -- --pdf-rich-paragraph-capture-only`, 실제 exit134, `N02 inert rich paragraph PDF capture API missing`; `/tmp/memo-n02-rich-capture-red.log`. 구현 전 Core DLL 2580992bytes/SHA256 `da951a359343aaf75dd4e918b88ee2bbee31128c470fbabdd214d727881ca9ee`, frozen `/tmp/memo-n02-preimplementation-core.dll`. 이 RED는 Core API 부재이며 Windows 서식 렌더링 RED가 아니다. 실제 bold face approval/renderer/native gate는 별도 대기 중이다.

Test-only actual face inventory source: `PdfSystemFontInventoryChecks.cs`, group `pdf-system-font-inventory`/`--pdf-system-font-inventory-only`. Four fixed families, all eight Normal/Bold requests, maximum64 installed faces per family; bounded requested/actual family/face/weight/stretch/style/simulation/validated system URI, unsupported origin redacted. Actual regular-as-bold and explicit WPF BoldSimulation negative controls; `rendererApproval=False`. Independent source PRE/POST Critical/Important 없음, actual Windows collection/face approval 대기. 기존 regular policy와 renderer는 변경하지 않는다.

Task1 최종 독립 source-only POST Critical/Important 없음. 실제 focused GREEN exit0 `/tmp/memo-n02-rich-capture-green.log` (RED와 같은 selector), 원문/raw 문서/projection/서식/grapheme/CRLF/전체선택거절/한도/합성 암호 파일·이력·OCR 보존 검사 통과. 실제 WindowsChecks 교차 빌드0warning/error,6.74sec `/tmp/memo-n02-capture-font-inventory-crossbuild.log`. 전체Core 진행 중이며 Windows native inventory/서식 renderer 지원 증거는 아직 아니다.

조정된 전체 Core 실제 exit0 `/tmp/memo-n02-rich-capture-full-core.log`. 새 Task1 포함 전체 회귀 통과이며 native face/renderer 증거와 구분한다.

2026-10-10 actual 소스 `e2bff1c210f8fe9539c92f0d537e7dc637e69d64` / tree `b1ad7885bf4223d84bcedc2f89f9cfb862882e1c`, [Windows 실행164](https://github.com/choehongseok/memo-app/actions/runs/38032655607), job114156646983 전체 SUCCESS. 새 N02 Core capture 및 전체 native/Core/H01호스트27/PDF/OCR/백업/413파일 패키지/실제 설치 EXE 검사 통과, uploadSKIP. Font inventory32installed/8requests/8resolved/6unsimulated/2unsupported. 실제 Windows Fonts의 Segoe UI Regular400 SEGOEUI.TTF·Bold700 SEGOEUIB.TTF, Malgun Gothic Regular400 MALGUN.TTF·Bold700 MALGUNBD.TTF, Symbol400 SEGUISYM.TTF·Emoji400 SEGUIEMJ.TTF: exactfamily/face/Normalstyle/stretch5/simulationNone/origin검증. Symbol·Emoji700은 같은Regular파일 BoldSimulation으로 거절. 독립 검토가 이6face만 승인했으며 실제 glyphcoverage·서식RGB·viewer·host권한은 별도대기. Task2 tests-first 활성화, 실제 native reference+기존rich visualroute 거절 RED부터 확인. 이 결과는 서식 PDF renderer 구현·전체134·물리 사용자 수용·새 산출물 업로드 완료가 아니다.

2026-10-10 N02 Task2 tests-first source: 독립 수동 WPF TextBlock/Run reference의 실제 text range·RGB/ink/red/alpha-highlight positive controls 뒤 기존 Main visual rich route에서 consent/picker 및 source/version/epoch/cipher 불변을 확인하고 지원 paragraph 성공을 요구하는 의도된 미구현 RED. 최종 styled PDF/reference RGB·decoration/bold fidelity·viewer 검증은 아직 없다. 제품·Core·plain renderer 변경 없음. 독립 좁은PRE/POST clear, WindowsChecks교차빌드0warning/error16.02sec `/tmp/memo-n02-rich-native-red-crossbuild.log`. 실제Windows RED는 대기; 이전 run164전체통과와 새RED를 분리한다.
