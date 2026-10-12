import importlib.util
import hashlib
import pathlib
import tempfile
import unittest
from unittest import mock

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


# Exact function from Leptonica 1.87.0, commit 13275a278eb55b5746e33f95fbf5a2c8f604b3ab.
# Surrounding source is synthetic so these patch tests need no checkout, network,
# compiler, or native process on either Windows or Linux.
ORIGINAL_MEMORY_PNM = r'''PIX *
pixReadMemPnm(const l_uint8  *data,
              size_t          size)
{
FILE  *fp;
PIX   *pix;

    if (!data)
        return (PIX *)ERROR_PTR("data not defined", __func__, NULL);
    if ((fp = fopenReadFromMemory(data, size)) == NULL)
        return (PIX *)ERROR_PTR("stream not opened", __func__, NULL);
    pix = pixReadStreamPnm(fp);
    fclose(fp);
    if (!pix) L_ERROR("pix not read\n", __func__);
    return pix;
}
'''
REPLACEMENT_MEMORY_PNM = '''/* Synthetic replacement: test patching, not decoding. */
PIX *
pixReadMemPnm(const l_uint8 *data, size_t size)
{
    return NULL;
}
'''


class MemoryPnmPatchTests(unittest.TestCase):
 def setUp(self):
  self.temporary = tempfile.TemporaryDirectory(prefix='native-pnm-test-', dir=pathlib.Path(__file__).parent)
  self.addCleanup(self.temporary.cleanup)
  self.root = pathlib.Path(self.temporary.name)
  (self.root / 'src').mkdir()
  self.source = self.root / 'src' / 'pnmio.c'
  self.replacement = self.root / 'memory-pnm.c'
  self.replacement.write_bytes(REPLACEMENT_MEMORY_PNM.encode('utf-8'))
  self.prefix = '/* Synthetic preceding source. */\nint before_reader = 17;\n\n'
  self.suffix = '\n/* Synthetic following source. */\nint after_reader = 29;\n'
  self.original = (self.prefix + ORIGINAL_MEMORY_PNM + self.suffix).encode('utf-8')
  self.source.write_bytes(self.original)
  self.addCleanup(mock.patch.stopall)
  mock.patch.object(native, 'LEPTONICA_PNM_SHA256', self.digest(self.original), create=True).start()
  mock.patch.object(native, 'MEMORY_PNM_SOURCE', self.replacement, create=True).start()
  self.run = mock.patch.object(native.subprocess, 'run').start()
  self.check_output = mock.patch.object(native.subprocess, 'check_output').start()

 @staticmethod
 def digest(content):
  return hashlib.sha256(content).hexdigest()

 def assert_refused_untouched(self, content):
  self.source.write_bytes(content)
  with self.assertRaises(ValueError):
   native.apply_memory_pnm_patch(self.root)
  self.assertEqual(self.source.read_bytes(), content)
  self.run.assert_not_called()
  self.check_output.assert_not_called()

 def test_mismatched_upstream_source_is_refused_without_writing(self):
  self.assert_refused_untouched(self.original + b'/* unexpected upstream change */\n')

 def test_applies_exactly_one_function_and_reports_actual_source_provenance(self):
  provenance = native.apply_memory_pnm_patch(self.root)
  expected = (self.prefix + REPLACEMENT_MEMORY_PNM + self.suffix).encode('utf-8')
  self.assertEqual(self.source.read_bytes(), expected)
  self.assertEqual(provenance, {
   'inputDecoder': 'memo-canonical-p6-memory-v1',
   'memoryPnmSha256': self.digest(self.replacement.read_bytes()),
   'upstreamPnmSha256': self.digest(self.original),
   'patchedPnmSha256': self.digest(self.source.read_bytes()),
  })
  self.run.assert_not_called()
  self.check_output.assert_not_called()

 def test_reapplying_patch_is_refused_without_changing_applied_source(self):
  native.apply_memory_pnm_patch(self.root)
  applied = self.source.read_bytes()
  self.assert_refused_untouched(applied)

 def test_duplicate_original_function_is_refused_even_with_matching_file_hash(self):
  duplicate = self.original + ORIGINAL_MEMORY_PNM.encode('utf-8')
  native.LEPTONICA_PNM_SHA256 = self.digest(duplicate)
  self.assert_refused_untouched(duplicate)

 def test_changed_original_function_is_refused_even_with_matching_file_hash(self):
  changed = self.original.replace(b'    fclose(fp);', b'    fclose(fp) ;')
  native.LEPTONICA_PNM_SHA256 = self.digest(changed)
  self.assert_refused_untouched(changed)

 def test_missing_original_function_is_refused_even_with_matching_file_hash(self):
  missing = (self.prefix + self.suffix).encode('utf-8')
  native.LEPTONICA_PNM_SHA256 = self.digest(missing)
  self.assert_refused_untouched(missing)

 def test_crlf_checkout_uses_the_same_normalized_source_hash_and_patch(self):
  self.source.write_bytes(self.original.replace(b'\n', b'\r\n'))
  provenance = native.apply_memory_pnm_patch(self.root)
  expected = (self.prefix + REPLACEMENT_MEMORY_PNM + self.suffix).encode('utf-8')
  self.assertEqual(self.source.read_bytes(), expected)
  self.assertEqual(provenance['upstreamPnmSha256'], self.digest(self.original))
  self.assertEqual(provenance['patchedPnmSha256'], self.digest(expected))

 def test_non_windows_build_refuses_before_native_execution_or_output_creation(self):
  output = self.root / 'not-created'
  with mock.patch.object(native.sys, 'platform', 'linux'):
   with self.assertRaises(RuntimeError):
    native.main(output)
  self.assertFalse(output.exists())
  self.run.assert_not_called()
  self.check_output.assert_not_called()
