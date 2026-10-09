import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location('native_ocr', pathlib.Path(__file__).parents[1] / 'tools/build_native_ocr.py')
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)

class NativeCompilerProbeTests(unittest.TestCase):
 def test_dumpbin_follows_actual_selected_compiler_not_other_installed_versions(self):
  cache = 'C:/Program Files/Microsoft Visual Studio/2022/Enterprise/VC/Tools/MSVC/14.44.35207/bin/Hostx64/x64/cl.exe\n'
  expected = pathlib.Path('C:/Program Files/Microsoft Visual Studio/2022/Enterprise/VC/Tools/MSVC/14.44.35207/bin/Hostx64/x64/dumpbin.exe')
  self.assertEqual(native.compiler_dumpbin(cache), expected)
 def test_missing_selected_msvc_compiler_is_refused(self):
  with self.assertRaises(ValueError):
   native.compiler_dumpbin('')
