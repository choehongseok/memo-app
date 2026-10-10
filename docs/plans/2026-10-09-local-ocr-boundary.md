# T12 로컬 OCR 메모리/프로세스 경계 — 후속 구현 계획

기존 승인된 로컬 OCR 범위에서 Windows identity 등록/서비스/유료 API/지속 보안설정 변경 없이 진행한다. Microsoft 공식 Windows.Media.Ocr 문서는 package identity를 요구하므로 현재 unpackaged installer/portable에서 지원된다고 가정하지 않는다. 기존 원본27417B/134ID/M06/schema1..7/encryption/lock은 변경하지 않는다.

첫 구현은 소유 BGRA raster를 제한 PPM/P6로 변환하고, 고정된 Tesseract 실행 경로·모델 directory·binary SHA256을 명시한 내부 adapter로 실제 child process stdin/stdout만 연결하는 경로다. 임시 이미지/평문 파일·shell·source note/keys/workspace·외부 통신을 전달하지 않는다. engine binary/model trust는 Windows 정식 배포 component 검증 전에는 제품 설정/UI에 노출하지 않는다. Linux 설치 binary의 해시 고정은 해당 탐색용 binary만 확인하며 전체 DLL provenance/Windows 엔진 신뢰를 증명하지 않는다.

입력은 기존 decoder raster의 앱 한도 이내(최대1024×1024/1M pixels), 공개 PPM header와 white alpha composite한 RGB bytes뿐이다. 소비/실패/취소 시 중간bytes와 결과bytes를 지운다. 출력UTF8 최대262144B와 UTF16문자65536/정상Unicode를 검증하며 stderr는 최대4096B만 소비하고 내용은 로그·예외에 포함하지 않는다. stdout/stderr 과다·20초 timeout·취소·nonzero exit는 child kill/pipe closure/late output discard로 처리한다. 완료 결과는 소유 UTF8로 반환하며 UI publication/sourceepoch/원문에 대한 권한은 갖지 않는다. child/OS/cache·관리문자열 완전 삭제는 주장하지 않는다.

별도 읽기 보안 검토 후 RED→GREEN: malformed/oversized raster·alpha/bytes/source zero·invalid UTF8/output/count·pre-cancel과 blocked child cancellation·출력과다/nonzero/timeout/finalcleanup·고정 argv/환경·Linux 실제 kor+eng 합성 인식. 가짜 OCR 응답이나 설정만으로 기능 상태를 올리지 않는다. Windows 고정 native 엔진의 라이선스/설치 크기/CPU/RAM/전체 payload hash와 실제 인식, global admission/sourceepoch/lock discard 및 결과 미리보기·명시 적용·암호 이력은 다음 경계다. 모델 설치는 사용자 선택 설정으로 분리한다. 자동 다운로드/외부 fallback은 없다.

참조: https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr/ ; https://tesseract-ocr.github.io/tessdoc/Compiling.html ; https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html . 현재 Linux binary 및 official pinned model 근거는 local-ocr-probe 문서를 따른다. native C++ build가 추가 CI 비용/라이선스 동의를 요구하면 해당 단계만 보류하고 정확한 조건을 보고한다.

독립 사전 읽기 검토의 Important 조건을 반영한다: stdin/stdout/stderr를 동시에 시작한다. 모든 operation 전체 timeout 및 출력 초과/취소에서 먼저 kill/pipe close, 이후 bounded cleanup(별도 최대2초)만 한다. 늦은 I/O 작업이 소유한 입력을 먼저 지우거나 반환하지 않으며, 소유 cleanup continuation에서 최종 zero한다. OS 강제 종료20초 보장은 주장하지 않는다. 실행 전 모델별 고정1677415B/4113088B와 각각SHA256을 검증하고 고정ArgumentList(언어kor+eng/PSM6/OEM1/stdin/stdout), 비밀 없는model working dir, 최소Environment로LD_PRELOAD/LD_LIBRARY_PATH/TESSDATA_PREFIX 등 상속을 차단한다. 내부 Linux binary 검증은74b76a5b994adcc18be362bf64ed5118082b2401d5d9a35242e92fc4c62e2b4b 및public SHA/model trust만 사용한다. Windows 전체 native binary/DLL provenance는 후속 사전조건으로 남긴다.

Windows native 탐색 고정 소스: Tesseract5.5.3 db0ec62f81b0737fbbe184d8fea40af5738f8eef, Leptonica1.87.0 13275a278eb55b5746e33f95fbf5a2c8f604b3ab, tessdata_fast87416418657359cb625c412a48b6e1d6d41c29bd. 기존 MSVC를 사용해 static CRT/Leptonica 및 PNM-only·LSTM-only engine을 빌드하고 curl/archive/graphics/optional codecs를 끈다. 정확한 tag SHA를 검사한 후만 CMake를 실행한다. 실제 native 출력version/engine SHA·size/공식model/전체component notice를 검사한다. self-generated manifest는 이 CI 내부 build와 harness를 잇는 근거일 뿐 production publisher authenticity가 아니다. 표준 공개 Windows runner만 사용하며 artifact/cache upload 없음. 이 bundle은 앱에 설치·배포하지 않는다.

공개 합성 이미지: tests/fixtures/ocr-synthetic-png.base64, PNG25955B SHA21f8894d43b5c5bfa13db5aaf1503ad726daf4675a01623e5c5778b0a5f89ca8. D2Coding1.4.0 기존 OFL 글꼴로 자체 생성한 한글4행/ABC123 시험 이미지이며 사용자 자료가 아니다. 폰트 notice는 docs/licenses/D2Coding1.4.0-OFL.txt에 보존한다.

독립 native source 읽기 검토 Critical/Important 없음(실행 안 함). 번들에 DLL이 없다는 사실만으로 static link를 주장하지 않고 실제 dumpbin PE imports의 OS DLL whitelist 및 CMake selected Leptonica_DIR가 고정 설치prefix 안인지 검사한다. optional codec 비활성화와 별개로 Leptonica 내장 형식 코드가 남으므로 입력 경계는 앱 생성 PPM만 전달이다. workflow .NET10.0.401은 기존 공식 SHA512 검증 설치 절차를 재사용하며 추가 native compiler/Windows SDK를 설치하지 않는다.
