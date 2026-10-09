# 설치형·portable Windows 시험판 인계 — 2026-10-09

전체 134 기능 완성이나 사용자 수용 완료가 아니라, 검증된 기능을 포함한 서명 없는 합성 자료용 시험판이다. 원장119구현·부분 연결/13미착수/N09차단1/M06정규화1, 전체ID완료·사용자수용0을 유지한다.

## 산출물과 검증

- 다운로드: https://github.com/choehongseok/memo-app/actions/runs/37995989631/artifacts/11647312201
- GitHub artifact ID: 11647312201 / 72,573,768 bytes
- GitHub 업로드 ZIP SHA256: `4f9c4d05e682a48b2cf02b48ae68275a71ebe301ba8cb3f316e1acd093369111`
- 만료: 2026-10-16 21:56:29 UTC. artifact 링크는 GitHub 접근 권한/로그인이 필요할 수 있다.
- 패키지 installation-manifest.json sourceCommit: `d8bccaab53f65d130d38237d989d35c44072fc2f`
- 최종 run37995989631/job114042089704: 원문27417B/hash·134ID/M06·Python39·고정 native OCR/model·앱 build0경고/오류·fullCore·실제 WPF(자동 정리와 OCR 포함)·publish413files·설치된 production EXE/shortcut/tray/종료·artifact upload 모두 SUCCESS.
- 기능 검증 소스 `f6af63cffd4abf41cbd901156af16c3a839cbe82` / run37995312568/job114039721425 SUCCESS. 앱 source tree `48d8354ed39e072f2ca8aeeaa78ee0b01d9761c4`, tests tree `2921ade94ee64e0e31e49d42ce19e1fd3541d476`을 그대로 최종 업로드에 지정했다. 최종 실행도 전 과정을 재검증했다.
- 이전 OCR 준비 실패 run37990395571은 fixture의 변환 확인값 누락을 고쳐 `baeba805967c290e2adb9d5eede3a7db76189a40` / run37991107459에서 전체 SUCCESS. 원본 보호 확인 조건을 약화하지 않았다.

## 실행 및 최소 실제 사용자 확인

ZIP 전체를 새 폴더에 해제한다. 새 사용자 폴더 설치는 `Install-User.cmd`, portable은 `Start-Portable.cmd`로 실행한다. 런타임 포함 Windows x64 패키지다. 새 합성 자료와 별도 보관한 복구 비밀만 사용한다. 기존 설치를 덮어쓰지 않는다. 자세한 실행/제거/보존 경로는 README.txt와 USER-TESTS.txt에 있다.

1. 설치·실행, 실제 한글 IME 마지막 수정 후 완전 종료·재실행 보존.
2. 실제 Windows 잠금과 사용하는 모니터에서 관리창·포스트잇·PNG/OCR 결과 비노출.
3. 합성 한글 PNG의 명시 OCR 모델 설치·별도 메모 적용·검색·재실행, 원본 보존.
4. 자동 정리 기본off·확인 취소·잠금/재실행 설정 해제. 실제 사용자 자료의 노후 메모 삭제 시험이나 시스템 시간 변경은 하지 않는다. CI는 합성 날짜 자료와 실제 복구 가능한 암호 백업만 사용했다.

자동 정리는 선택 세션7/30/90일·외부 암호 백업100개 한도·성공한 현재 백업 이후에만 적용한다. 삭제 관계와 백업/previous/pending/암호 첨부 객체는 남으며 완전 삭제가 아니다. schema8 자료를 이전 앱으로 수정하지 않는다.

## 남은 조건과 범위

13미착수: L09/L10 배포자 인증 업데이트, P05/P06 인증 다중 PC 동기화·기기별 일부 유지, Q03/Q04/Q06/Q07/Q08 Android, T13 음성 작성, T14/T15/T16 로컬 AI 요약·다듬기·질문. N09 원본 S메모 고유 형식 합성 자료/형식 계약은 차단 유지한다. 진행 중119개의 미완료 수용조건도 각 원장에서 보존한다.

Android SDK 약관 명시 동의는 기존 요청 대기다. 동기화는 실제 기기 등록/해제·키 교체/복구/종단간 암호화 신뢰 설정, 업데이트는 개발 키와 분리한 실제 배포자 인증이 필요하며 임의 외부서비스·계정·유료 인증서를 추가하지 않았다. 음성/AI 공식 후보 메타데이터의 `huggingface.co/api/models/ggerganov/whisper.cpp` 및 `huggingface.co/api/models/Qwen/Qwen3-0.6B-GGUF` 직접 요청은 프록시 `Tunnel connection failed: 403 Forbidden`으로 거절됐다. 허용된 정식 모델 연결 또는 검증 가능한 공식 모델 파일이 다음 실제 엔진 시험에 필요하다. 다른 환경/URL로 거절을 우회하거나 외부 메모 전송으로 대체하지 않았다. 이 거절은 성공한 OCR 공식 모델 경로와 별개다.

main 미병합, draft PR1 유지, 공개 release/권한/결제 설정 변경 없음.
