# N07 Excel 제목·본문 가져오기

범위는 offline XLSX의 첫 worksheet 제목/본문 문자열 셀이다. 첫 시트임과 일반 2열(제목/본문 또는 Title/Body), 메모앱 5열(제목/본문1~3/원본모드)만 지원하며 모두 plain note로 추가한다고 최종 확인에 표시한다. 서식/첨부/다른 시트는 가져오지 않고 원본 파일은 변경하지 않는다. 일반 .xls, macro, formula/numeric/date/외부 연결, 모호한 헤더/좌표/part/relationship은 원본 보존 거절한다.

16MiB ZIP/128part/실제 해제 aggregate96MiB/16sheet/100note/10,000 shared strings; reader의 실제 read와 XML node/문자한도를 먼저 검사하고 ZIP length/count/dimension 선언을 신뢰하지 않는다. 파일 추출·외부 실행·resolver/DTD를 금지한다. workbook 순서의 첫 sheet를 유효한 내부 relationship로 찾는다. 모든 package 관계의 외부/중복/URI/상위경로를 거절한다. shared string text/run은 t 또는 r(rPr/t)만 plain projection으로 읽고 phonetic/unknown markup은 거절한다. formula cache도 거절한다. SpreadsheetML escape는 단일 pass 후 Unicode/제어문자/decoded cell32767/title256/body65536/총UTF8한도를 검증한다.

Workspace의 새 ImportTexts는 전체 candidate를 사전 검증한 후 모든 draft를 한꺼번에 등록/AcceptPrepared하고 한 번 Changed 및 collection notifications를 게시한다. 이는 기존 ImportText와 같은 메모리 적용 경계이며 disk durable commit이라고 부르지 않는다. UI는 이후 SaveAsync를 기다린다. malformed/count/payload 거절은 workspace/cipher 모두 불변; 적용 이후 save 실패는 전체 dirty batch+기존 cipher를 보존하며 '추가했지만 암호 저장 실패'라고 표시한다. 잠금/notification reentry는 전체 batch만 보거나 즉시 닫힌 draft를 보며 일부 late publication을 하지 않는다. 원래 dirty 변경을 rollback/delete하지 않는다.

검사: 내보낸5열 roundtrip, 일반2열/sharedstrings/Unicode escaping/surrogate, malformed ZIP/actual length/duplicate/case ambiguity/traversal/DTD/externalrels/formula/cache/extra columns/decodedlimits/boundedenumeration, 원본hash 보존, 기존100노트/전체payload 실패 불변, 한 번 event/notification wholebatch와 lock, 실제 암호 restart/flushfailure/commit 중lock, Windows picker/read/confirm current guard와 truthful failure. 현재 사용자 실제 PC 시험은 구현의 선행 차단 조건이 아니다.
