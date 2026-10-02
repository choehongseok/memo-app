"""Stage 0 source integrity. No product-behavior claims."""
from pathlib import Path
import hashlib,json,re,sys
ROOT=Path(__file__).resolve().parents[1]
SOURCE_SHA256='29655ee9235ebcc3e056551e1aeaf3af26781f1b43a93b78cd585e285c48957d'
def verify_ledger(source,ledger):
 section=source.split('4. 선택 기능 원본 — 총 134개\n',1)[1].split('5. 중복·충돌',1)[0]
 expected={i:n.strip() for i,n in re.findall(r'\b([A-Z]\d{2}) ([^/\n]+)',section)}
 assert len(expected)==134
 rows=ledger['features']; assert ledger['feature_count']==134 and ledger['schema_version']==1
 assert len(rows)==134,'ID 누락/추가'
 actual=[r['id'] for r in rows]
 assert len(set(actual))==134,'중복 ID'
 assert set(actual)==set(expected),'ID 누락/재번호'
 for r in rows:
  assert r['name']==expected[r['id']],'원선택 이름 변경'
  assert r['phase'] in range(1,6)
  assert r['user_acceptance']=='미확인','사용자 수용 미확인'
  assert r['acceptance_criteria'] and all(isinstance(x,str) and x for x in r['acceptance_criteria'])
  assert r['planned_module'] and isinstance(r['implementation_locations'],list)
  assert r['test_evidence']==[],'단계 0 기능 검증 주장 금지'
  if r['id']=='M06':
   assert r['status']=='정규화로 대체됨'
   assert r['normalized']=='정규화로 대체됨: M01~M05 유지'
  else:
   assert r['status']=='미착수','단계 0 제품 구현 주장 금지'
   assert r['normalized']==r['name'],'무단 정규화/제외'
 return len(rows)
def main():
 b=(ROOT/'docs/SOURCE_PROMPT.txt').read_bytes()
 assert len(b)==27417 and hashlib.sha256(b).hexdigest()==SOURCE_SHA256,'원문 변경'
 count=verify_ledger(b.decode(),json.loads((ROOT/'docs/FEATURES.json').read_text()))
 for path in ['AGENTS.md','README.md','docs/PRODUCT_SPEC.md','docs/STATUS.md','docs/VERIFICATION.md','global.json','MemoApp.slnx','.github/workflows/preparation.yml']:
  assert (ROOT/path).is_file(),f'필수 파일 누락: {path}'
 sdk=json.loads((ROOT/'global.json').read_text())['sdk']
 assert sdk=={'version':'10.0.401','rollForward':'disable','allowPrerelease':False}
 for p in ROOT.glob('**/*.csproj'): assert '<PackageReference' not in p.read_text(),'미검토 제품 의존성'
 for entry in ['*.db','*.key','*.pfx','user-data/','models/']: assert entry in (ROOT/'.gitignore').read_text()
 print(f'PASS: original 27417 bytes/SHA256, {count} exact IDs/names, M06 normalization, preparation boundaries')
 print('LIMIT: no Windows execution/encryption/storage/recovery/sync/Android/AI verification')
if __name__=='__main__':
 try: main()
 except (AssertionError,KeyError,ValueError) as e: print(f'FAIL: {e}',file=sys.stderr);sys.exit(1)
