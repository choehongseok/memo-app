import copy,importlib.util,json,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('verify',ROOT/'tools/verify_preparation.py')
verify=importlib.util.module_from_spec(spec);spec.loader.exec_module(verify)
class LedgerDriftChecks(unittest.TestCase):
 def setUp(self):
  self.source=(ROOT/'docs/SOURCE_PROMPT.txt').read_text()
  self.ledger=json.loads((ROOT/'docs/FEATURES.json').read_text())
 def test_original_selection_is_exact(self):
  self.assertEqual(134,verify.verify_ledger(self.source,self.ledger))
 def test_missing_android_id_is_rejected(self):
  x=copy.deepcopy(self.ledger);x['features']=[r for r in x['features'] if r['id']!='Q04']
  with self.assertRaises(AssertionError):verify.verify_ledger(self.source,x)
 def test_duplicate_id_is_rejected(self):
  x=copy.deepcopy(self.ledger);x['features'][-1]=copy.deepcopy(x['features'][0])
  with self.assertRaises(AssertionError):verify.verify_ledger(self.source,x)
 def test_name_change_is_rejected(self):
  x=copy.deepcopy(self.ledger);x['features'][0]['name']='임의 축소'
  with self.assertRaises(AssertionError):verify.verify_ledger(self.source,x)
 def test_m06_adoption_is_rejected(self):
  x=copy.deepcopy(self.ledger);next(r for r in x['features'] if r['id']=='M06')['status']='미착수'
  with self.assertRaises(AssertionError):verify.verify_ledger(self.source,x)
 def test_unproven_completion_is_rejected(self):
  x=copy.deepcopy(self.ledger);x['features'][0]['status']='검증됨'
  with self.assertRaises(AssertionError):verify.verify_ledger(self.source,x)
if __name__=='__main__':unittest.main()
