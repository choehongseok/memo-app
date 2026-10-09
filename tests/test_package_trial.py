import hashlib,json,pathlib,subprocess,tempfile,unittest
ROOT=pathlib.Path(__file__).resolve().parents[1]
class PackageTrialChecks(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.temp.name)
  for name in ['MemoApp.Windows.exe','MemoApp.Windows.dll','coreclr.dll','MemoApp.Windows.deps.json','MemoApp.Windows.runtimeconfig.json','Start-Portable.cmd','Install-User.cmd','createdump.exe','MemoApp.Windows.pdb','licenses/Markdig1.4.0.txt','licenses/DotNet10.txt','licenses/DotNet10-ThirdPartyNotices.txt','licenses/WindowsDesktop10.txt']:
   p=self.root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(('synthetic '+name).encode())
 def tearDown(self):self.temp.cleanup()
 def run_tool(self):return subprocess.run(['python3',str(ROOT/'tools/package_trial.py'),str(self.root)],cwd=ROOT,capture_output=True,text=True)
 def test_exact_payload_hashes_dump_debug_removed(self):
  self.assertEqual(self.run_tool().returncode,0)
  data=json.loads((self.root/'installation-manifest.json').read_text());names={x['path'] for x in data['files']}
  self.assertNotIn('createdump.exe',names);self.assertNotIn('MemoApp.Windows.pdb',names);self.assertIn('coreclr.dll',names)
  for entry in data['files']:
   payload=(self.root/entry['path']).read_bytes();self.assertEqual(len(payload),entry['size']);self.assertEqual(hashlib.sha256(payload).hexdigest(),entry['sha256'])
 def test_regeneration_refuses_original_manifest(self):
  self.assertEqual(self.run_tool().returncode,0);p=self.root/'installation-manifest.json';before=p.read_bytes();self.assertNotEqual(self.run_tool().returncode,0);self.assertEqual(p.read_bytes(),before)
 def test_unexpected_data_and_launcher_refused(self):
  for name in ['private.vault','unreviewed.exe']:
   p=self.root/name;p.write_bytes(b'synthetic');self.assertNotEqual(self.run_tool().returncode,0);self.assertFalse((self.root/'installation-manifest.json').exists());p.unlink()
