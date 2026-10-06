# 현재 상태
## 2026-10-06 원격 저장 및 Windows CI 갱신
아래 2026-10-02 상태는 당시 기록이다. 일반 Git push로 main/preparation/stage-0=be6dbed, development/stage-1 이력을 저장했고 draft PR https://github.com/choehongseok/memo-app/pull/1 을 생성했다(미병합).
Windows checkout 원문 CRLF 변환은 .gitattributes의 원문 -text로, Python 기본 charmap 읽기 실패는 검사/회귀의 명시 UTF-8 읽기로 수정했다. 원문 byte/hash, 134 ID/이름/M06 및 검사 기준은 변경하지 않았다. 제품 코드 변경 없음.
검증 코드 a8915328d42db319504b4b457f58a64f8d454c7d의 Windows Server 2022 CI https://github.com/choehongseok/memo-app/actions/runs/37504683119 성공: 원문 blob/checkout 일치, 원장/Python6/민감정보 검사, Windows 빌드 경고·오류0, 실제 합성 암호 파일/장애/프로세스 종료/잠금 경쟁 계약 검사, publish, artifact 업로드.
artifact windows-x64-synthetic-trial ID11430249406, 64,957,037 bytes, SHA256 5b079cee5e1071b4a0b9b6c488e75954e604f813168549cc667f783223e04b2c, 만료2026-10-13. 이전 Linux 생성 ZIP과 별개다. 미서명 합성 시험판이며 Windows GUI/IME/OS SessionLock/ACL/다른 계정 사용자 흐름은 여전히 미검증이다. 전체134 기능/실제 개인정보 사용 준비 완료 아님.
push username 오류는 일시 발생했지만 설정/토큰 변경 없이 동일 Git 경로 dry-run 및 push가 후속 성공했다. 내부 인증 제공 원인은 미확인이며 저장소 권한 문제로 단정하지 않는다.

2026-10-02, 단계 1 첫 합성 자료용 암호 메모 단위. 전체 134 기능 미완성, 사용자 수용 미확인. M06 정규화 유지.

실제 cloud Linux x86_64, `/workspace/memo-app`, origin `https://github.com/choehongseok/memo-app.git`. branch `development/stage-1`; 기준 준비 be6dbed 및 기존 이력 보존. 검증 Core 코드 ef5a8b7, 최종 Windows 소스/교차 publish 코드 67fe9c5. 현재 기능/검사 근거 FEATURES.json 및 VERIFICATION.md.

원격: 후속 요청의 1회 gh repo view도 Forbidden. 우회/추가 확인/원격 쓰기 없음. 원격 HEAD·보호 규칙·Actions/runner 미확인. 원격 commit/push/PR 없음.

구현: plain/device-only 메모 생성·제목/본문 편집·자동 암호 snapshot 저장·재실행/복구 비밀 해제·공유 포스트잇·즉시 잠금·실패 상태·명시 후보 복구/암호문 사본 보존. 100 notes/16MiB 시험 한도. DPAPI 자동 접근 off/미제공, Hello/비밀번호 해제 미제공. 보안 설계 독립 검토 조건 반영; 검토자는 설계 문구만 보았고 코드 실행/감사는 아님.

동작 근거: 실제 암호 파일/복구·변조19영역·절단7·엄격 JSON/UUID/한도·write/flush/replace 오류·base 충돌·정상 후보 보존·다른 process writer 거부·process Kill4·stdout/stderr/파일 평문·키 누출·generation/epoch/즉시 잠금 경쟁·실패 비노출·숨겨진 복구 재개 PASS. 원문134 ID 및 Python6 회귀 PASS. 전체 Windows 소스 Linux 교차 build/publish PASS.

시험판: `/workspace/memo-app-windows-x64-synthetic-trial.zip`, self-contained win-x64 runtime10.0.12 포함. 앱 미서명. Linux에서 생성한 PE 실행 파일이며 Windows 실행/GUI/IME/OS SessionLock/다른 계정/ACL 동작은 미검증. 실제 개인정보 사용 준비 완료 아님. README의 합성 사용·백업/복구·한도를 따를 것.

이력 보존: `/workspace/memo-app-stage1-encrypted.bundle` 전체 branch 이력. `git clone -b development/stage-1 /workspace/memo-app-stage1-encrypted.bundle memo-app-recovered`로 복원 가능. stage0 및 초기 stage1 bundle도 유지.

현재 단위 종료: 암호 저장/잠금 코드·검사·Windows 산출물을 커밋/bundle로 인계. 추가 범위 확장 없음. 다음 단위는 가능한 Windows CI/실행/IME/OS 잠금/복구 실제 확인 및 지적 부분 수정, 그 다음 단계1 폴더·검색·휴지통·이력/정식 백업 UI 등 작은 흐름. 접근 복구 시 원격 현황/정책 확인 후 기존 자료와 통합+draft PR; force push 금지.

한계: 이전 암호 snapshot 자동 삭제 없음(디스크 증가), 복구 화면 최근128 후보, 과거 전체 파일 replay/카운터 rollback·완전 전원 내구성·OS/관리자 메모리 보호 증명 없음. 독립 코드 검토/전문 보안 감사 없음. Android/동기화/로컬 OCR·음성·AI와 134 최종 범위는 그대로 미완료 추적. 실메모/키·유료 가입·외부 API·공개 배포·visibility 변경 없음.

인계 파일 보존: 시험판 ZIP 63,192,131 bytes, SHA256 744dec678cc0088b97056ab5511b7a7c4495b03650987464d430aa12480ff5a8. Library 저장 1회는 네트워크 오류로 실패하여 Library file ID 없음. 우회/대체 업로드/반복 시도 없음. 파일은 위 cloud 경로에만 보존. 부모가 직접 filesystem을 읽지 못하면 후속 cloud 작업에서 필요한 내용을 텍스트로 전달하거나 연결 복구 후 파일을 보존해야 함.
