import json,pathlib,subprocess,tempfile,unittest
TOOL=pathlib.Path(__file__).resolve().parents[1]/'tools/check_artifact_request.py'
class ArtifactRequestChecks(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.temp.name)
  self.git('init','-q');self.git('config','user.name','Synthetic Test');self.git('config','user.email','synthetic@example.invalid')
  for name in ['src','tests','docs']:(self.root/name).mkdir()
  (self.root/'src/fixture.cs').write_text('// synthetic');(self.root/'tests/fixture.txt').write_text('synthetic')
  self.commit('base');(self.root/'docs/readme.txt').write_text('synthetic');self.commit('baseline')
 def tearDown(self):self.temp.cleanup()
 def git(self,*args):return subprocess.check_output(['git',*args],cwd=self.root,text=True).strip()
 def commit(self,message):self.git('add','.');self.git('commit','-qm',message)
 def request(self,source=None):
  data={'schemaVersion':1,'sourceTree':source or self.git('rev-parse','HEAD:src'),'testTree':self.git('rev-parse','HEAD:tests'),'purpose':'authorized-final-windows-trial'}
  (self.root/'docs/ARTIFACT_UPLOAD_REQUEST.json').write_text(json.dumps(data))
 def run_tool(self):return subprocess.run(['python3',str(TOOL)],cwd=self.root,capture_output=True,text=True)
 def test_no_request_stays_off(self):
  result=self.run_tool();self.assertEqual(result.returncode,0);self.assertIn('none for this commit',result.stdout)
 def test_exact_marker_only_allows(self):
  self.request();self.commit('request');result=self.run_tool();self.assertEqual(result.returncode,0);self.assertIn('exact committed trees',result.stdout)
 def test_bundled_new_source_refused_even_matching_trees(self):
  (self.root/'src/fixture.cs').write_text('// changed');self.git('add','src');source=self.git('write-tree');source=self.git('rev-parse',source+':src')
  self.request(source);self.commit('bundled');self.assertNotEqual(self.run_tool().returncode,0)
 def test_bundled_workflow_refused(self):
  self.request();(self.root/'docs/workflow.txt').write_text('changed');self.commit('bundled');self.assertNotEqual(self.run_tool().returncode,0)
 def test_bad_tree_refused(self):
  self.request('a'*40);self.commit('request');self.assertNotEqual(self.run_tool().returncode,0)
 def test_later_docs_do_not_repeat_upload(self):
  self.request();self.commit('request');(self.root/'docs/readme.txt').write_text('followup');self.commit('followup');result=self.run_tool();self.assertEqual(result.returncode,0);self.assertIn('none for this commit',result.stdout)
