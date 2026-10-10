import hashlib
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1]

class FontCheckoutChecks(unittest.TestCase):
 def test_fixed_ofl_bytes_survive_windows_style_git_checkout(self):
  relative='docs/licenses/D2Coding1.4.0-OFL.txt'
  original=(ROOT/relative).read_bytes()
  with tempfile.TemporaryDirectory(prefix='memo-public-font-') as folder:
   r=Path(folder);(r/'docs/licenses').mkdir(parents=True)
   (r/relative).write_bytes(original)
   (r/'.gitattributes').write_bytes((ROOT/'.gitattributes').read_bytes())
   def git(*args):
    return subprocess.run(['git','-c','core.autocrlf=true',*args],cwd=r,check=True,capture_output=True)
   git('init','--quiet');git('add','.gitattributes',relative)
   destination=r/'checked';git('checkout-index',f'--prefix={destination.as_posix()}/',relative)
   checked=(destination/relative).read_bytes()
   self.assertEqual(checked,original)
   self.assertEqual(hashlib.sha256(checked).hexdigest(),'1807e8dec4d65f474cbf9be39f5e2254ecb81702babc320749e272ea66ffcc69')

if __name__=='__main__':unittest.main()
