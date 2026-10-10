# T12 Windows 제품 연결 — 기존 설계의 후속 경계

134ID/원문/M06/기존 암호schema·잠금 설계를 유지한다. 이 문서는 기존 전체 설계를 재시작하지 않는다. 내부 owned PPM/process 경계와 실제 native Windows feasibility가 통과한 뒤의 연결 단위를 정의한다. 제품 실행·설치·사용자 수용 증거가 아니며 T12 집계는 올리지 않는다.

## 최소 실제 흐름

사용자가 선택 PNG에 대해 로컬 OCR을 실행하고 제한 UTF8 결과를 미리 본다. 명시적 적용은 기존 Workspace.ImportTexts 전체 snapshot preflight로 새 plain 메모를 만든다. 기존 rich/Markdown 원본과 첨부를 변경하지 않는다. 생성 결과는 기존 암호 저장·이력·검색 흐름을 사용한다. 실제 추가 ID를 기록한 뒤 Save 실패는 dirty 추가 메모 보존·저장 실패 안내로 보고한다. 모델 설치와 실행은 각각 사용자 명시 동작이며 기본 OFF, 자동 모델 다운로드/외부 fallback은 없다. 제품 UI에는 내부용 arbitrary executable/command 설정을 노출하지 않는다.

## 제품 엔진 신뢰와 설치

고정 upstream commits, model 길이/SHA256, 실제 선택 MSVC·Leptonica package 및 실제 PE imports를 native gate에서 확인한다. CI build manifest는 build/harness 내부 증거일 뿐 mutable 파일을 production 실행 신뢰로 받아들이지 않는다. 배포 전체 구성물/notice 검토 후 expected engine hash를 제품 assembly의 고정 검증 입력과 연결한다. installer/portable 구성물과 모델 사용자 선택·미설치 거절·정확한 모델 용량/라이선스 안내는 별도 검증한다. SDK약관 수락·Windows identity 등록·지속 authentication/security settings 변경·새 유료 의존성은 포함하지 않는다.

## 수명과 publication

기존 ImagePreviewAdmission을 attachment lease/decode 전에 획득한다. UI 요청 종료와 실제 child/pipe settlement를 분리한다. LocalOcrProcess가 late cleanup continuation으로 반환하는 경우 slot을 UI finally에서 즉시 반환하지 않는다. UI publication 종료와 실제 cleanup이 모두 끝나야 global slot을 해제하며, 완료되지 않은 작업은 새 Main/Sticky 이미지 decode admission을 거절한다. 결과 게시가 Dispatcher에 대기 중일 때도 slot과 owned bytes를 유지한다.

worker는 detached lease/token 및 검증된 scalar engine/model 설정만 받고 note/session/key/current authority closure를 받지 않는다. Same/selected ID/EditVersion/AttachmentPreviewEpoch/generation 판단은 Dispatcher에 둔다. InvalidatePreview를 OCR CTS/owned result/native labels까지 revoke-first로 확장한다. selection/NoteChanged/동일-version AcceptPrepared의 epoch/conceal/dispose에서 취소·폐기하며, 결과 표시 기간은 기존 Rendering epoch 검사를 쓴다. native setter 이후마다 authority를 다시 확인한다. TextBox의 undo/history·선택/복사 가능한 stale label도 잠금에서 비운다. 관리문자열·OS/child 메모리 완전 삭제를 주장하지 않는다.

## 검증 순서

1. Core owned process settlement API와 전체 이미지 admission 수명 회귀 RED→GREEN: late pipe cleanup 동안 다른 Main/Sticky decode 거절, 최종 settlement 후 한 번 해제.
2. 실제 설치 검증 입력에 고정 엔진·모델·전체 notice·사용자 명시 모델 설치와 미설치/변조/외부 DLL 거절을 연결한다. native gate와 제품 실행 gate는 분리한다.
3. 실제 WPF 선택 PNG OCR group: 전체된 한글영문 합성 입력→actual native recognition→preview→explicit new plain note→encrypted save/reopen/search. 합성 fake child는 cleanup boundary 시험에만 사용한다.
4. lock 즉시 key 해제/결과 conceal, selection/edit/same-version epoch 폐기, callback/native setter 재진입, 느린 실행·timeout/취소·큰출력, 원본 불변·한도/기존dirty/이력·저장실패 보존.
5. 허용하지 않은 runtime 통신을 실제 관찰해 기록하고 가능한 CPU/RAM/경과시간과 정확도 한계를 분리한다. 단일 public fixture 일치가 전체 OCR 정확도 증명이 아니다. 실제 사용자 Windows 수용은 자동 Windows control 검증과 구분한다.

독립 읽기 검토자가 실제 AttachmentPanel/ImagePreviewAdmission/EditingWorkspace/SaveCoordinator 메서드를 확인하여 위 Important 연결 조건을 제시했다. 검토자는 코드 작성·빌드·runtime 실행을 하지 않았다. 적용 경로는 NoteDraft.Text 직접 대입 대신 preflight가 있는 ImportTexts를 사용한다.
