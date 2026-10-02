# 단계 1 첫 수직 기능 계획
기준 be6dbed / development/stage-1. 단일 cloud 작업, 원문 134 ID 보존. 명세 PRODUCT_SPEC.md, STAGE1_SECURITY_REVIEW.md. 최신 사용자 지시로 실제 개발 승인, 재승인 질문 없음.

- [x] 기준 git/지침 확인 및 GitHub 접근 1회 재확인. Forbidden, 우회 없음.
- [x] 암호/키/복구/파일 교체 설계를 부모 독립 검토용으로 작성/보고.
- [x] 독립 편집 상태: `NoteDraft`(Id/Title/Text/EditVersion/UTC 시각), `EditingWorkspace`(CreateNote/Clear/읽기전용 Notes), 다중 창 같은 객체 공유 및 종료 시 참조 편집 차단. 실패 테스트 → 실제 구현 → 공통 시험.
- [x] WPF 관리창/포스트잇 편집 바인딩(교차 빌드, 실제 GUI 미검증). 초기 비영속 단위 이후 실제 암호 저장 화면으로 연결했다. 합성 자료용이며 Windows 실행은 미검증.
- [x] 부모의 독립 보안 검토 결과/기준 커밋을 받은 후 수정사항 반영. 결과 없으면 아래 항목 구현을 시작하지 않음.
- [x] 승인된 EncryptedVault/WindowsKeyAccess 실제 구현과 잘못된 키·변조·평문 누출·새 인스턴스·복구 비밀·쓰기 실패·비정상 종료의 동작 시험.
- [x] 저장 성공/실패·자동 저장·재실행 복원·앱/OS 잠금 연결(실제 Windows GUI/OS event 미검증). 메모 두 창 동시 편집 충돌·실패 보존 시험.
- [ ] Windows 실제 실행. Linux 교차 build/self-contained publish는 완료. 현재 runner 접근 없음: Linux 교차 빌드와 구분하고 GUI/DPAPI/IME/실기기 결과는 미실행으로 기록.
- [x] 기능 원장 관련 ID에 부분 구현/미검증 상태만 반영, 커밋·bundle·검사·원격 차단 인계.

검토 초점: 비밀키 평문 저장 금지; nonce/DEK wrap 한도; current/previous 원자 교체 실패; 잠금 시 편집 참조 폐기; 여러 창 편집·IME와 수정 순서. 저장은 독립 검토 후 실제 파일/프로세스로 시험하며 mock 저장 성공으로 암호 저장 완료를 표시하지 않는다.
