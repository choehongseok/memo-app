# N02 표시용 PDF 첫 수직 기능 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. 이 인계는 계획 문서만 작성하는 단계다. 현재 코드·빌드·패키징은 실행하지 않는다. 구현을 재개할 때 부모의 기존 실행·검증 조정을 따른다.

**Goal:** 기존 검색 가능한 제한 평문 PDF를 유지하면서, 검증한 plain Unicode 제목·본문을 WPF가 그린 페이지 이미지로 담는 별도 표시용 PDF를 명시적으로 내보낸다.

**Architecture:** UI 소유 스냅샷에서 검증·줄 배치·페이지 렌더를 수행하고, 페이지별 owned RGB를 Core 고정 Image XObject PDF writer에 전달한다. 페이지 한 장씩 압축하여 전체 16 MiB 후보를 먼저 완성하며, 파일 worker에는 완성된 `PreparedTextExport`, 목적지, 취소 token, 파일 backend만 전달한다. glyph·텍스트·live editor·session·키를 worker로 전달하지 않는다.

**Tech Stack:** 기존 .NET 10/WPF/BCL의 `TextFormatter`, `TextLine.GetIndexedGlyphRuns`, `RenderTargetBitmap`, `CopyPixels`, `ZLibStream`과 기존 `PreparedTextExport`/`TextTransfer.WritePrepared`. 새 라이브러리·글꼴·서비스·PDF 입력 파서는 추가하지 않는다.

**Spec:** `docs/SOURCE_PROMPT.txt` N02/평문 내보내기 주의, `docs/PRODUCT_SPEC.md`, `docs/FEATURES.json` N02, `docs/NEXT_SCOPE.md`, `docs/FEATURE_GAP_AUDIT.md`, 기존 `docs/plans/2026-10-09-pdf-text-export.md`. N02 원문에는 평문 전용 제한이 없으며 이 기능도 전체 N02 완료가 아니다.

## Global Constraints

- 기존 `PdfTextTransfer`의 고정 D2Coding/ToUnicode 텍스트 PDF와 관련 회귀를 유지한다. 사용자 선택 없이 raster 모드로 바꾸거나 미지원 문자를 대체하지 않는다.
- 선택지는 `텍스트 PDF (지원 문자·검색/복사 가능)`와 `표시용 PDF (텍스트 검색·복사 미지원)`로 구분한다. 표시용 PDF도 평문 파일이다. 외부 OCR까지 금지한다는 뜻의 “복사 불가” 보장은 하지 않는다.
- 표시용 첫 단위는 plain 메모의 제목·본문만 지원한다. rich/Markdown은 명시 거절하며 자동 plain 변환·원문 projection으로 우회하지 않는다. H01 rich 문서 v2와 PNG 이미지, canonical rich v1, rendered Markdown은 후속 단위다.
- 기존 내보내기 기본 No 확인, 정확한 선택·소스 버전·session/source/UI epoch, vault 외부 새 로컬 `.pdf`, `CreateNew`, 기존 파일 보존을 유지한다. 잠금과 취소는 전체 작업을 취소한다.
- A4 210×297 mm, 고정 96 DPI. DIP 페이지 크기는 `210/25.4*96` × `297/25.4*96`; bitmap은 각 축을 올림하여 794×1123 px로 제한한다. PDF MediaBox는 동일 물리 크기를 point로 환산한다. 흰 배경에 렌더하며 투명도를 잘못 제거하여 검게 만드는 출력은 거절한다.
- 100메모, 검증 입력 UTF-8 합계 16 MiB, 최종 PDF 16 MiB, 최대 256쪽. 한 페이지 raw Pbgra32는 794×1123×4, RGB는 794×1123×3으로 checked 계산한다. 모든 페이지 raw raster를 보관하지 않는다.
- 출력 크기뿐 아니라 압축 중 누적 byte·PDF 객체 수·페이지 수를 할당 전에 검사한다. 전체 후보가 성공한 뒤에만 목적지 파일을 만든다. 마지막 페이지 실패로 정상 일부 문서만 저장하지 않는다. 최종 파일 I/O 실패 뒤 부분 평문 파일은 남을 수 있다.
- 임의 XAML/HTML/FlowDocument 역직렬화, 사용자 font URI, URI 이미지, 외부 리소스, 링크 실행, live editor 화면 캡처는 금지한다. 폰트 파일을 출력 PDF에 embedding하거나 앱에 추가 배포하지 않는다.
- managed byte 배열은 종료·취소·예외에 zero/dispose한다. WPF 내부 native/GPU/GC 문자열·이미지 복사본의 완전한 secure erase는 보장하지 않는다. 폰트/픽셀만으로 키가 필요하지 않은 마지막 파일 작업에서 잠금 키 종료가 지연되면 안 된다.
- 실자료 대신 합성 fixture를 사용한다. 자체 검토와 독립 검토, Linux Core 결과와 실제 Windows WPF/viewer 결과, 구현 진행과 사용자 수용을 구분한다. 원문 134 ID 및 원장은 이 계획으로 변경하지 않는다.

## Review Focus

- glyph 0 없이도 emoji/클러스터가 잘못 분리되거나 다른 모양으로 그려질 수 있다. Task 2의 native glyph·독립 픽셀 oracle이 지원 범위를 결정한다.
- combining mark의 ink가 line advance 밖에 있거나 페이지 끝에서 잘릴 수 있다. Task 2가 ink bounds·마지막 줄·긴 grapheme 거절을 검증한다.
- 압축이 잘되는 fixture만 통과해 실제 RGB 메모리를 과다 할당할 수 있다. Task 1이 incompressible RGB·누적 출력·257쪽·단일 raw 페이지 수명을 검증한다.
- UI yield 사이의 편집·동일 버전 스냅샷 수락·선택 변경·잠금이 원래 작업을 무효화한다. Task 3에서 각 페이지와 파일 생성 직전 권한을 검증한다.
- 현재 선택 membership만 비교하면 선택을 다른 메모로 바꿨다가 되돌린 사건을 놓친다. 준비 단계뿐 아니라 blocked `CreateNew` 동안의 edit·selection round trip·동일 버전 `AcceptPrepared`도 export별 linked cancellation을 영구 취소해야 한다. UI/source owner를 파일 worker에 전달하여 검사하지 않는다.
- bitmap renderer/파일 worker가 원래 session·NoteDraft·UI·키 owner를 보유할 수 있다. Task 3에서 compiled worker field graph와 잠금 즉시 key release를 확인한다.

## 첫 단위 지원 경계

폰트 family는 기기에 설치된 고정 후보 `Segoe UI`, `Malgun Gothic`, `Segoe UI Symbol`, `Segoe UI Emoji`로 한정한다. 실제 shaping 결과의 각 `GlyphTypeface`도 허용 family인지 확인하여 암묵적 fallback으로 다른 설치 글꼴을 가져오지 않는다. 확인된 Windows 시스템 글꼴만 사용하고, 임의 face/file/URI 선택 UI는 만들지 않는다. 미설치 face·알 수 없는 fallback·glyph 0·불완전 source coverage는 파일 생성 전에 명시 거절한다. `GlyphTypeface.FontUri`가 외부/네트워크 리소스인 결과는 거절하며 이 값으로 추가 리소스를 로드하지 않는다.

family metadata 일치만으로 Windows 시스템 글꼴 origin/권리를 증명하지 않는다. 실제 resolved face의 승인된 로컬 시스템 설치 origin을 확인하고 사용자 설치·예상하지 않은 파일 origin은 거절한다. 기존 공식 Windows font FAQ는 제3자 설치 글꼴의 권리 근거가 아니다. font 파일 다운로드·embedding·추가 배포는 이 단위에 포함하지 않는다.

원문 Unicode를 정규화하거나 바꾸지 않는다. 잘못된 surrogate와 제어 문자는 거절하되 CR/LF/탭은 기존 읽기용 줄 배치 및 탭 4공백 정책을 명시한다. 실제 native 검사 대상은 한글·영문·기존 한자, `e\u0301` 등 결합 문자, 분해 한글, 단일 supplementary emoji `😀`, 글꼴이 실제 지원하는 supplementary 문자다. 지원 font 이름만으로 모든 문자를 허용하지 않는다. 합성 검사에서 확인하지 않은 ZWJ/variation selector/국기·피부색 조합, bidi/RTL, 컬러 emoji 표현은 첫 단위에서 거절하거나 미지원으로 명확히 구분한다. WPF가 그리는 확인된 흑백 emoji 모양을 허용할 수 있으나 원래 컬러 외형 보존을 주장하지 않는다.

`TextFormatter.FormatLine` 결과의 `TextLine.GetIndexedGlyphRuns()`를 검사하며, 원래 source index/length와 glyph coverage를 비교한다. 단순 UTF-16 `char` 분할로 줄을 끊지 않는다. 표시할 수 없는 긴 클러스터를 잘라 그리지 않고 거절한다. 줄 advance와 ink bounds를 함께 고려하여 다음 줄·페이지 위치를 계산한다. 빈 줄·선행/후행/연속 공백을 trim하지 않으며, note 간 구분과 제목·본문 순서를 고정한다.

coverage는 UTF-16 글자 수와 glyph 수의 1:1 비교로 판단하지 않는다. surrogate·combining·ligature의 실제 cluster mapping과 원문 범위를 감사하고, 합성 paragraph terminator 및 선언한 CR/LF/탭 배치와 공백을 missing glyph와 구분한다. 실제 source 소비가 매 줄 양수로 진행되는지 확인한다. 지원을 입증하지 않은 bidi/format/variation selector/ZWJ/modifier는 formatter 호출 전 명시 거절하며 긴 cluster를 잘라 한도에 맞추지 않는다.

## 파일 구조와 인터페이스 후보

아래는 후속 구현 때 사용할 구체적인 경계다. 현재 존재하지 않는 API를 구현 완료로 취급하지 않는다.

| 파일 | 역할 |
|---|---|
| 새 `src/MemoApp.Core/Transfer/PdfRasterTransfer.cs` | 외부 PDF/font 파싱 없이 고정 RGB Image XObject·Flate·xref 출력과 용량 검사 |
| 새 `src/MemoApp.Windows/VisualPdfRenderer.cs` | detached plain 스냅샷, 허용 font, native glyph audit, 한 페이지 WPF 렌더 |
| 기존 `src/MemoApp.Windows/MainWindow.PdfExport.cs` 및 `MainWindow.xaml` | 두 명시 모드와 기존 내보내기 권한·안내 연결 |
| 새 `tests/MemoApp.ContractChecks/PdfRasterExportChecks.cs` | 고정 PDF 구조·RGB·한도·소유 byte zero·취소 |
| 새 `tests/MemoApp.WindowsChecks/VisualPdfExportChecks.cs` | native shaping/pixels·모드·stale/lock/선택·worker 검증 |
| 기존 PDF 검사·Windows/Contract `Program.cs` | 기존 텍스트 PDF 회귀 유지와 새 group 등록 |

Core 후보 API는 `PdfRasterDocumentBuilder(CancellationToken token)`, `void AddRgbPage(int width,int height,ReadOnlySpan<byte> rgb)`, `PreparedTextExport Finish()`, `Dispose()`다. Add는 RGB 길이·794×1123 크기를 확인하고 같은 호출 안에서 압축을 끝낸다. 호출자가 raw RGB를 종료 시 지울 수 있으며 builder는 raw 페이지를 보유하지 않는다. Finish만 완성 byte 소유자를 반환한다. PDF font/text/URI/annotation/action 객체는 만들지 않는다.

Add 또는 Finish의 실패·취소 뒤 builder는 faulted 상태가 되어 후속 Finish로 이전 정상 페이지만 반환할 수 없다. finished/disposed 상태도 재사용을 거절한다. 압축 stream에 쓰는 중 용량을 검사하고 final xref/trailer 공간을 포함하여 누적 한도를 적용한다. 종료 시 출력·임시 owned 배열을 지우되 native surface의 즉시 해제·완전 삭제를 주장하지 않는다.

Windows 입력 후보는 `VisualPdfSource(string Title,string Body)`와 `CapturePlainSources(IEnumerable<NoteDraft> notes)`다. Capture는 source owner에서 기존 `TextTransfer.Capture` 검증을 거치고 plain 여부·입력 한도를 확인한다. `Task<PreparedTextExport> RenderPlainAsync(IReadOnlyList<VisualPdfSource> sources,Func<bool> current,CancellationToken token)`는 UI dispatcher에서만 호출한다. `current`와 source owner는 UI 준비 작업에만 있으며, 별도 파일 worker에는 기존 `WritePdfExportAsync(PreparedTextExport,string,CancellationToken,IAtomicVaultFiles?)` 패턴으로 전달하지 않는다.

## Task 1: 고정 RGB PDF 소유자

**Files:** 새 `PdfRasterTransfer.cs`, 새 Core 검사 파일, Contract `Program.cs`.

**Interfaces:** 위 builder API; 입력은 한 장의 RGB bytes, 결과는 완성 `PreparedTextExport`.

- [ ] 합성 RGB 794×1123의 한 페이지와 2페이지 문서 검사부터 작성한다. 기대값은 PDF1.7/정확 A4 MediaBox, `/Subtype /Image`·`/DeviceRGB`·8bits·Flate, xref/stream 길이, 원래 RGB exact inflate, font/text/URI/action 객체 없음이다.
- [ ] 누적 16 MiB 초과, 257쪽, 잘못된 stride/길이/크기, checked overflow, canceled Add/Finish, incomplete 후보, incompressible 페이지 입력, owned 출력·중간 버퍼 zero 검사를 작성하고 RED를 확인한다.
- [ ] 최소 writer를 구현하여 raw 페이지를 호출 중에 압축한다. 압축된 페이지는 가능한 한 즉시 builder의 bounded PDF buffer에 추가하고 raw/압축 임시 페이지를 해제한다. 각 페이지 객체 ID와 xref 위치만 보관하고 Catalog/Pages 객체는 Finish에서 완성할 수 있다. 최종 PDF buffer는 16 MiB로 제한한다. Finish의 owned 출력 복사 중에는 최대 두 개의 16 MiB managed 문서 버퍼가 잠시 존재할 수 있음을 별도 메모리 예산으로 명시하고, 기존 buffer는 즉시 지운다. 전체 raw raster 보관은 허용하지 않는다.
- [ ] 새 `--pdf-raster-only`를 Contract Program에 등록한다. 후속 실행 명령은 `dotnet run --project tests/MemoApp.ContractChecks -c Release -- --pdf-raster-only`와 전체 `dotnet run --project tests/MemoApp.ContractChecks -c Release`이며, 각각 새 검사 PASS/종료 0과 전체 실패 0을 확인한다. 조정된 직렬 실행으로 GREEN 확인하고, 독립 PDF parser/viewer로 합성 RGB의 렌더를 비교한다. 독립 도구는 검사 환경 도구이며 제품 dependency로 추가하지 않는다.

## Task 2: plain Unicode의 native 페이지 렌더

**Files:** 새 `VisualPdfRenderer.cs`, 새 Windows 검사 파일.

**Interfaces:** plain capture·RenderPlainAsync; Task 1 builder에 각 페이지 RGB를 전달한다.

- [ ] 실제 Windows에서 동일 합성 원문을 native WPF reference visual과 export renderer로 각각 그려 glyph/픽셀을 비교하는 RED fixture를 먼저 작성한다. `😀`, `e\u0301`, 분해 한글, supplementary 문자, 공백·CR/LF·탭, 빈 메모, 긴 줄·마지막 페이지 줄을 별도 단언한다. 단순 nonwhite pixel 개수만으로 올바른 glyph를 증명하지 않는다.
- [ ] 허용 font와 실제 fallback 검사, glyph 0/coverage 누락, invalid Unicode, unsupported sequence, rich/Markdown 거절, ink/page 경계 잘림 및 257쪽 실패 검사를 작성한다.
- [ ] `TextFormatter`/`TextLine`을 소유 UI에서 생성·dispose하고, 페이지별 native Draw 결과를 흰 배경 794×1123 bitmap에 렌더한다. `CopyPixels` 후 RGB 변환은 alpha/channel 검사를 포함하며 raw 배열과 bitmap을 다음 페이지 전에 해제/clear한다.
- [ ] 페이지마다 dispatcher yield 전후에 current/token을 검사하여 잠금·입력이 처리될 기회를 만든다. TextLine/native 그림 준비 중 모든 페이지를 거대한 visual/tree/bitmap으로 합치지 않는다.
- [ ] 한 페이지가 UI를 오래 막지 않도록 측정한 bounded line batch 사이에도 yield/current/token 검사를 한다. 페이지 간 yield만으로 잠금 처리 시간 상한을 주장하지 않는다.
- [ ] 실제 WPF glyph·픽셀 결과를 확인한 범위만 지원으로 기록하고, 독립 PDF viewer에서 동일 페이지가 읽을 수 있게 그려지는지 확인한다. 96 DPI 작은 글자의 품질은 별도 수용 근거가 필요하다.

## Task 3: 명시 내보내기 경계와 기존 PDF 회귀

**Files:** `MainWindow.PdfExport.cs`, `MainWindow.xaml`, 기존/새 Windows PDF 검사 및 `Program.cs`.

**Interfaces:** 기존 source-authority/파일 검사와 Task 2 준비 API. 기존 텍스트 PDF API는 유지한다.

- [ ] 두 모드 선택·기본 No·선택 취소·목적지 취소·plain 전용 안내·미지원 모드 무파일·기존 텍스트 PDF 원래 출력의 RED 검사부터 작성한다.
- [ ] 페이지 중간에 edit/version/선택/UI epoch/동일 버전 AcceptPrepared를 바꾸거나 잠그는 fixture를 작성한다. 전체 candidate가 취소되고 새 파일이 없으며 원래 snapshot/cipher가 정확히 같아야 한다.
- [ ] 완성 후보만 CreateNew를 호출하도록 기존 파일 경계를 연결한다. 파일 worker compiled closure가 PreparedTextExport/string/token/backend만 보유하는지 검사한다. paused CreateNew 중 잠금은 즉시 키를 종료하고 늦은 파일은 plaintext를 쓰지 않아야 한다.
- [ ] export별 cancellation은 note EditVersion·workspace source invalidation/동일 버전 acceptance·selection event·session/lock/close에 동기적으로 연결하고 finally에 모든 구독을 해제한다. 선택이 원래대로 돌아와도 취소는 되돌리지 않는다. paused CreateNew 동안 edit/selection round trip/AcceptPrepared를 각각 검증하며 late 생성된 빈 파일은 부분 파일 가능성 안내와 구분 없이 숨기지 않는다.
- [ ] 기존 text PDF unsupported 문자 거절·modal/native setter·nooverwrite·source invariance 회귀와 새 `pdf-visual-export` group을 실제 Windows에서 함께 실행한다. 후속 Windows 명령은 `dotnet run --project tests/MemoApp.WindowsChecks -c Release` 또는 기존 Windows workflow의 같은 검사 EXE이며 모든 group 실패 0이 GREEN 조건이다. Linux 교차 build를 native 실행 근거로 쓰지 않는다. package/installed 버튼·명시 안내도 해당 소스에서 확인한다.
- [ ] 완료 문구는 `표시용 PDF를 저장했습니다. 이 PDF에는 검색·복사용 텍스트가 없습니다.`로 한정한다. 실패 문구는 원본/기존 파일 보존, 문자·품질/한도 거절과 부분 파일 가능성을 사실대로 알린다. 후속 docs/원장에는 실제 검사 범위만 반영한다.

## 설계에서 아직 입증해야 할 점

- 고정 family 목록에서 Windows별 실제 glyph fallback/흑백 emoji/combining 결과와 줄 geometry는 API 존재만으로 입증되지 않는다. Task 2 native fixture에서 허용·거절 사례를 고정한다. full shaping·컬러 emoji 보존은 첫 단위의 약속이 아니다.
- UI 렌더 단위의 yield 간격과 처리 시간 상한은 native 측정으로 결정한다. 페이지 간 취소는 필수지만 한 페이지 자체가 장시간 UI를 막으면 renderer를 더 작은 줄 단위 작업으로 나눠야 한다.
- A4 96 DPI의 794×1123 올림 pixel/물리 point 환산, baseline/여백·line height 및 note 간 구분 간격은 golden fixture로 확정한다. 기본 여백은 기존 text PDF의 약 40pt로 시작하되 ink를 잘라 맞추지 않는다. 자동 DPI 저하·글자 축소로 한도 실패를 숨기지 않는다.
- managed 소유 버퍼 zero 검사는 가능하나 WPF native pixel surface·GC 문자열 완전 삭제는 검증 범위 밖이다. 이 한계를 내보내기 안내와 구현 근거에 보존한다.
- 독립 viewer 출력 품질·사용자 수용은 native 테스트와 구분한다. 표시 PDF에는 텍스트 검색/복사·PDF-A/UA 접근성·확대 시 벡터 품질을 주장하지 않는다.

## 후속 경계

canonical rich v1의 run 스타일·목록·표 및 `SafeMarkdown.Preview` 블록은 알려진 inert 모델에서 새 detached renderer를 만들 수 있다. live editor 캡처를 재사용하지 않는다. H01 문서 v2와 PNG는 기존 schema/root/read-lease 검증, managed PNG decoder, 동일 전역 image/OCR admission, pixel/object 한도, lock revocation을 먼저 연결해야 하며 첫 plain 단위에 조용히 포함하지 않는다. 그림·표 행 pagination·rendered Markdown 지원은 각자의 RED/native/viewer 근거를 갖춘 뒤 범위를 늘린다.

## 공식 API·권리 근거

- [RenderTargetBitmap](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.rendertargetbitmap?view=windowsdesktop-10.0): WPF Visual을 bitmap으로 렌더할 수 있다. 실제 glyph 지원·출력 품질의 성공 근거는 별도다.
- [TextLine.GetIndexedGlyphRuns](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.textformatting.textline.getindexedglyphruns?view=windowsdesktop-10.0), [GlyphRun](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.glyphrun?view=windowsdesktop-10.0): font/glyph/원문/cluster와 위치를 감사할 수 있다.
- [Windows font FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq): Windows 시스템 글꼴로 문장·문구를 그린 graphic 출력과 font 파일 재배포/embedding은 다른 범주다. 제3자 설치 글꼴의 권리까지 이 FAQ로 추정하지 않는다.

현재 상태: 계획만 작성. 구현/RED·GREEN/native 픽셀/독립 viewer/패키지·사용자 수용은 아직 실행·완료하지 않았다. 일반 기능 승인 재요청 없이 기존 승인 범위와 부모의 실행 순서에 따라 후속 구현할 수 있다.

별도 기존 텍스트 PDF 권한 보완: `MainWindow.PdfExport.cs` export별 취소/구독 경계와 `PdfExportAuthorityChecks.cs`의 native setter·confirm/picker selection round trip 및 paused CreateNew edit/selection/동일 버전 acceptance/close fixture를 먼저 추가했다. 실제 Windows RED/GREEN 실행은 이 Linux 환경에서 불가하여 대기이며 이 변경은 raster 구현·native shaping·viewer 성공 근거가 아니다. 기존 text/font/limits 및 `TextTransfer.WritePrepared`의 CreateNew 후 token 검사를 유지한다.
