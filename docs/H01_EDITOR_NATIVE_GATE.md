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
