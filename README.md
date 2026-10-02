# 메모앱

Windows 중심의 개인용 S메모 대체 앱을 준비하는 저장소입니다. 현재 **단계 1 개발 중**이며 메모 작성·저장·백업·복구·자료 이전·동기화·Android·OCR·음성·AI 기능은 아직 제공하지 않습니다. 실제 메모나 키를 넣지 마세요. 개발 골격은 설치 가능한 시험판이 아닙니다.

## 개발 골격 검사
.NET SDK 10.0.401과 Python 3.12 이상을 사용합니다. Linux에서 공통 계약만 검사할 수 있습니다.

```sh
python3 tools/verify_preparation.py
python3 -m unittest discover -s tests -p 'test_*.py' -v
dotnet run --project tests/MemoApp.ContractChecks -c Release
```

Windows에서 골격 빌드/실행:

```powershell
dotnet build MemoApp.slnx -c Release
dotnet run --project src/MemoApp.Windows -c Release
```

기본 실행은 암호 저장 검토 대기 안내와 작성 차단 상태입니다. 개발자가 `dotnet run --project src/MemoApp.Windows -c Release -- --editing-preview`로 시작하면 메모리 편집/포스트잇 공유 바인딩을 평가할 수 있습니다. 이 모드는 암호화·자동 저장·재실행 복원이 없고 종료하면 내용이 사라집니다. 합성 자료만 사용하며 안전한 메모 시험판으로 취급하지 마세요. Windows 런타임/GUI 실행은 Linux 검사로 확인할 수 없습니다. `.github/workflows/preparation.yml`은 접근 복구 후 Windows 빌드·검사·골격 산출물 생성을 위한 설정이며 현재 실행 성공을 뜻하지 않습니다.

## 실제 지원 범위와 안내
설치형·포터블·백업·복구·S메모 가져오기 절차는 아직 없습니다. 기존 S메모 자료를 삭제하거나 이 앱으로 이전하지 마세요. 실행 파일/APK/서명된 배포본도 아직 없습니다. 향후 평문 내보내기/외부 첨부 열기는 사용자 확인과 위험 안내 후 제공합니다.

전체 요구·기술/보안/복구/동기화 설계는 [PRODUCT_SPEC](docs/PRODUCT_SPEC.md), 원선택 134개 상태는 [FEATURES](docs/FEATURES.json), 실제 작업과 다음 작업은 [STATUS](docs/STATUS.md), 검사 근거와 미실행 항목은 [VERIFICATION](docs/VERIFICATION.md)에 기록합니다. 원문은 [SOURCE_PROMPT](docs/SOURCE_PROMPT.txt)에 보존했습니다.
