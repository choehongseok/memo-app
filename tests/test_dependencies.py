import importlib.util,json,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('verify_dependencies_module',ROOT/'tools/verify_preparation.py')
verify=importlib.util.module_from_spec(spec);spec.loader.exec_module(verify)
HASH='lLhaiI/mNDTNzgLqnmknhf4LjgTqOAyjx5+GNz0X3AxYMxUhmFppF2WVMdER+SDqwhE5wUF1pzs/cuLwFxs7xA=='
class DependencyPolicyChecks(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name);(self.root/'docs/licenses').mkdir(parents=True);(self.root/'src/MemoApp.Core').mkdir(parents=True)
  (self.root/'Directory.Build.props').write_text('<Project><PropertyGroup><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><RestoreLockedMode>true</RestoreLockedMode></PropertyGroup></Project>',encoding='utf-8')
  self.project=self.root/'src/MemoApp.Core/MemoApp.Core.csproj';self.project.write_text('<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Markdig" Version="[1.4.0]"/></ItemGroup></Project>',encoding='utf-8')
  self.notice=self.root/'docs/licenses/Markdig1.4.0.txt';self.notice.write_bytes((ROOT/'docs/licenses/Markdig1.4.0.txt').read_bytes())
  self.allowlist={'schema_version':1,'packages':[{'id':'Markdig','version':'1.4.0','project':'src/MemoApp.Core/MemoApp.Core.csproj','content_hash':HASH,'license_path':'docs/licenses/Markdig1.4.0.txt','license':'BSD-2-Clause','allowed_tfms':['net10.0','net10.0-windows7.0']}]};self.save_allowlist()
  self.lock={'version':1,'dependencies':{'net10.0':{'Markdig':{'type':'Direct','requested':'[1.4.0, 1.4.0]','resolved':'1.4.0','contentHash':HASH}}}};self.save_lock()
  self.windows=self.root/'src/MemoApp.Windows/MemoApp.Windows.csproj';self.windows.parent.mkdir();self.windows.write_text('<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup><ItemGroup><None Include="../../docs/licenses/Markdig1.4.0.txt" Link="licenses/Markdig1.4.0.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest"/></ItemGroup></Project>',encoding='utf-8')
  (self.windows.parent/'packages.lock.json').write_text(json.dumps({'version':1,'dependencies':{'net10.0-windows7.0':{'Markdig':{'type':'Transitive','resolved':'1.4.0','contentHash':HASH}}}}),encoding='utf-8')
 def tearDown(self):self.temp.cleanup()
 def save_allowlist(self):(self.root/'docs/DEPENDENCY_ALLOWLIST.json').write_text(json.dumps(self.allowlist),encoding='utf-8')
 def save_lock(self):(self.project.parent/'packages.lock.json').write_text(json.dumps(self.lock),encoding='utf-8')
 def check(self):verify.verify_dependencies(self.root)
 def test_exact_reviewed_dependency_passes(self):self.check()
 def test_unknown_reference_rejected(self):
  self.project.write_text(self.project.read_text(encoding='utf-8').replace('Markdig','UnknownParser'),encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_minimum_version_is_not_exact_pin(self):
  self.project.write_text(self.project.read_text(encoding='utf-8').replace('[1.4.0]','1.4.0'),encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_unlisted_transitive_rejected(self):
  self.lock['dependencies']['net10.0']['UnknownTransitive']={'type':'Transitive','resolved':'1.0','contentHash':'unknown'};self.save_lock()
  with self.assertRaises(AssertionError):self.check()
 def test_locked_hash_mismatch_rejected(self):
  self.lock['dependencies']['net10.0']['Markdig']['contentHash']='wrong';self.save_lock()
  with self.assertRaises(AssertionError):self.check()
 def test_missing_lock_rejected(self):
  (self.project.parent/'packages.lock.json').unlink()
  with self.assertRaises(AssertionError):self.check()
 def test_disabled_locked_restore_rejected(self):
  p=self.root/'Directory.Build.props';p.write_text(p.read_text(encoding='utf-8').replace('<RestoreLockedMode>true','<RestoreLockedMode>false'),encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_conditional_locked_restore_rejected(self):
  p=self.root/'Directory.Build.props';p.write_text(p.read_text(encoding='utf-8').replace('<PropertyGroup>','<PropertyGroup Condition="false">'),encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_missing_publish_license_declaration_rejected(self):
  self.windows.write_text('<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>',encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_conditionally_disabled_publish_license_rejected(self):
  self.windows.write_text(self.windows.read_text(encoding='utf-8').replace('<ItemGroup>','<ItemGroup Condition="false">'),encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_missing_license_notice_rejected(self):
  self.notice.unlink()
  with self.assertRaises(AssertionError):self.check()
 def test_truncated_license_rejected(self):
  self.notice.write_text('Copyright (c) 2016-2026, Alexandre Mutel\nRedistribution and use in source and binary forms\nTHIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS',encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_props_reference_rejected(self):
  (self.root/'Additional.props').write_text('<Project><ItemGroup><PackageReference Include="Markdig" Version="[1.4.0]"/></ItemGroup></Project>',encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_targets_locked_override_rejected(self):
  (self.root/'Additional.targets').write_text('<Project><PropertyGroup><RestoreLockedMode>false</RestoreLockedMode></PropertyGroup></Project>',encoding='utf-8')
  with self.assertRaises(AssertionError):self.check()
 def test_license_path_escape_rejected(self):
  self.allowlist['packages'][0]['license_path']='../outside';self.save_allowlist()
  with self.assertRaises(AssertionError):self.check()
 def test_unknown_framework_rejected(self):
  self.lock['dependencies']['net462']=self.lock['dependencies'].pop('net10.0');self.save_lock()
  with self.assertRaises(AssertionError):self.check()
 def test_project_entry_unknown_nested_dependency_rejected(self):
  self.lock['dependencies']['net10.0']['memoapp.windows']={'type':'Project','dependencies':{'UnknownNested':'1.0'}};self.save_lock()
  with self.assertRaises(AssertionError):self.check()
 def test_project_entry_wrong_nested_pin_rejected(self):
  self.lock['dependencies']['net10.0']['memoapp.windows']={'type':'Project','dependencies':{'Markdig':'[1.0.0, 9.0.0]'}};self.save_lock()
  with self.assertRaises(AssertionError):self.check()
if __name__=='__main__':unittest.main()
