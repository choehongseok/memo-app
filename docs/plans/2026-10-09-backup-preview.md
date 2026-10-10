# O08 기존 암호 백업 미리보기

기존 해제 세션에서 선택한 로컬 암호 파일을 읽기 전용으로 미리본다. 파일 worker는 경로/token과 bounded 암호문만 취급하고 키/UI/workspace를 받지 않는다. worker 완료 후 정확한 session/sourceepoch/lock을 검증하고 UI context에서 기존 vault의 authenticated recovery authority로 잠깐 복호화한다. envelope/header/전체 snapshot/모든 첨부 인증 검증을 마친 후 100개 이하의 제목·본문 256자 DTO와 sequence/count만 반환한다. 임시 root key/복호화 scratch는 기존 DecryptOwned Dispose로 끝내고 key/snapshot/첨부암호문은 viewer에 반환하지 않는다. native 메시지/파일 내용 실행·평문 temp·원본/current/previous/pending 쓰기는 없다.

미리보기는 별도 읽기 view로 만들고 main lock/source/session종료 전에 owner를 revoke한 뒤 title/body/list를 각각 clear하고 close한다. native setter/collection callback 뒤 authority를 재확인한다. 시작 picker 중 잠금, cipher read 중 잠금/변경, 잘못된 secret/다른 vault/tamper/truncated/large/URI/link, DTO 게재 중 reentry lock을 검사한다. 문서/이미지 렌더링 대신 plain bounded 문자열만 표시하며 managed/native 복사 완전 삭제를 약속하지 않는다.

상한: 기존 vault 파일 규격 한도, 최대100 notes와 note당title256/body256 grapheme-safe excerpt. 실제 전체 envelope 검증은 기존 구현을 재사용한다. 현재 소스/이력/조직/암호/잠금/nonce/state를 변경하지 않는다. O09 선택 복구는 미리보기 이후 별도 검토/구현이며 O08 구현을 복구 완료로 과장하지 않는다.
