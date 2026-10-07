"""Source/ledger integrity across stages. No product-behavior claims."""
from pathlib import Path
import hashlib,json,re,sys,xml.etree.ElementTree as ET
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
  assert isinstance(r['test_evidence'],list)
  assert r['status'] in {'미착수','진행 중','구현됨·미검증','검증됨','차단됨','정규화로 대체됨'}
  if r['status']=='검증됨':
   assert r['test_evidence'],'근거 없는 검증 완료 금지'
   assert any(e.get('kind')=='feature-acceptance' and e.get('result')=='PASS' for e in r['test_evidence']), '부분 검사만으로 기능 전체 검증 완료 금지'
   for evidence in r['test_evidence']:
    assert all(evidence.get(k) for k in ['commit','environment','command','result']), '불완전 검증 근거'
  if r['id']=='M06':
   assert r['status']=='정규화로 대체됨'
   assert r['normalized']=='정규화로 대체됨: M01~M05 유지'
  else:
   assert r['normalized']==r['name'],'무단 정규화/제외'
 return len(rows)
MARKDIG_CONTENT_HASH='lLhaiI/mNDTNzgLqnmknhf4LjgTqOAyjx5+GNz0X3AxYMxUhmFppF2WVMdER+SDqwhE5wUF1pzs/cuLwFxs7xA=='
def verify_ci_scope(text):
 events=text.split('on:\n',1)[1].split('permissions:\n',1)[0]
 assert '  push:\n    branches: [main, preparation/stage-0]\n  pull_request:\n' in events,'개발 PR의 중복 push/PR 전체검사 금지'
 assert '  workflow_dispatch:\n' in events and 'default: false' in events,'명시적 기본off trial upload 유지'
def verify_dependencies(root):
 root=Path(root).resolve()
 manifest_path=root/'docs/DEPENDENCY_ALLOWLIST.json'
 assert manifest_path.is_file(),'의존성 allowlist 누락'
 manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
 assert manifest.get('schema_version')==1 and len(manifest.get('packages',[]))==1,'미검토 의존성 allowlist'
 package=manifest['packages'][0]
 assert (package.get('id'),package.get('version'),package.get('project'),package.get('content_hash'),package.get('license'))==('Markdig','1.4.0','src/MemoApp.Core/MemoApp.Core.csproj',MARKDIG_CONTENT_HASH,'BSD-2-Clause'),'검토된 의존성 pin 변경'
 allowed_tfms=set(package.get('allowed_tfms',[]))
 assert allowed_tfms=={'net10.0','net10.0-windows7.0'},'미검토 의존성 framework'
 notice=(root/package['license_path']).resolve()
 assert notice.is_relative_to(root) and notice.is_file(),'배포 고지 누락/경로 탈출'
 assert hashlib.sha256(notice.read_bytes()).hexdigest()=='7423242b4ae72bccdf19a06cd3c20790df8519a164a08f37b31cb4a40034d827','BSD 전체 고지 byte/hash 변경'
 license_text=notice.read_text(encoding='utf-8')
 assert all(part in license_text for part in ['Copyright (c) 2016-2026, Alexandre Mutel','Redistribution and use in source and binary forms','THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS']),'BSD 고지 누락'
 files=[p for p in root.rglob('*') if p.is_file() and p.suffix in {'.csproj','.props','.targets'} and not any(part in {'bin','obj','.git','artifacts'} for part in p.relative_to(root).parts)]
 projects=[p for p in files if p.suffix=='.csproj'];names={p.stem.lower() for p in projects};references=[];locked=[];lock_enabled=[];publish_notices=[]
 for path in files:
  assert path.resolve().is_relative_to(root),'의존성 설정 경로 탈출'
  tree=ET.parse(path).getroot()
  parents={child:parent for parent in tree.iter() for child in parent}
  def unconditional(value):
   while value is not None:
    assert 'Condition' not in value.attrib,'의존성/잠금/고지 조건부 선언'
    value=parents.get(value)
  for value in tree.iter():
   tag=value.tag.split('}')[-1]
   if tag in {'RestoreLockedMode','RestorePackagesWithLockFile'}:
    unconditional(value)
    assert path==root/'Directory.Build.props' and not value.attrib and (value.text or '').strip()=='true','잠금 restore override'
    (locked if tag=='RestoreLockedMode' else lock_enabled).append(value)
   if tag=='PackageReference':
    unconditional(value)
    assert path.relative_to(root).as_posix()==package['project'] and value.attrib=={'Include':'Markdig','Version':'[1.4.0]'} and len(value)==0,'미검토 직접 의존성/버전'
    references.append(value)
   if tag in {'PackageVersion','GlobalPackageReference'}:raise AssertionError('미검토 props/targets 의존성')
   if tag=='None' and any('Markdig1.4.0.txt' in entry for entry in value.attrib.values()):
    unconditional(value)
    assert path.relative_to(root).as_posix()=='src/MemoApp.Windows/MemoApp.Windows.csproj' and value.attrib=={'Include':'../../docs/licenses/Markdig1.4.0.txt','Link':'licenses/Markdig1.4.0.txt','CopyToOutputDirectory':'PreserveNewest','CopyToPublishDirectory':'PreserveNewest'} and len(value)==0,'배포 고지 복사 선언 변경'
    publish_notices.append(value)
 assert len(locked)==len(lock_enabled)==len(references)==1,'의존성/잠금 정책 누락/중복'
 assert len(publish_notices)==1,'배포 고지 복사 선언 누락/중복'
 for path in projects:
  target=ET.parse(path).getroot().find('.//TargetFramework')
  assert target is not None and target.text in {'net10.0','net10.0-windows'},'미검토 project framework'
  expected='net10.0-windows7.0' if target.text=='net10.0-windows' else 'net10.0'
  lock_path=path.parent/'packages.lock.json';assert lock_path.is_file(),'의존성 lockfile 누락'
  lock=json.loads(lock_path.read_text(encoding='utf-8'))
  assert lock.get('version') in {1,2} and isinstance(lock.get('dependencies'),dict) and expected in lock['dependencies'],'미검토 lockfile framework/형식'
  seen=False
  for framework,entries in lock['dependencies'].items():
   assert framework.split('/')[0] in allowed_tfms and isinstance(entries,dict),'미검토 lock framework'
   for name,info in entries.items():
    if info.get('type')=='Project':
     assert name.lower() in names,'미검토 project dependency'
     nested=info.get('dependencies',{});assert isinstance(nested,dict),'project dependency graph 형식'
     for dependency,version in nested.items():
      assert dependency=='Markdig' and version=='[1.4.0, 1.4.0]' or dependency.lower() in names and version=='[1.0.0, )','미검토 nested project dependency/pin'
     continue
    assert name=='Markdig' and info.get('resolved')=='1.4.0' and info.get('contentHash')==MARKDIG_CONTENT_HASH and not info.get('dependencies'),'미검토 전이 의존성/hash'
    direct=path.relative_to(root).as_posix()==package['project']
    assert info.get('type')==('Direct' if direct else 'Transitive'),'의존성 직접/전이 경계'
    if direct:assert info.get('requested')=='[1.4.0, 1.4.0]','의존성 lock range'
    seen=True
  assert seen,'검토된 의존성 lock 누락'
def main():
 b=(ROOT/'docs/SOURCE_PROMPT.txt').read_bytes()
 assert len(b)==27417 and hashlib.sha256(b).hexdigest()==SOURCE_SHA256,'원문 변경'
 count=verify_ledger(b.decode('utf-8'),json.loads((ROOT/'docs/FEATURES.json').read_text(encoding='utf-8')))
 for path in ['AGENTS.md','README.md','docs/PRODUCT_SPEC.md','docs/STATUS.md','docs/VERIFICATION.md','global.json','MemoApp.slnx','.github/workflows/preparation.yml']:
  assert (ROOT/path).is_file(),f'필수 파일 누락: {path}'
 sdk=json.loads((ROOT/'global.json').read_text(encoding='utf-8'))['sdk']
 assert sdk=={'version':'10.0.401','rollForward':'disable','allowPrerelease':False}
 verify_dependencies(ROOT)
 verify_ci_scope((ROOT/'.github/workflows/preparation.yml').read_text(encoding='utf-8'))
 for entry in ['*.db','*.key','*.pfx','user-data/','models/']: assert entry in (ROOT/'.gitignore').read_text(encoding='utf-8')
 print(f'PASS: original 27417 bytes/SHA256, {count} exact IDs/names, M06 normalization, scope invariants')
 print('LIMIT: this source/ledger check does not test app behavior; see the runtime checks')
if __name__=='__main__':
 try: main()
 except (AssertionError,KeyError,ValueError) as e: print(f'FAIL: {e}',file=sys.stderr);sys.exit(1)
