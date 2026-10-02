# 메모앱 — 첫 합성 자료용 Windows 시험판

Windows 관리창·포스트잇을 중심으로 개발 중인 S메모 대체 앱입니다. 현재 일반 메모 생성·제목/본문 편집·암호 자동 저장·재실행 복원·포스트잇·잠금·파일 후보 복구를 연결했습니다. **서명 없는 합성 자료용 시험판**이며 Linux에서 교차 빌드했습니다. 실제 Windows 실행/GUI/한글 IME/OS 잠금/다른 계정 복구는 아직 미검증입니다. 실제 개인정보나 기존 S메모 자료를 넣거나 이전하지 마세요. 전체 134 기능 완료/실제 개인정보 사용 준비 완료가 아닙니다.

## 실행과 첫 사용
Windows x64 시험판 zip 전체를 새 폴더에 압축 해제하고 `MemoApp.Windows.exe`를 실행합니다. .NET 런타임을 포함한 self-contained 패키지입니다. 설치형/서명/자동 업데이트는 없습니다. OS/기관 PC 정책을 우회하지 마세요.

1. 새 자료용 복구 비밀을 생성해 오프라인에 보관하고 정확히 다시 입력해 자료를 만듭니다. 비밀은 43글자이며 자동 클립보드 복사/로그 기록은 하지 않습니다. 비밀을 잃으면 복구할 수 없습니다.
2. 새 메모의 제목·일반 본문을 편집하면 약 0.7초마다 암호 저장을 시도합니다. 관리창의 변경됨/저장 중/저장됨/실패 상태를 확인합니다. 포스트잇과 관리창은 같은 메모 편집 상태를 공유합니다.
3. 정상 종료는 마지막 저장·잠금 완료를 기다립니다. 다시 실행해 같은 복구 비밀로 해제하면 저장한 합성 메모를 불러옵니다.
4. 잠금/5분 idle/OS SessionLock 연결은 즉시 화면·독립창을 숨기고 키를 종료하도록 작성했습니다. Windows 실제 이벤트/화면 동작은 미검증입니다. DPAPI 자동 접근은 꺼짐·미제공, Hello와 비밀번호 해제도 미제공입니다.

자료 폴더: `%LOCALAPPDATA%\MemoApp\SyntheticTrial`. `current.vault`와 `previous-*.vault`/`pending-*.vault`는 암호문입니다. 복구 비밀 평문은 파일에 저장하지 않습니다. 이전 파일은 자동 삭제하지 않아 디스크 사용량이 늘 수 있습니다.

## 백업·복구·이전
앱을 완전히 종료한 뒤 자료 폴더의 암호 파일을 별도 안전한 위치에 복사합니다. 현재 정식 자동 백업/보관 개수/선택 복구 UI는 없습니다. 암호 파일 복사에도 복구 비밀이 필요하며 파일 안에는 암호화된 키 복구 정보가 포함됩니다.

다른 시험 폴더/PC에서 파일을 가져오려면 잠금 화면에 복구 비밀을 입력하고 '암호문 사본 가져와 후보로 보존'을 사용합니다. 사본 인증 후 후보로 보존하며 적용하려면 후보를 골라 명시적으로 복구합니다. 다른 Windows 계정/PC 실사용은 미검증입니다. 복구는 새 키 epoch의 snapshot을 만들고 기존 파일·정상 후보를 보존합니다. 병합·일부 메모 선택 복구·S메모 가져오기는 미제공입니다.

저장 오류는 쓰기를 중단하고 기존/보류 파일을 보존합니다. 잠금 중 암호문 보류 상태이면 사본 저장 후 후보로 복구할 수 있습니다. 암호화 전 실패는 화면을 가린 '숨겨진 복구 대기·메모리 키 보유'로 표시하고 같은 비밀로 편집을 재개합니다. 미저장 변경을 버리고 종료/재해제하는 동작은 별도 확인이 있습니다. 유효한 과거 파일 전체 replay는 AEAD만으로 검출하지 못하며 전원 장애의 완전 내구성을 보장하지 않습니다.

## 이번 지원 한도와 미완료
일반 텍스트·로컬 전용, 메모100개, 제목256/본문65536 UTF-16 code units, 파일16 MiB, 이력 메모당512/총10000. 한도 초과는 저장 실패로 표시하며 기존 자료를 덮지 않습니다. 폴더 편집·검색·휴지통·서식/Markdown·첨부·이력 UI·기기 위치 보존·설치형·동기화·Android·OCR·음성·AI는 아직 미완료입니다. 최종 요구에서 제외하지 않았습니다.

## 개발 검사
.NET SDK 10.0.401과 Python 3.12 이상. 다음 검사는 실제 합성 암호 파일/복구/변조/프로세스 종료/경쟁을 시험하지만 Windows GUI를 실행하지 않습니다.

```sh
python3 tools/verify_preparation.py
python3 -m unittest discover -s tests -p 'test_*.py' -v
dotnet run --project tests/MemoApp.ContractChecks -c Release
dotnet build MemoApp.slnx -c Release
dotnet publish src/MemoApp.Windows -c Release -r win-x64 --self-contained true -o artifacts/windows-x64-synthetic-trial
```

[PRODUCT_SPEC](docs/PRODUCT_SPEC.md)는 전체 설계, [FEATURES](docs/FEATURES.json)는 원선택134 ID 상태, [STATUS](docs/STATUS.md)는 실제 작업/원격 차단, [VERIFICATION](docs/VERIFICATION.md)은 실행 근거입니다. 원문은 [SOURCE_PROMPT](docs/SOURCE_PROMPT.txt), 독립 설계 검토 조건은 [STAGE1_SECURITY_REVIEW](docs/STAGE1_SECURITY_REVIEW.md)에 보존했습니다.
