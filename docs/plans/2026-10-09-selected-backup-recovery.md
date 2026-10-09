# O09 선택 백업 메모 새 복사 복구

O08의 동일 vault read-only 인증을 재사용하며 원본 backup/current 메모를 덮어쓰거나 삭제하지 않는다. 화면에서 선택한 백업 메모를 fresh NoteId/RevisionId의 새 복사로 가져온다는 명시 확인을 받는다. 제목/본문/원본 rich JSON/mode/created date/색상·중요·즐겨찾기·고정·보관/조직/첨부 원본은 보존한다. 복구된 새 복사는 Deleted=false/ModifiedAt=복구 시각/parents=[]이고 과거 이력은 기존 메모와 원본 백업에서 보존한다. 전체 이력·기기 UI 재적용은 전체 자료 복구와 구분한다.

미리본 source의 SHA256과 선택 Guid를 UI authority로 보유하고 복구 시 bounded cipher read를 다시 수행해 exact hash를 확인한다. 파일 worker는 path/token만 받는다. picker/read/confirm/session/sourceepoch/lock 이후 추가를 거절한다. main UI context에서 같은 vault 전체 인증 후 잠깐 얻은 detached snapshot을 후보 조립에만 사용하며 임시 root key는 복호화 단계에서 Dispose된다. 키/UI/workspace는 worker에 보내지 않는다.

관련 폴더의 조상·태그·선택 첨부만 현재 candidate에 병합한다. 기존 ID의 폴더/태그/첨부가 의미·부모·암호문과 다르면 재해석/덮어쓰기 없이 전체 거절한다. 폴더·태그 이름 충돌/한도도 사전 검증으로 전체 거절한다. 첨부가 있으면 현재와 backup의 실제 AttachmentRootId가 일치해야 하며 ID만으로 신뢰하지 않는다. 전체 후보의 선택 object를 현재 vault/root key로 실제 인증한 뒤에만 적용한다. 같은 vault/rootID·다른 rootkey 합법 envelope도 적용 이전 불변 거절한다. 선택되지 않은 object/history/UI는 새 복사에 가져오지 않으며 기존 current의 모든 object/history/UI/root를 그대로 유지한다.

원자 경계는 전체 후보 사전 VaultEnvelope.Validate 후 모든 새 draft를 등록하고 AcceptPrepared·한 번 Changed·닫힘 여부를 보는 collection notifications이다. 저장은 기존 SaveAsync를 이어 기다린다. 적용 이전 실패는 정확한 current/workspace/source 불변이고, 적용 뒤 저장/알림 실패는 모든 새 복사+기존 dirty를 유지하여 사실대로 안내한다. 숨김/닫힘 view에는 결과를 재게재하지 않는다. 원문134/M06/암호 구조/이전 JSON literal는 바꾸지 않는다.

검사: 실제 백업 unknown rich 원문/메타데이터/암호 첨부 같은 bytes·현재 충돌 원본 불변·fresh IDs·한 번 wholebatch event; 폴더/태그/object/root/100notes/payload 충돌 거절 불변; savefailure whole dirty 보존·restart·lock/reentry; UI hash replacement/confirm sourceedit/취소/명시 새 복사/선택 restore 결과 및 키 없는 worker. O09 전체 수용/사용자 실제PC는 별도 기록한다.
