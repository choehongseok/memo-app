"""Package only published executable/runtime/license files; no user data inputs."""
import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path(sys.argv[1]).resolve()
if (root/'installation-manifest.json').exists():raise SystemExit('Refusing to overwrite existing installation manifest')
files=[]
for p in sorted(root.rglob('*')):
 if p.is_file():
  if p.suffix.lower()=='.pdb':p.unlink();continue
  if p.is_symlink() or p.suffix.lower() not in {'.exe','.dll','.json','.txt','.cmd'}:raise SystemExit('Unexpected publish payload')
  files.append({'path':p.relative_to(root).as_posix(),'size':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
assert 1<=len(files)<=512 and sum(x['size'] for x in files)<=536870912
manifest={'schemaVersion':1,'sourceCommit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'files':files}
(root/'installation-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS: bounded published installation manifest',len(files),'files')
