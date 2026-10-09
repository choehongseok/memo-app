"""A single committed request can upload only its exact source/test trees.
No request, document-only follow-up, or later code change enables an upload.
CI also limits this to the repository's own development PR, first run attempt.
"""
import json,os,pathlib,re,subprocess

def git(*arguments):
 return subprocess.check_output(['git',*arguments],text=True).strip()

changed=git('diff','--name-only','HEAD^','HEAD').splitlines()
request='docs/ARTIFACT_UPLOAD_REQUEST.json'
allow=False
if request in changed:
 data=json.loads(pathlib.Path(request).read_text(encoding='utf-8'))
 assert set(data)=={'schemaVersion','sourceTree','testTree','purpose'} and data['schemaVersion']==1
 assert data['purpose']=='authorized-final-windows-trial'
 assert all(re.fullmatch('[0-9a-f]{40}',data[key]) for key in ['sourceTree','testTree'])
 assert data['sourceTree']==git('rev-parse','HEAD:src') and data['testTree']==git('rev-parse','HEAD:tests'), 'Requested tested trees changed'
 allow=True
print('Final trial upload request:', 'exact committed trees' if allow else 'none for this commit')
output=os.environ.get('GITHUB_OUTPUT')
if output:
 with open(output,'a',encoding='utf-8') as stream:stream.write('allowed='+str(allow).lower()+'\n')
