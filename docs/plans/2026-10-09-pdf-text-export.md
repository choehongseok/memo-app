# N02 제한된 평문 PDF 연결

기존 TXT/Office 내보내기 경계에서 검증된 제목·본문을 독립 PDF로 복사한다. 원문/암호 저장/134 ID/미지원 rich 원문은 변경하지 않는다. 기본 No 확인, 정확한 선택·session/source epoch, 원본 버전, vault 외부 새 로컬 파일, CreateNew, 잠금 취소와 원본 불변을 기존 내보내기와 같이 검사한다. worker에는 준비된 byte 소유자·목적지·취소 token·파일 backend만 전달한다. 실패한 부분 평문 파일/외부 캐시는 자동 제거를 보장하지 않는다.

새 NuGet/시스템 글꼴/외부 서비스 없이 BCL로 PDF1.7 classic xref, Type0/CIDFontType2, CIDToGIDMap, ToUnicode를 쓴다. 입력 PDF 파서·JavaScript·URI·주석·원본 첨부를 제공하지 않는다. Markdown은 문자 그대로이며 서식/첨부 보존은 전체 암호 이전 기능을 사용한다. 100메모/256쪽/입력·출력 각각16MiB/쪽 stream32KiB/ToUnicode1MiB/CID16384 제한을 할당 도중 검사한다. 가변 byte buffer/중간 glyph map/완성 출력은 소유권 종료 시 지운다. GC 관리 문자열 완전 삭제는 주장하지 않는다.

공식 NAVER D2Coding VER1.4.0 commit b0dc372e28d7abdba7f3cba854855ceffbd490a8의 변경하지 않은 전체 regular TTF를 zlib 저장한다. 원본4119796B SHA256 d5f2b6bf5b1826cfbf3734c70582f0cd42ea2e1d6c2636ae681b612bc9c5682f, 저장2051259B SHA256 2f332f3a70c551b36761e68ac76787b6a3957b9888a87d97ab16a50957503bf4. fsType8의 editable embedding이며 subset하지 않는다. 저작권·reserved names·OFL1.1 전체4511B SHA256 1807e8dec4d65f474cbf9be39f5e2254ecb81702babc320749e272ea66ffcc69를 패키지에 포함하고 누락/변조 시 package/installed gate를 거절한다. 앱 라이선스와 글꼴 라이선스를 혼동하지 않는다.

고정 글꼴의 합성 한글/일부 한자/영문과 제한 script만 허용한다. cmap에 없는 글자, non-BMP emoji, 결합 문자, 현대 분해 자모, RTL, bidi/ZWJ/variation selector 등 shaping이 필요한 입력은 파일 생성 전 명시 거절한다. 대체·정규화로 원문을 바꾸지 않는다. 탭/줄은 읽기용 배치로 바뀌며 제목/본문 원래 문자열은 보존한다.

검증: missing-contract RED → Core targeted 및 전체 PASS; Windows 프로젝트 교차 빌드0warning0error. 독립 Poppler pdfinfo/pdffonts/pdftotext/pdftoppm의 실제 3쪽 A4/embedded not-subset Unicode font/한글·한자·영문 추출/첫쪽 렌더 확인. 독립 actual diff 읽기 검토 추가 Important 없음. 실제 Windows PDF UI/native setter/modal/lock/원본 불변 및 설치 notice 검사는 새 source CI에서 수행한다. Linux Poppler 결과는 Windows PDF reader의 사용자 수용이 아니다.

근거: https://naver.github.io/d2-coding-font/ ; https://github.com/naver/d2-coding-font/releases/tag/VER1.4.0 ; https://openfontlicense.org/ofl-faq/ ; https://learn.microsoft.com/en-us/typography/opentype/spec/cmap ; https://learn.microsoft.com/en-us/typography/opentype/spec/os2 ; https://pdfa.org/norm-refs/5411.ToUnicode.pdf .

GitHub connector의 큰 binary 요청 완료 응답이 없어 65536B 최대/32개 고정 리소스로 분할한다. 재조합2051259B와 compressed SHA256을 먼저 검사한 뒤 기존4119796B exact inflate/원본 SHA256을 검사한다. 원본 글꼴·OFL·PDF 출력 의미는 변경하지 않는다.
