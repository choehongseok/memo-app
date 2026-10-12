#!/usr/bin/env python3
"""Independent viewer gate for the three public synthetic N02 fixture pairs.

Usage: python3 tools/verify_n02_pdf_viewer.py FIXTURE_DIRECTORY

This is test tooling, not a general PDF parser or a product dependency. Only the
exact single-page PdfRasterDocumentBuilder output grammar is admitted. Reference
RGB must come from the separately constructed manual WPF oracle. Its provenance
must be established by the native fixture producer/review, not by these hashes.
No network, extraction fallback, registration, or viewer-skip path is provided.
"""
import argparse
import hashlib
import importlib
import importlib.metadata
import json
import os
from pathlib import Path
import re
import stat
import sys
import zlib

WIDTH, HEIGHT, DPI = 794, 1123, 96
RAW_BYTES = WIDTH * HEIGHT * 3
MAX_PDF_BYTES = 16 * 1024 * 1024
MAX_MANIFEST_BYTES = 8192
NAMES = ("basic", "large", "wrap")
PYPDFIUM_VERSION = "5.3.0"
PDFIUM_VERSION = "145.0.7616.0"
SCOPE = "Fixed public synthetic PDF viewer checks only; native WPF execution and product completeness are not established."


class VerificationError(ValueError):
    """A bounded fixture, parser, or independent viewer requirement failed."""


def require(condition, message):
    if not condition:
        raise VerificationError(message)


def parse_pdf(pdf):
    """Return RGB only after exact grammar, all connections, and zlib validation."""
    require(isinstance(pdf, bytes) and 0 < len(pdf) <= MAX_PDF_BYTES, "PDF size/type limit")
    position = 0
    offsets = {}

    def consume(expected):
        nonlocal position
        require(pdf.startswith(expected, position), "PDF fixed grammar/framing mismatch")
        position += len(expected)

    consume(b"%PDF-1.7\n")
    offsets[3] = position
    consume(b"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.2755905511812 841.8897637795276] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n")
    offsets[4] = position
    consume(b"4 0 obj\n<< /Type /XObject /Subtype /Image /Width 794 /Height 1123 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length ")
    length_match = re.match(rb"[1-9][0-9]{0,7} >>\nstream\n", pdf[position:position + 30])
    require(length_match is not None, "PDF compressed stream length syntax")
    compressed_length = int(length_match.group().split(b" ", 1)[0])
    require(compressed_length <= RAW_BYTES + 65536, "PDF compressed stream length limit")
    position += length_match.end()
    require(compressed_length <= len(pdf) - position, "PDF truncated compressed stream")
    compressed = pdf[position:position + compressed_length]
    position += compressed_length
    consume(b"\nendstream\nendobj\n")
    offsets[5] = position
    draw = b"q\n595.2755905511812 0 0 841.8897637795276 0 0 cm\n/Im0 Do\nQ\n"
    consume(b"5 0 obj\n<< /Length " + str(len(draw)).encode("ascii") + b" >>\nstream\n" + draw + b"\nendstream\nendobj\n")
    offsets[1] = position
    consume(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
    offsets[2] = position
    consume(b"2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n")
    xref_position = position
    consume(b"xref\n0 6\n0000000000 65535 f \n")
    for number in range(1, 6):
        consume(f"{offsets[number]:010d} 00000 n \n".encode("ascii"))
    consume(b"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + str(xref_position).encode("ascii") + b"\n%%EOF\n")
    require(position == len(pdf), "PDF trailing bytes or incremental update")
    try:
        decoder = zlib.decompressobj()
        rgb = decoder.decompress(compressed, RAW_BYTES + 1)
    except zlib.error as error:
        raise VerificationError("PDF invalid zlib stream") from error
    require(len(rgb) == RAW_BYTES, "PDF inflated RGB size mismatch")
    require(decoder.eof and not decoder.unused_data and not decoder.unconsumed_tail,
            "PDF zlib must contain exactly one fully consumed stream")
    return rgb


def compare_rgb(actual, expected):
    """Exact VisualPdfReferenceMatches tolerance from PdfVisualChecks.cs.

    No translation, scaling, threshold negotiation, or per-channel averaging.
    Ink is counted from the expected image, as in the existing native check.
    """
    require(len(actual) == RAW_BYTES and len(expected) == RAW_BYTES, "RGB dimensions/length mismatch")
    difference = large = ink = 0
    for i in range(0, RAW_BYTES, 3):
        if expected[i] < 250 or expected[i + 1] < 250 or expected[i + 2] < 250:
            ink += 1
        delta = max(abs(actual[i] - expected[i]), abs(actual[i + 1] - expected[i + 1]),
                    abs(actual[i + 2] - expected[i + 2]))
        difference += delta
        if delta > 32:
            large += 1
    large_limit, difference_limit = max(8, ink // 20), max(64, ink * 8)
    return {"ink_pixels": ink, "large_pixels": large, "difference": difference,
            "large_limit": large_limit, "difference_limit": difference_limit,
            "passed": large <= large_limit and difference <= difference_limit}


def _pdfium():
    try:
        pdfium = importlib.import_module("pypdfium2")
        distribution_version = importlib.metadata.version("pypdfium2")
    except (ImportError, OSError, importlib.metadata.PackageNotFoundError) as error:
        raise VerificationError("Required independent pypdfium2 viewer unavailable; no skip/fallback") from error
    require(distribution_version == PYPDFIUM_VERSION and str(pdfium.PYPDFIUM_INFO) == PYPDFIUM_VERSION,
            "Independent pypdfium2 version mismatch (requires 5.3.0)")
    require(str(pdfium.PDFIUM_INFO) == PDFIUM_VERSION,
            "Independent PDFium version mismatch (requires 145.0.7616.0)")
    return pdfium


def viewer_versions():
    pdfium = _pdfium()
    return {"pypdfium2": str(pdfium.PYPDFIUM_INFO), "pdfium": str(pdfium.PDFIUM_INFO)}


def render_pdf96(pdf):
    """Actually render the validated full PDF through PDFium at 96/72 scale."""
    parse_pdf(pdf)  # This public entry point cannot hand unvalidated PDFs to native code.
    pdfium = _pdfium()
    try:
        with pdfium.PdfDocument(pdf) as document:
            require(len(document) == 1, "Viewer page count mismatch")
            page = document[0]
            try:
                require(abs(page.get_width() - 595.2755905511812) < 0.001 and
                        abs(page.get_height() - 841.8897637795276) < 0.001,
                        "Viewer page dimensions mismatch")
                bitmap = page.render(scale=DPI / 72, rotation=0, crop=(0, 0, 0, 0),
                                     rev_byteorder=True, fill_color=(255, 255, 255, 255),
                                     may_draw_forms=False, draw_annots=False)
                try:
                    require((bitmap.width, bitmap.height, bitmap.mode) == (WIDTH, HEIGHT, "RGB"),
                            "Viewer RGB format/dimensions mismatch")
                    require(WIDTH * 3 <= bitmap.stride <= WIDTH * 3 + 3,
                            "Viewer RGB stride mismatch")
                    pixels = memoryview(bitmap.buffer).cast("B")
                    rgb = b"".join(pixels[y * bitmap.stride:y * bitmap.stride + WIDTH * 3]
                                   for y in range(HEIGHT))
                    del pixels
                    require(len(rgb) == RAW_BYTES, "Viewer RGB byte count mismatch")
                    return rgb
                finally:
                    bitmap.close()
            finally:
                page.close()
    except VerificationError:
        raise
    except Exception as error:
        raise VerificationError("Independent PDFium rendering failed") from error


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Manifest duplicate key")
        result[key] = value
    return result


def _manifest(data):
    try:
        value = json.loads(data.decode("utf-8"), object_pairs_hook=_unique_object)
    except (UnicodeError, ValueError, RecursionError) as error:
        raise VerificationError("Invalid or ambiguous manifest JSON") from error
    require(type(value) is dict and set(value) == {"version", "width", "height", "dpi", "fixtures"},
            "Manifest fields mismatch")
    for key, expected in (("version", 1), ("width", WIDTH), ("height", HEIGHT), ("dpi", DPI)):
        require(type(value[key]) is int and value[key] == expected, "Manifest fixed dimensions/version mismatch")
    require(type(value["fixtures"]) is list and len(value["fixtures"]) == 3, "Manifest needs exactly three fixtures")
    for fixture, name in zip(value["fixtures"], NAMES):
        require(type(fixture) is dict and set(fixture) == {
            "name", "pdf_sha256", "pdf_bytes", "reference_sha256", "reference_bytes"}, "Manifest fixture fields mismatch")
        require(fixture["name"] == name, "Manifest fixed fixture name/order mismatch")
        require(type(fixture["pdf_bytes"]) is int and 0 < fixture["pdf_bytes"] <= MAX_PDF_BYTES,
                "Manifest PDF size limit")
        require(type(fixture["reference_bytes"]) is int and fixture["reference_bytes"] == RAW_BYTES,
                "Manifest reference size mismatch")
        for field in ("pdf_sha256", "reference_sha256"):
            require(type(fixture[field]) is str and re.fullmatch("[0-9a-f]{64}", fixture[field]) is not None,
                    "Manifest SHA256 syntax mismatch")
    return value


def _directory(path):
    path = Path(path)
    require(".." not in path.parts, "Fixture directory parent traversal refused")
    path = path.absolute()
    for component in (*reversed(path.parents), path):
        info = component.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 0x400,
                "Fixture directory symlink/reparse alias refused")
    require(path.is_dir(), "Fixture directory missing")
    require({p.name for p in path.iterdir()} == {"manifest.json", *(n + suffix for n in NAMES for suffix in (".pdf", ".rgb"))},
            "Fixture directory must contain only the seven fixed files")
    return path


def _read_file(path, bound, identities):
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1 and
            not getattr(before, "st_file_attributes", 0) & 0x400,
            "Fixture must be a regular file without symlink/reparse/hardlink aliases")
    require(0 < before.st_size <= bound, "Fixture file size limit")
    identity = (before.st_dev, before.st_ino)
    require(identity not in identities, "Fixture file identity alias refused")
    identities.add(identity)
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_BINARY", 0)
    fd = os.open(path, flags)
    with os.fdopen(fd, "rb") as stream:
        opened = os.fstat(stream.fileno())
        require((opened.st_dev, opened.st_ino) == identity and stat.S_ISREG(opened.st_mode) and opened.st_nlink == 1,
                "Fixture file changed while opening")
        data = stream.read(bound + 1)
        after = os.fstat(stream.fileno())
    final = path.lstat()
    if os.name == "nt":
        require(all(isinstance(getattr(info, "st_birthtime_ns", None), int)
                    for info in (before, opened, after, final)), "Windows fixture birth time unavailable")
    def signature(info):
        # CPython 3.12 on Windows can expose creation time through lstat ctime
        # and ChangeTime through fstat ctime. Bind birth time across APIs and
        # retain both APIs' own ctime stability checks below.
        timestamp = info.st_birthtime_ns if os.name == "nt" else info.st_ctime_ns
        return (info.st_dev, info.st_ino, info.st_mode, info.st_nlink, info.st_size, info.st_mtime_ns,
                timestamp, getattr(info, "st_file_attributes", 0))
    require(signature(before) == signature(opened) == signature(after) == signature(final)
            and before.st_ctime_ns == final.st_ctime_ns and opened.st_ctime_ns == after.st_ctime_ns,
            "Fixture changed while reading")
    require(len(data) == before.st_size and len(data) <= bound, "Fixture bounded read mismatch")
    return data


def verify_directory(directory):
    """Preflight the entire closed fixture set, then run the independent viewer."""
    try:
        directory = _directory(directory)
        identities = set()
        manifest = _manifest(_read_file(directory / "manifest.json", MAX_MANIFEST_BYTES, identities))
        inputs = []
        for fixture in manifest["fixtures"]:
            name = fixture["name"]
            pdf = _read_file(directory / (name + ".pdf"), MAX_PDF_BYTES, identities)
            reference = _read_file(directory / (name + ".rgb"), RAW_BYTES, identities)
            for data, prefix in ((pdf, "pdf"), (reference, "reference")):
                require(len(data) == fixture[prefix + "_bytes"] and
                        hashlib.sha256(data).hexdigest() == fixture[prefix + "_sha256"],
                        name + ": fixture byte count/SHA256 mismatch")
            image = parse_pdf(pdf)
            require(any(channel < 250 for channel in reference), name + ": blank independent reference refused")
            inputs.append((fixture, pdf, image, reference))
        versions = viewer_versions()
        results = []
        for fixture, pdf, image, reference in inputs:
            rendered = render_pdf96(pdf)
            comparisons = {"image_vs_reference": compare_rgb(image, reference),
                           "viewer_vs_image": compare_rgb(rendered, image),
                           "viewer_vs_reference": compare_rgb(rendered, reference)}
            results.append({**fixture, **comparisons, "passed": all(m["passed"] for m in comparisons.values())})
        return {"passed": all(fixture["passed"] for fixture in results), "scope": SCOPE,
                "native_wpf_verified": False, "product_complete": False,
                "width": WIDTH, "height": HEIGHT, "dpi": DPI,
                "viewer": versions, "fixtures": results}
    except OSError as error:
        raise VerificationError("Fixture filesystem access/read failed") from error


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fixture_directory")
    args = parser.parse_args(argv)
    try:
        result = verify_directory(args.fixture_directory)
    except VerificationError as error:
        result = {"passed": False, "error": str(error), "scope": SCOPE,
                  "native_wpf_verified": False, "product_complete": False}
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
