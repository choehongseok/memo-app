import importlib.util, unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('verify_ci_module',ROOT/'tools/verify_preparation.py');verify=importlib.util.module_from_spec(spec);spec.loader.exec_module(verify)
class CiScopeChecks(unittest.TestCase):
 def test_development_branch_uses_one_pull_request_event(self):
  verify.verify_ci_scope((ROOT/'.github/workflows/preparation.yml').read_text(encoding='utf-8'))
 def test_unrestricted_dual_push_and_pr_rejected(self):
  with self.assertRaises(AssertionError):verify.verify_ci_scope('on:\n  push:\n  pull_request:\n  workflow_dispatch:\n')
if __name__=='__main__':unittest.main()
