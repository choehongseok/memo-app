# memo-app 작업 규칙
대상은 choehongseok/memo-app. Windows 중심 S메모 대체 앱이며 이번 작업은 단계 0이다.
시작 시 실제 git 상태와 이 파일, docs/STATUS.md, docs/PRODUCT_SPEC.md 및 관련 FEATURES.json 항목을 읽는다. 하위 AGENTS/override도 확인한다.
원문은 docs/SOURCE_PROMPT.txt. 134 ID 삭제·재번호 금지, M06은 M01~M05 유지로 정규화 대체. Android·동기화·로컬 OCR/음성/AI 범위를 보존한다.
실메모·키·토큰·사용자 백업은 저장소/로그/CI에 금지. 합성 자료만 사용한다. 네트워크 없는 핵심 기능, 통신 기본 차단, 광고·텔레메트리 금지.
유료 가입·외부 AI·공개 배포·visibility 변경·비가역 데이터 변경·force push 금지. 접근 거부는 우회하지 않고 기록한다.
저장·암호화·복구·동기화·업데이트 구현 전 별도 보안 검토가 필요하다. 자체 검토와 독립 검토를 구분한다.
python3 tools/verify_preparation.py, dotnet run --project tests/MemoApp.ContractChecks -c Release를 실행한다. Windows 빌드/GUI, Android 실기기 검증은 Linux 검사와 분리한다.
기능 상태/사용자 수용/구현 위치/근거를 사실대로 갱신한다. 단계 종료 시 원문 134 ID 자동 대조, diff·민감정보·산출물 검사를 수행한다.
원격 정책 확인 전 main push 금지. 기존 자료가 있으면 보존하고 작업 브랜치+draft PR 사용. 반복 승인 없이 승인된 범위를 진행한다.
