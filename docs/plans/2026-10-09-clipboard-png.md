# H02 제한 PNG 클립보드 붙여넣기

기존 이미지/첨부 설계와 원문 Ctrl+V를 잇는 독립 입력 단위다. 메모 첨부 영역에 focus가 있을 때 Ctrl+V 또는 명시 버튼으로 Windows 클립보드의 자동 변환 없는 PNG만 받는다. 텍스트 편집기의 기존 붙여넣기는 유지한다. Bitmap/DIB/HTML/FileDrop/URI 자동 변환·native 이미지 decode·외부 실행을 하지 않는다. 일반 스크린샷 도구가 PNG를 제공하지 않는 경우는 지원 제한으로 명시하며 완료로 과장하지 않는다.

GetDataPresent/GetData/stream 속성과 실제 read 모두 불신 경계이다. 호출 전후 session/note/version/preview epoch/current를 재확인한다. PNG bytes 또는 정확 MemoryStream만 최대4MiB bounded copy, borrowed clipboard 버퍼를 zero/dispose하지 않는다. 앱 소유 복사만 finally zero. PNG strict preflight를 적용하되 자동 표시/decoder를 호출하지 않는다. 앱 전체 image admission 하나를 사용해 기존 미리보기와 처리 중 상호 제한한다. PrepareAttachments 이전에 입력/preflight/current를 완료하고 이후 다시 권한을 검사한다. 암호 AttachBytes 및 SaveAsync 기존 경로를 쓰며 lock/선택/source변경 결과를 폐기한다. 클립보드 내용은 앱 밖에 남으며 잠금은 이미 존재한 OS 클립보드를 지우지 않는다고 표시한다.

검사: delayed OLE getter lock/source/selection revocation, unsupported formats/autoconversion, malformed/limit PNG, clipboard 원본 불변/owned zero, app global admission, 관리창/스티키 actual Ctrl+V routes, 암호 restart와 늦은 save/lock. 사용자 실제 PC 시험은 코드 구현의 선행 차단 조건이 아니다.
