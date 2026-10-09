# A09/E10 기기별 암호 기록 확장

기존 기기별 암호 UI 기록을 확장한다. 원문·134 ID·메모 원본/이력/첨부·기존 schema1~5 읽기와 출력 규칙은 보존한다. 최근 메모는 최대20개의 MRU 메모 ID, 저장 검색은 최대20개의 ID·이름64자·query256자 및 기존 SearchOptions 조건이다. 평문 설정 파일이나 외부 서비스는 추가하지 않는다. 전체 개발 승인에 포함된 구현 단위이며 초기 설계 승인을 반복하지 않는다.

새 필드는 schema6에서만 출력하며 schema1~5 입력에 새 필드가 섞이면 거절한다. 6은 기존 root가 있는 envelope2에만 저장하고 암호 알고리즘·AAD 목적·nonce·키 래핑은 바꾸지 않는다. 최초 요청은 기존 PrepareAttachmentsAsync의 root 고정을 기다리고 세션/기기/선택/version을 다시 확인한다. 실패·잠금 시 새 기록을 게시하지 않는다. 전체 candidate·payload 검증을 통과한 뒤만 상태를 바꾸며 내용 revision을 만들지 않는다.

독립 읽기 검토에서 요구한 조건: root 이동/고정/3-wrap 예약/암호화/retained object/숨겨진 복구의 모든 exact5 분기를 검증된5/6으로 확장한다. 빈 기록에서도 Capture와 preferences/window 변경은6을 유지한다. 미래 버전은 거절하고6→5 downgrade도 거절한다. 최근 목록은 삭제/휴지통 변경 candidate에서 모든 기기에 걸쳐 원자적으로 정리한다. 저장 검색의 폴더 제거 동작은 조건을 더 넓히지 않게 정의하고 검토한 뒤 연결한다.

필수 검사: 원래1~5 serializer literal fixture,6의 빈/비어있지 않은 기록, 최초 migration의 exact previous ciphertext 및 history/root 보존, 암호 restart/backup/recovery/hidden recovery, downgrade/future/ref/count/Unicode/date/enum 거절, 한도·payload 실패 전체 불변, 모든 기기의 삭제 처리, root 준비 중 잠금, 깨끗한 메모의 metadata-only 저장 시 내용 revision/이력 불변. 이어서 실제 Windows 최근 목록/저장 검색 적용·삭제·잠금 및 native 재진입을 검사한다.

현재는 별도 보안 읽기 검토 조건을 기록한 설계이며 schema6 코드나 데이터 쓰기는 아직 활성화하지 않았다. N05 독립 로컬 TXT UI 구현을 먼저 검증하는 동안 이 경계를 준비한다. 사용자 실제 PC 시험은 이 구현의 선행 차단 조건이 아니다.
