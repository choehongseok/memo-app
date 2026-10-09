"""Public synthetic CI probe only; no runtime installer/download or artifact upload."""
import hashlib, json, os, pathlib, re, shutil, subprocess, sys, urllib.request

TESSERACT = 'db0ec62f81b0737fbbe184d8fea40af5738f8eef'
LEPTONICA = '13275a278eb55b5746e33f95fbf5a2c8f604b3ab'
MODELS = '87416418657359cb625c412a48b6e1d6d41c29bd'
PINS = {
 'kor': (1677415, '6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2'),
 'eng': (4113088, '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2'),
}

def run(*args):
 subprocess.run(args, check=True)

def clone(root, name, version, commit):
 dest = root / name
 run('git', 'clone', '--depth', '1', '--branch', version,
     'https://github.com/tesseract-ocr/' + name + '.git' if name == 'tesseract' else 'https://github.com/DanBloomberg/leptonica.git', str(dest))
 actual = subprocess.check_output(['git', '-C', str(dest), 'rev-parse', 'HEAD'], text=True).strip()
 if actual != commit:
  raise ValueError('Pinned upstream source commit mismatch')
 return dest

def main(root):
 if sys.platform != 'win32':
  raise RuntimeError('Existing MSVC Windows CI only; no compiler/SDK installation')
 root.mkdir(parents=True, exist_ok=False)
 prefix = root / 'leptonica-install'
 leptonica = clone(root, 'leptonica', '1.87.0', LEPTONICA)
 common = ['-G', 'Visual Studio 17 2022', '-A', 'x64', '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW', '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded']
 flags = ['-DBUILD_PROG=OFF', '-DSW_BUILD=OFF', '-DBUILD_SHARED_LIBS=OFF'] + ['-DENABLE_' + option + '=OFF' for option in ['ZLIB', 'PNG', 'GIF', 'JPEG', 'TIFF', 'WEBP', 'OPENJPEG']]
 run('cmake', '-S', str(leptonica), '-B', str(root / 'leptonica-build'), *common, '-DCMAKE_INSTALL_PREFIX=' + str(prefix), *flags)
 run('cmake', '--build', str(root / 'leptonica-build'), '--config', 'Release', '--target', 'install', '--parallel', '2')
 tesseract = clone(root, 'tesseract', '5.5.3', TESSERACT)
 flags = ['-DBUILD_TRAINING_TOOLS=OFF', '-DBUILD_TESTS=OFF', '-DBUILD_SHARED_LIBS=OFF', '-DDISABLED_LEGACY_ENGINE=ON', '-DGRAPHICS_DISABLED=ON', '-DDISABLE_CURL=ON', '-DDISABLE_ARCHIVE=ON', '-DDISABLE_TIFF=ON', '-DINSTALL_CONFIGS=OFF', '-DOPENMP_BUILD=OFF', '-DENABLE_NATIVE=OFF', '-DFAST_FLOAT=ON', '-DENABLE_LTO=OFF', '-DENABLE_CCACHE=OFF', '-DWIN32_MT_BUILD=ON']
 run('cmake', '-S', str(tesseract), '-B', str(root / 'tesseract-build'), *common, '-DCMAKE_PREFIX_PATH=' + str(prefix), *flags)
 cache = (root / 'tesseract-build' / 'CMakeCache.txt').read_text(encoding='utf-8')
 selected = re.search(r'^Leptonica_DIR:PATH=(.+)$', cache, re.M)
 if not selected or not pathlib.Path(selected.group(1)).resolve().is_relative_to(prefix.resolve()):
  raise ValueError('CMake selected an unexpected Leptonica package')
 print('Actual selected Leptonica package: ' + selected.group(1))
 run('cmake', '--build', str(root / 'tesseract-build'), '--config', 'Release', '--target', 'tesseract', '--parallel', '2')
 bundle = root / 'probe'
 (bundle / 'tessdata').mkdir(parents=True)
 executable = root / 'tesseract-build' / 'bin' / 'Release' / 'tesseract.exe'
 if not executable.is_file():
  candidates = list((root / 'tesseract-build').rglob('tesseract.exe'))
  if len(candidates) != 1:
   raise ValueError('Ambiguous native executable')
  executable = candidates[0]
 vswhere = pathlib.Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
 dumps = subprocess.check_output([str(vswhere), '-latest', '-products', '*', '-find', r'VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe'], text=True).splitlines()
 if len(dumps) != 1:
  raise ValueError('Existing dumpbin path is ambiguous')
 imports = subprocess.check_output([dumps[0], '/dependents', str(executable)], text=True)
 print(imports)
 dependencies = sorted(set(re.findall(r'^\s+([A-Za-z0-9_.-]+\.dll)\s*$', imports, re.M | re.I)))
 allowed = {'kernel32.dll', 'user32.dll', 'advapi32.dll', 'shell32.dll', 'ole32.dll', 'oleaut32.dll', 'shlwapi.dll', 'crypt32.dll', 'bcrypt.dll', 'ntdll.dll', 'gdi32.dll'}
 if not dependencies or any(name.lower() not in allowed for name in dependencies):
  raise ValueError('Unexpected non-system/CRT/network DLL dependency in native probe')
 shutil.copyfile(executable, bundle / 'tesseract.exe')
 for language, (length, sha) in PINS.items():
  url = f'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/{MODELS}/{language}.traineddata'
  with urllib.request.urlopen(url, timeout=60) as response:
   content = response.read(length + 1)
  if len(content) != length or hashlib.sha256(content).hexdigest() != sha:
   raise ValueError('Pinned public model mismatch')
  (bundle / 'tessdata' / (language + '.traineddata')).write_bytes(content)
 for source, filename, sha in [(tesseract / 'LICENSE', 'Tesseract-Apache2.txt', 'cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30'), (leptonica / 'leptonica-license.txt', 'Leptonica.txt', '87829abb5bbb00b55a107365da89e9a33f86c4250169e5a1e5588505be7d5806')]:
  content = source.read_bytes().replace(b'\r\n', b'\n')
  if hashlib.sha256(content).hexdigest() != sha:
   raise ValueError('Component license mismatch')
  (bundle / filename).write_bytes(content)
 content = (bundle / 'tesseract.exe').read_bytes()
 manifest = {'tesseractCommit': TESSERACT, 'leptonicaCommit': LEPTONICA, 'modelsCommit': MODELS, 'engineLength': len(content), 'engineSha256': hashlib.sha256(content).hexdigest(), 'peDependencies': dependencies}
 (bundle / 'native-build.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
 run(str(bundle / 'tesseract.exe'), '--version')
 print(json.dumps(manifest))
 print('Native engine proof bundle only; not installed into product or uploaded')

if __name__ == '__main__':
 if len(sys.argv) != 2:
  raise SystemExit('usage: build_native_ocr.py NEW_RUNNER_TEMP_DIRECTORY')
 main(pathlib.Path(sys.argv[1]).resolve())
