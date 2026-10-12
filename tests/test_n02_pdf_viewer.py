"""Fixed public synthetic fixtures only; no product renderer used as an oracle."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
import zlib

ROOT = Path(__file__).resolve().parents[1]
TOOL = ROOT / "tools/verify_n02_pdf_viewer.py"
WIDTH, HEIGHT = 794, 1123
RAW_BYTES = WIDTH * HEIGHT * 3
WHITE = bytes([255]) * RAW_BYTES
NAMES = ("basic", "large", "wrap")


def synthetic_rgb(color=(20, 70, 160)):
    pixels = bytearray(WHITE)
    # Deliberately asymmetric, colored marks catch blank, flipped, swapped-channel
    # or disconnected-image output from the real independent viewer.
    for y in range(80, 145):
        for x in range(60, 200):
            offset = (y * WIDTH + x) * 3
            pixels[offset:offset + 3] = bytes(color)
    for y in range(600, 620):
        for x in range(500, 630):
            offset = (y * WIDTH + x) * 3
            pixels[offset:offset + 3] = b"\xa0\x20\x40"
    return bytes(pixels)


def synthetic_pdf(rgb=None, compressed=None, change=None):
    """Independent fixture encoder, including recomputed offsets after mutations."""
    if compressed is None:
        compressed = zlib.compress(synthetic_rgb() if rgb is None else rgb)
    draw = b"q\n595.2755905511812 0 0 841.8897637795276 0 0 cm\n/Im0 Do\nQ\n"
    objects = {
        3: b"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.2755905511812 841.8897637795276] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
        4: b"4 0 obj\n<< /Type /XObject /Subtype /Image /Width 794 /Height 1123 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length " + str(len(compressed)).encode() + b" >>\nstream\n" + compressed + b"\nendstream\nendobj\n",
        5: b"5 0 obj\n<< /Length " + str(len(draw)).encode() + b" >>\nstream\n" + draw + b"\nendstream\nendobj\n",
        1: b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
        2: b"2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n",
    }
    if change:
        change(objects)
    result = bytearray(b"%PDF-1.7\n")
    offsets = {}
    for number, value in objects.items():
        offsets[number] = len(result)
        result.extend(value)
    xref = len(result)
    result.extend(b"xref\n0 6\n0000000000 65535 f \n")
    for number in range(1, 6):
        result.extend(f"{offsets[number]:010d} 00000 n \n".encode())
    result.extend(b"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + str(xref).encode() + b"\n%%EOF\n")
    return bytes(result)


class ViewerChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not TOOL.is_file():
            raise AssertionError("Missing independent N02 viewer module (intentional RED)")
        spec = importlib.util.spec_from_file_location("n02_viewer", TOOL)
        cls.viewer = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.viewer)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)

    def bundle(self, blank=False):
        fixtures = []
        for index, name in enumerate(NAMES):
            rgb = WHITE if blank else synthetic_rgb((20 + index * 30, 70, 160))
            pdf = synthetic_pdf(rgb)
            (self.directory / (name + ".pdf")).write_bytes(pdf)
            (self.directory / (name + ".rgb")).write_bytes(rgb)
            fixtures.append({"name": name, "pdf_sha256": hashlib.sha256(pdf).hexdigest(),
                             "pdf_bytes": len(pdf), "reference_sha256": hashlib.sha256(rgb).hexdigest(),
                             "reference_bytes": len(rgb)})
        manifest = {"version": 1, "width": WIDTH, "height": HEIGHT, "dpi": 96, "fixtures": fixtures}
        self.save_manifest(manifest)
        return manifest

    def save_manifest(self, manifest):
        (self.directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    def test_exact_writer_grammar_returns_exact_rgb(self):
        rgb = synthetic_rgb()
        self.assertEqual(rgb, self.viewer.parse_pdf(synthetic_pdf(rgb)))

    def test_rejects_noncanonical_objects_and_disconnected_images(self):
        mutations = [
            (3, b"/Im0 4 0 R", b"/Im0 5 0 R"),
            (3, b"/Contents 5 0 R", b"/Contents 4 0 R"),
            (3, b"/Parent 2 0 R", b"/Parent 1 0 R"),
            (3, b"/Resources", b"/Rotate 180 /Resources"),
            (3, b"/MediaBox [0 0", b"/MediaBox [1 0"),
            (4, b"/Width 794", b"/Width 795"),
            (4, b"/Height 1123", b"/Height 1122"),
            (4, b"/DeviceRGB", b"/DeviceBGR"),
            (4, b"/BitsPerComponent 8", b"/BitsPerComponent 16"),
            (4, b"/FlateDecode", b"[/FlateDecode]"),
            (4, b"/Length ", b"/Length 0 /Length "),
            (4, b"\nstream\n", b"\nstream\r\n"),
            (4, b"\nendstream\n", b"endstream\n"),
            (5, b"/Im0 Do", b"/Im1 Do"),
            (5, b" 0 0 cm", b" 2 0 cm"),
            (5, b"/Length 59", b"/Length 60"),
            (1, b"/Pages 2 0 R", b"/Pages 3 0 R"),
            (1, b" >>", b" /OpenAction 4 0 R >>"),
            (2, b"/Count 1", b"/Count 2"),
            (2, b"[3 0 R]", b"[4 0 R]"),
            (2, b"endobj\n", b"endobj\n2 0 obj\n<<>>\nendobj\n"),
        ]
        for number, old, new in mutations:
            with self.subTest(number=number, old=old, new=new):
                def change(objects):
                    self.assertIn(old, objects[number])
                    objects[number] = objects[number].replace(old, new)
                with self.assertRaises(self.viewer.VerificationError):
                    self.viewer.parse_pdf(synthetic_pdf(change=change))

    def test_rejects_prefix_trailing_xref_and_trailer_ambiguity(self):
        pdf = synthetic_pdf()
        malformed = [b" " + pdf, pdf + b"\n", pdf + pdf,
                     pdf.replace(b"0000000009 00000 n", b"0000000008 00000 n"),
                     pdf.replace(b"/Size 6", b"/Size 7"),
                     pdf.replace(b"/Root 1 0 R", b"/Root 2 0 R"),
                     pdf.replace(b"xref\n0 6", b"xref\n0 5"),
                     pdf.replace(b"00000 n", b"00001 n", 1),
                     pdf.replace(b"startxref\n", b"startxref\n0"),
                     pdf.replace(b"%%EOF\n", b"%%EOF"), pdf[:-1]]
        for data in malformed:
            with self.subTest(size=len(data)):
                self.assertNotEqual(pdf, data)
                with self.assertRaises(self.viewer.VerificationError):
                    self.viewer.parse_pdf(data)

    def test_rejects_bad_stream_lengths(self):
        pdf = synthetic_pdf()
        for suffix in (b"", b"0", b"-1", b"16777217", b"999999999999999999999999999"):
            start = pdf.index(b"/Length ") + len(b"/Length ")
            end = pdf.index(b" >>", start)
            with self.subTest(value=suffix), self.assertRaises(self.viewer.VerificationError):
                self.viewer.parse_pdf(pdf[:start] + suffix + pdf[end:])

    def test_rejects_extra_truncated_and_oversized_inflated_streams(self):
        valid = zlib.compress(WHITE)
        for compressed in (valid[:-1], valid + b"garbage", valid + zlib.compress(b""),
                           zlib.compress(WHITE[:-1]), zlib.compress(WHITE + b"x"),
                           zlib.compress(bytes(RAW_BYTES * 4)), b"", b"invalid"):
            with self.subTest(size=len(compressed)), self.assertRaises(self.viewer.VerificationError):
                self.viewer.parse_pdf(synthetic_pdf(compressed=compressed))

    def test_pdf_bound_is_enforced(self):
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.parse_pdf(bytes(16 * 1024 * 1024 + 1))

    def test_pixel_tolerance_is_existing_native_formula(self):
        expected = bytearray(WHITE)
        expected[:300] = bytes(300)  # 100 ink pixels, limits 8 and 800.
        actual = bytearray(expected)
        actual[:3] = bytes([33]) * 3
        metrics = self.viewer.compare_rgb(bytes(actual), bytes(expected))
        self.assertEqual({"ink_pixels": 100, "large_pixels": 1, "difference": 33,
                          "large_limit": 8, "difference_limit": 800, "passed": True}, metrics)
        actual[:24] = bytes([33]) * 24
        self.assertTrue(self.viewer.compare_rgb(bytes(actual), bytes(expected))["passed"])
        actual[:27] = bytes([33]) * 27  # Ninth >32 pixel must fail.
        self.assertFalse(self.viewer.compare_rgb(bytes(actual), bytes(expected))["passed"])
        actual[:27] = bytes([32]) * 27
        self.assertTrue(self.viewer.compare_rgb(bytes(actual), bytes(expected))["passed"])
        actual = bytearray(expected)
        actual[:300] = bytes([8]) * 300
        self.assertTrue(self.viewer.compare_rgb(bytes(actual), bytes(expected))["passed"])
        actual[:300] = bytes([9]) * 300  # Sum alone must fail.
        self.assertFalse(self.viewer.compare_rgb(bytes(actual), bytes(expected))["passed"])

    def test_mismatched_rgb_dimensions_fail(self):
        for actual, expected in ((b"", b""), (WHITE[:-1], WHITE), (WHITE, WHITE + b"x")):
            with self.assertRaises(self.viewer.VerificationError):
                self.viewer.compare_rgb(actual, expected)

    def test_real_pdfium_renders_full_page_orientation_and_rgb(self):
        rgb = synthetic_rgb()
        result = self.viewer.render_pdf96(synthetic_pdf(rgb))
        self.assertEqual(len(result), RAW_BYTES)
        for x, y in ((90, 100), (540, 610), (300, 500)):
            offset = (y * WIDTH + x) * 3
            self.assertEqual(rgb[offset:offset + 3], result[offset:offset + 3])
        self.assertTrue(self.viewer.compare_rgb(result, rgb)["passed"])

    def test_version_is_reported_and_checked(self):
        self.assertEqual({"pypdfium2": "5.3.0", "pdfium": "145.0.7616.0"}, self.viewer.viewer_versions())
        with mock.patch.object(self.viewer.importlib.metadata, "version", return_value="0.0"):
            with self.assertRaises(self.viewer.VerificationError):
                self.viewer.viewer_versions()
        pdfium = self.viewer.importlib.import_module("pypdfium2")
        with mock.patch.object(pdfium, "PDFIUM_INFO", "0.0"):
            with self.assertRaises(self.viewer.VerificationError):
                self.viewer.viewer_versions()

    def test_renderer_rejects_unvalidated_pdf_before_loading_viewer(self):
        with mock.patch.object(self.viewer, "_pdfium", side_effect=AssertionError("unsafe native parser entry")):
            with self.assertRaises(self.viewer.VerificationError):
                self.viewer.render_pdf96(synthetic_pdf() + b"extra")

    def test_manifest_rejects_shape_types_and_path_traversal(self):
        for key, value in (("version", True), ("dpi", 96.0), ("width", 795), ("height", 1122),
                           ("unknown", 1), ("fixtures", [])):
            manifest = self.bundle()
            manifest[key] = value
            self.save_manifest(manifest)
            with self.subTest(key=key), self.assertRaises(self.viewer.VerificationError):
                self.viewer.verify_directory(self.directory)
        for value in ("../basic", "BASIC", "basic.pdf", "large"):
            manifest = self.bundle()
            manifest["fixtures"][0]["name"] = value
            self.save_manifest(manifest)
            with self.subTest(name=value), self.assertRaises(self.viewer.VerificationError):
                self.viewer.verify_directory(self.directory)

    def test_manifest_duplicate_keys_and_hash_size_mismatch_fail(self):
        manifest = self.bundle()
        path = self.directory / "manifest.json"
        path.write_text(json.dumps(manifest).replace('"version": 1', '"version": 1, "version": 1'))
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)
        for key, value in (("pdf_sha256", "0" * 64), ("reference_sha256", "0" * 64),
                           ("pdf_bytes", True), ("pdf_bytes", 16 * 1024 * 1024 + 1),
                           ("reference_bytes", RAW_BYTES - 1), ("pdf_sha256", "G" * 64),
                           ("pdf_bytes", 1), ("extra", "basic.pdf")):
            manifest = self.bundle()
            manifest["fixtures"][0][key] = value
            self.save_manifest(manifest)
            with self.subTest(key=key, value=value), self.assertRaises(self.viewer.VerificationError):
                self.viewer.verify_directory(self.directory)

    def test_rejects_missing_extra_symlink_and_hardlink_files(self):
        self.bundle()
        path = self.directory / "basic.pdf"
        content = path.read_bytes()
        path.unlink()
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)
        other = Path(self.temp.name + "-other.pdf")
        self.addCleanup(lambda: other.unlink(missing_ok=True))
        other.write_bytes(content)
        path.symlink_to(other)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)
        path.unlink()
        os.link(other, path)
        other.unlink()  # Restore a single regular file first.
        alias = Path(self.temp.name + "-alias")
        self.addCleanup(lambda: alias.unlink(missing_ok=True))
        os.link(path, alias)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)
        alias.unlink()
        (self.directory / "unexpected.pdf").write_bytes(content)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)

    def test_manifest_symlink_is_rejected(self):
        self.bundle()
        manifest = self.directory / "manifest.json"
        other = Path(self.temp.name + "-manifest.json")
        self.addCleanup(lambda: other.unlink(missing_ok=True))
        manifest.rename(other)
        manifest.symlink_to(other)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)

    def test_reference_cannot_alias_the_embedded_pdf_file(self):
        self.bundle()
        reference = self.directory / "basic.rgb"
        reference.unlink()
        reference.symlink_to(self.directory / "basic.pdf")
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)

    def test_directory_symlink_and_parent_traversal_fail(self):
        self.bundle()
        alias = Path(self.temp.name + "-alias")
        alias.symlink_to(self.directory, target_is_directory=True)
        self.addCleanup(alias.unlink)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(alias)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory / ".." / self.directory.name)

    def test_windows_reparse_alias_metadata_is_refused(self):
        self.bundle()
        original = Path.lstat
        for target in (self.directory, self.directory / "manifest.json"):
            def reparse_stat(path, *args, **kwargs):
                value = original(path, *args, **kwargs)
                if path != target:
                    return value
                fields = {key: getattr(value, key) for key in dir(value) if key.startswith("st_")}
                fields["st_file_attributes"] = 0x400  # FILE_ATTRIBUTE_REPARSE_POINT
                return SimpleNamespace(**fields)
            with self.subTest(target=target.name), mock.patch.object(Path, "lstat", reparse_stat):
                with self.assertRaises(self.viewer.VerificationError):
                    if target == self.directory:
                        self.viewer._directory(target)
                    else:
                        self.viewer._read_file(target, 8192, set())

    def test_bundle_validates_all_inputs_before_invoking_viewer(self):
        self.bundle()
        (self.directory / "wrap.pdf").write_bytes(b"bad")
        with mock.patch.object(self.viewer, "render_pdf96", side_effect=AssertionError("viewer called before complete preflight")):
            with self.assertRaises(self.viewer.VerificationError):
                self.viewer.verify_directory(self.directory)

    def read_with_stat_snapshots(self, ctimes, changes=(), platform="nt"):
        # Keep the bounded file read real; synthesize only OS metadata snapshots so
        # Windows API semantics and otherwise racy mutations are deterministic.
        path = self.directory / "stat-fixture.bin"
        content = b"public synthetic stat fixture"
        path.write_bytes(content)
        actual = path.lstat()
        fields = {key: getattr(actual, key) for key in dir(actual) if key.startswith("st_")}
        fields.update(st_birthtime_ns=10, st_mtime_ns=30, st_file_attributes=32)
        snapshots = [SimpleNamespace(**{**fields, "st_ctime_ns": value}) for value in ctimes]
        for index, field, value in changes:
            setattr(snapshots[index], field, value)
        with mock.patch.object(self.viewer.os, "name", platform), \
                mock.patch.object(Path, "lstat", side_effect=[snapshots[0], snapshots[3]]), \
                mock.patch.object(self.viewer.os, "fstat", side_effect=[snapshots[1], snapshots[2]]):
            return self.viewer._read_file(path, 128, set()), content

    def test_stable_windows_birth_and_change_times_are_not_confused(self):
        # CPython 3.12 Windows lstat reports creation time as ctime, while
        # fstat can report ChangeTime. Both API sequences are individually stable.
        try:
            actual, expected = self.read_with_stat_snapshots((10, 20, 20, 10))
        except self.viewer.VerificationError as error:
            self.fail("Stable Windows path/handle timestamps were rejected: " + str(error))
        self.assertEqual(expected, actual)

    def test_windows_each_api_rejects_ctime_only_mutation(self):
        for index, value in ((3, 11), (2, 21)):
            with self.subTest(snapshot=index), self.assertRaises(self.viewer.VerificationError):
                self.read_with_stat_snapshots((10, 20, 20, 10), [(index, "st_ctime_ns", value)])

    def test_windows_birthtime_is_bound_across_every_snapshot(self):
        for index in range(4):
            with self.subTest(snapshot=index), self.assertRaises(self.viewer.VerificationError):
                self.read_with_stat_snapshots((10, 10, 10, 10), [(index, "st_birthtime_ns", 11)])

    def test_windows_identity_and_metadata_changes_remain_rejected(self):
        path = self.directory / "metadata-control.bin"
        path.write_bytes(b"control")
        info = path.lstat()
        mutations = (("st_dev", info.st_dev + 1), ("st_ino", 0),
                     ("st_mode", info.st_mode ^ 0o100), ("st_nlink", 2),
                     ("st_size", 1), ("st_mtime_ns", 31),
                     ("st_file_attributes", 32 | 0x400))
        for index in range(4):
            for field, value in mutations:
                with self.subTest(snapshot=index, field=field), self.assertRaises(self.viewer.VerificationError):
                    self.read_with_stat_snapshots((10, 10, 10, 10), [(index, field, value)])

    def test_posix_retains_cross_api_ctime_binding(self):
        actual, expected = self.read_with_stat_snapshots((20, 20, 20, 20), platform="posix")
        self.assertEqual(expected, actual)
        with self.assertRaises(self.viewer.VerificationError):
            self.read_with_stat_snapshots((10, 20, 20, 10), platform="posix")

    def test_blank_references_cannot_report_success(self):
        self.bundle(blank=True)
        with self.assertRaises(self.viewer.VerificationError):
            self.viewer.verify_directory(self.directory)

    def test_independent_reference_mismatch_fails_even_with_valid_hash(self):
        manifest = self.bundle()
        different = synthetic_rgb((220, 230, 240))
        (self.directory / "basic.rgb").write_bytes(different)
        manifest["fixtures"][0]["reference_sha256"] = hashlib.sha256(different).hexdigest()
        self.save_manifest(manifest)
        result = self.viewer.verify_directory(self.directory)
        self.assertFalse(result["passed"])
        self.assertFalse(result["fixtures"][0]["image_vs_reference"]["passed"])

    def test_viewer_mismatch_cannot_be_masked_by_extraction_success(self):
        self.bundle()
        with mock.patch.object(self.viewer, "render_pdf96", return_value=WHITE):
            result = self.viewer.verify_directory(self.directory)
        self.assertFalse(result["passed"])
        self.assertFalse(result["fixtures"][0]["viewer_vs_reference"]["passed"])
        self.assertFalse(result["fixtures"][0]["viewer_vs_image"]["passed"])

    def test_unavailable_viewer_fails_without_skip(self):
        self.bundle()
        with mock.patch.object(self.viewer.importlib, "import_module", side_effect=ImportError("absent")):
            with self.assertRaises(self.viewer.VerificationError):
                self.viewer.verify_directory(self.directory)

    def test_cli_success_is_json_and_explicitly_limited(self):
        self.bundle()
        result = subprocess.run([sys.executable, str(TOOL), str(self.directory)], capture_output=True, text=True, timeout=60)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        report = json.loads(result.stdout)
        self.assertTrue(report["passed"])
        self.assertFalse(report["native_wpf_verified"])
        self.assertFalse(report["product_complete"])
        self.assertEqual(list(NAMES), [f["name"] for f in report["fixtures"]])
        self.assertEqual("", result.stderr)

    def test_cli_failure_is_json_nonzero(self):
        self.bundle()
        (self.directory / "basic.pdf").write_bytes(b"not PDF")
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            code = self.viewer.main([str(self.directory)])
        self.assertEqual(1, code)
        self.assertFalse(json.loads(output.getvalue())["passed"])


if __name__ == "__main__":
    unittest.main()
