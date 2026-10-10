# N03/N04 선택 메모의 Excel/Word 평문 내보내기

기존 TXT projection의 unknown-rich 거절·선택/version/epoch·명시 평문 경고·새 파일만 생성·잠금 취소·활성 vault 밖 로컬 경계를 재사용한다. Microsoft Office나 외부 서비스/유료 library를 필요로 하지 않고 BCL ZIP/XML로 Office Open XML .xlsx/.docx의 최소 필수 package를 만든다. 서식 원문과 첨부는 전체 암호 이전 파일로 보존하며 이 명령은 제목/검증된 본문 projection/원본 모드만 내보낸다고 명확히 표시한다. 자동 외부 실행은 하지 않는다.

최대100개/원문 UTF8합계16MiB/package16MiB; Excel body65536은 최대32767 UTF16 cell 제한을 지키며 surrogate pair를 자르지 않고 3개 본문 열로 분리한다. 모든 Excel cell은 inline string이라 formula/URL/외부 참조를 실행하지 않으며 SpreadsheetML escape-looking 원문은 문자로 보존한다. Word는 제목 bold와 본문 text/tab/linebreak만 가진다. XML에서 표현할 수 없는 control은 원문 변경 없이 거절한다. package content types와 relationships/parts는 고정이고 macro/external relationship은 만들지 않는다.

공식 형식 근거(2026-10-09 확인): https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/structure-of-a-spreadsheetml-document ; https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.inlinestring ; https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document . Microsoft Office 실제 열기/렌더링은 Windows 합성 package/control 검사와 구분한다. N07 가져오기 어댑터는 boundedZIP/DTD금지/외부참조거절 및 전체 candidate 원자 검증 후 별도로 연결한다.
