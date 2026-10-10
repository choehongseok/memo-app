# O01~O04 암호 자동 백업 연결

기존 암호 백업·복구 형식을 유지하고 별도 읽기 보안 검토를 거친다. 성공한 현재 암호문의 hash를 commitGate/gate 안에서 검증해 독립 복사 객체로 만든 뒤 destination I/O는 모든 vault gate 밖에서 수행한다. worker에는 복사할 암호문·목적지·파일 adapter만 전달한다. 새 암호 알고리즘/키/nonce/schema/외부 서비스를 추가하지 않는다.

종료 백업은 LockAsync가 마지막 dirty snapshot을 준비하고 화면 숨김/키 종료를 즉시 수행한 뒤 실제 최종 commit 성공을 기다려 keyless authenticated current를 복사한다. LockAsync 진입 시 기존 backupTask를 먼저 고정해 새 종료 wrapper와 자기 대기하지 않는다. IsDirty는 locked commit의 savedGeneration을 표시하지 않으므로 잠금 성공 결과와 pending/hidden 실패 없음, 현재 암호문 hash로 확인한다. 복사와 lock의 안전한 종료를 모두 기다린 뒤 writer를 해제한다.

자동 백업은 기본 꺼짐이며 사용자가 현재 해제 세션에 대해 기존 로컬 외부 폴더와 매 저장/UTC 하루 한 번/종료 조건을 선택한다. 이 단계의 설정은 세션 내에서만 유지하며 잠금 시 제거한다. 새 파일 이름은 vault별 고정 prefix와 UTC timestamp/난수이며 CreateNew만 허용한다. 정한 보관 한도가 찼으면 기존 파일을 삭제하지 않고 새 백업을 멈춘다. 실패한 부분 파일도 개수에 포함한다. 자동 삭제·설정의 재실행 유지·특정 시간 보장/USB 실기기 등 남은 수용 조건은 사실대로 별도 기록한다.

검사: 키 종료 후 독립 ciphertext exact copy, final dirty exit, 기존 목적지/활성 vault/링크/UNC 거절, 실패 부분 파일 보존, 해시 불일치/재실행 첨부 인증, busy admission/잠금 동안 늦은 UI 게시 거절, 개수한도/열거한도/하루 중복 방지. 실제 Windows 자동 설정 화면·닫기·숨김·취소·용량 가득 참을 검사한다. 사용자 실제 PC 시험은 로컬 구현의 선행 차단 조건이 아니다.
