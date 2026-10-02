"""Heuristic staged/tracked file scan. Not a security audit."""
from pathlib import Path
import re,subprocess,sys
root=Path(__file__).resolve().parents[1]
paths=subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0')
for name in filter(None,paths):
 p=root/name
 if p.suffix.lower() in {'.db','.sqlite','.key','.pem','.pfx','.p12','.dmp','.usr','.exe','.zip'}:
  sys.exit('FAIL: forbidden data/binary path: '+name)
 patterns=[b'-----BEGIN '+b'(?:RSA |EC |OPENSSH )?PRIVATE KEY-----',b'gh[pousr]_'+b'[A-Za-z0-9]{30,}',b'AKIA'+b'[A-Z0-9]{16}']
 if any(re.search(pattern,p.read_bytes()) for pattern in patterns):sys.exit('FAIL: potential secret in '+name)
print('PASS: tracked heuristic secret/data-artifact scan; no content printed')
