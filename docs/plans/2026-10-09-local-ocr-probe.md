# T12 로컬 OCR 실행 탐색 — 제품 구현 아님

기존 범위는 로컬 OCR이며 외부 API·실메모 전송·유료 의존성은 추가하지 않는다. 아직 Windows 제품 엔진·UI·암호 저장 연결은 없으므로 T12의 미착수 상태를 유지한다.

현재 Linux 설치 엔진 Tesseract5.5.0/Leptonica1.84.1을 확인했다. 공식 tessdata_fast commit `87416418657359cb625c412a48b6e1d6d41c29bd`의 public 모델만 `/workspace/memo-tools/ocr-probe`에 탐색용으로 내려받았다. 앱 저장소/설치 패키지에 모델·합성 PNG를 넣지 않았다.

- kor.traineddata:1677415B SHA256 `6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2`.
- eng.traineddata:4113088B SHA256 `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`.
- upstream Apache2 LICENSE:11358B SHA256 `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`.

변경하지 않은 OFL D2Coding 글꼴로 흰 배경·검은64pt의 합성 네 줄 이미지를 만들었다. 내용은 `한글 메모 시험`, `합성 자료만 사용합니다`, `저장 암호 잠금 복구`, `ABC 123`이다. local-only `tesseract synthetic.png stdout --tessdata-dir tessdata -l kor --oem 1 --psm 6`의 첫 탐색은 한국어 세 줄을 읽었으나 영문ABC를2866으로 오인했다. `-l kor+eng` actual 실행 결과는 네 줄의 공백을 제외한 문자열이 expected와 정확히 일치했다. 원시 결과의 빈 줄 차이도 숨기지 않는다. 이는 한 장의 깨끗한 합성 이미지 탐색이며 일반 정확도·손글씨·Windows 실행·앱 OCR 지원 증거가 아니다.

제품 연결 전에는 고정 Windows 엔진과 종속 라이선스/모델 선택 설치·CPU/RAM/용량, PNG의 기존 제한과 앱 전체 작업 제한, source/epoch/lock 폐기, 키 없는 worker, child 취소/출력 한도, 모델·실행 파일 신뢰, 암호 파생물 저장과 적용 전 미리보기를 별도 검토한다. 자동 클라우드 대체·사용자 모델 자동 설치·원본 덮어쓰기는 하지 않는다.

공식 근거: [Tesseract 설치/라이선스](https://tesseract-ocr.github.io/tessdoc/Installation.html), [Windows source compilation](https://tesseract-ocr.github.io/tessdoc/Compiling.html), [모델 제약](https://tesseract-ocr.github.io/tessdoc/Data-Files.html), [고정 모델 원본](https://github.com/tesseract-ocr/tessdata_fast/tree/87416418657359cb625c412a48b6e1d6d41c29bd). Windows 내장 OCR의 기기별 language pack 전제도 [Microsoft 공식 문서](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.availablerecognizerlanguages)를 확인했으며 내장 엔진으로 임의 대체하거나 한국어 pack 존재를 가정하지 않는다.
