"""Package only published executable/runtime/license files; no user data inputs."""
import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path(sys.argv[1]).resolve()
if (root/'installation-manifest.json').exists():raise SystemExit('Refusing to overwrite existing installation manifest')
allowed={'MemoApp.Windows.exe','MemoApp.Windows.deps.json','MemoApp.Windows.runtimeconfig.json','licenses/Markdig1.4.0.txt','licenses/DotNet10.txt','licenses/DotNet10-ThirdPartyNotices.txt','licenses/WindowsDesktop10.txt','Start-Portable.cmd','Install-User.cmd','README.txt','USER-TESTS.txt'}
files=[]
for p in sorted(root.rglob('*')):
 if p.is_file():
  if p.is_symlink():raise SystemExit('Linked publish payload refused')
  relative=p.relative_to(root).as_posix()
  # Diagnostic dump helper is not an application dependency; never ship a dump launcher.
  if p.suffix.lower()=='.pdb' or relative=='createdump.exe':p.unlink();continue
  if not (p.suffix.lower()=='.dll' or relative in allowed):raise SystemExit('Unexpected publish payload: '+relative)
  files.append({'path':p.relative_to(root).as_posix(),'size':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
assert 1<=len(files)<=512 and sum(x['size'] for x in files)<=536870912
manifest={'schemaVersion':1,'sourceCommit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'files':files}
(root/'installation-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS: bounded published installation manifest',len(files),'files')
