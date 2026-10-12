# N02 — 승인 후 별도 Windows PC 검증 절차

이 문서는 **향후 실행 안내**다. PC 사용 허가는 아직 대기 중이며, 문서 작성·Linux 교차빌드·소스 검토는 Windows/native GREEN의 근거가 아니다. 실행 순서의 기준은 [preparation.yml](../.github/workflows/preparation.yml)이고, 이전 결과와 미완료 경계는 [복구 기록](N02_RECOVERY_20261012.md)에 있다.

## 실행 전 조건

- 사용 승인을 받은 **별도 폐기 가능한 Windows x64 테스트 계정과 대화형 데스크톱**을 사용한다. 실제 사용자 vault·메모·백업·키·기존 앱 설치가 없는 환경이어야 한다. WPF/클립보드/트레이/시작 등록 검사가 있으므로 일상 작업 중인 계정이나 비대화형 세션은 사용하지 않는다.
- PowerShell **7.4 이상 x64**, Git, Python **3.12 x64**, 기존 **Visual Studio 2022 MSVC x64 C++ 도구·CMake·Windows SDK**가 필요하다. 아래 스크립트는 Python `-c` 문자열의 따옴표를 보존하도록 native 인자 전달을 `Standard`로 고정한다. 네이티브 OCR 빌드와 전체 앱 패키지의 필수 조건이다. 도구가 없으면 중단하고 준비 상태를 기록한다. 이 절차는 컴파일러나 Windows SDK 설치를 자동화하지 않는다.
- **공식 SHA512 검증을 마친 .NET SDK 10.0.401 win-x64**를 별도 도구 디렉터리에 준비한다. `global.json`은 roll-forward를 금지한다. `Microsoft.NETCore.App`과 `Microsoft.WindowsDesktop.App` **10.0.12**가 필요하며, 아래에서 `DOTNET_ROOT`와 실행 파일을 같은 SDK로 고정한다. SDK 확보 방법은 기존 workflow의 `Install fixed official SDK with SHA512 validation` 단계를 따른다. 임의 설치 스크립트·최신 SDK 대체는 하지 않는다. 공식 [10.0 출시 메타데이터](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)의 정확한 win-x64 ZIP URL/버전/SHA512와 검증한 ZIP을 보존한다.
- 승인 범위에는 새 복제본, 공식 PyPI/NuGet 및 기존 OCR 빌더가 사용하는 고정 공개 upstream/model 다운로드, 합성 검사, 임시 패키지 설치·자식 EXE 실행이 포함되어야 한다. 접근 거부·도구 누락이면 중단한다. Actions 거부를 우회하거나 다른 자격 증명을 시도하지 않는다.
- 최종 보고서의 **최종 40자리 HEAD**를 아래 `$ExpectedHead`에 넣는다. 이 문서에는 아직 결정되지 않은 최종 SHA를 넣지 않는다. 승인된 원본 저장소는 그 커밋을 포함해야 한다. `main`/`development`/PR에 push하거나 PR·workflow·업로드 조건을 변경하는 명령은 없다.

## 한 번의 전체 실행

아래를 새 PowerShell 세션의 스크립트로 저장하고, 상단의 여섯 입력값을 채운 뒤 **PC 사용 승인 후에만** 실행한다. 저장소 URL에는 토큰을 넣지 않는다. 기존 작업 폴더를 재사용하지 않으며, 매 실행마다 새 `$TestRoot`와 clone을 만든다. 모든 외부 명령은 종료 코드를 확인하고 0이 아니면 즉시 중단한다. 실패한 폴더·로그를 지우거나 기존 PDF를 재사용하지 않는다.

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false # 아래 함수가 종료 코드를 검사

$ExpectedHead = '<최종 보고서의 40자리 HEAD>'
$ApprovedRepository = '<그 커밋을 포함하는 승인된 저장소 경로 또는 URL>'
$SdkRoot = '<공식 SHA512 검증 SDK 10.0.401 디렉터리>'
$SdkArchive = '<검증에 사용한 공식 win-x64 SDK ZIP 경로>'
$ExpectedSdkSha512 = '<공식 메타데이터의 해당 ZIP SHA512 128자리>'
$Python312 = '<Python 3.12 x64 python.exe 절대 경로>'

if (!$IsWindows -or ![Environment]::Is64BitProcess -or
    $PSVersionTable.PSVersion -lt [version]'7.4' -or ![Environment]::UserInteractive) {
    throw 'Windows x64 PowerShell 7.4 이상 대화형 데스크톱이 필요합니다.'
}
$PSNativeCommandArgumentPassing = 'Standard'
if ($ExpectedHead -cnotmatch '^[0-9a-f]{40}$' -or
    $ExpectedSdkSha512 -notmatch '^[0-9a-fA-F]{128}$' -or
    $ApprovedRepository.Contains('<')) { throw '최종 HEAD/공식 해시/승인된 원본을 입력하세요.' }
$Dotnet = Join-Path $SdkRoot 'dotnet.exe'
foreach ($file in @($Dotnet, $SdkArchive, $Python312)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "필수 파일 없음: $file" }
}
if ((Get-FileHash -LiteralPath $SdkArchive -Algorithm SHA512).Hash -ine $ExpectedSdkSha512) {
    throw '공식 SDK ZIP SHA512 불일치'
}
$Git = (Get-Command git.exe -CommandType Application).Source
$Cmake = (Get-Command cmake.exe -CommandType Application).Source
$TestRoot = Join-Path $env:TEMP ('memo-n02-pc-' + [guid]::NewGuid().ToString('N'))
$LogRoot = Join-Path $TestRoot 'logs'
$Checkout = Join-Path $TestRoot 'checkout'
$OcrRoot = Join-Path $TestRoot 'native-ocr'
$Viewer = Join-Path $TestRoot 'viewer-full'
$Package = Join-Path $TestRoot 'windows-x64-synthetic-trial'
New-Item -ItemType Directory -Path $TestRoot | Out-Null
New-Item -ItemType Directory -Path $LogRoot, (Join-Path $TestRoot 'temp') | Out-Null
$env:TEMP = Join-Path $TestRoot 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_ROOT = $SdkRoot
$env:DOTNET_CLI_HOME = Join-Path $TestRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $TestRoot 'nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:PATH = "$SdkRoot;$env:PATH"
$Run = [ordered]@{
    expectedHead = $ExpectedHead; actualHead = $null; startedUtc = [DateTime]::UtcNow.ToString('o')
    os = [Environment]::OSVersion.VersionString; powershell = $PSVersionTable.PSVersion.ToString()
    sdkRoot = $SdkRoot; sdkArchive = $SdkArchive; sdkArchiveSha512 = $ExpectedSdkSha512.ToLowerInvariant()
    sdkMetadata = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    fullWpfPassed = $false; viewerPassed = $false; installedPackagePassed = $false
    complete = $false; failure = $null
}
function Invoke-Checked {
    param([string]$Name, [string]$Executable, [string[]]$ArgumentList)
    $log = Join-Path $LogRoot "$Name.log"
    if (Test-Path -LiteralPath $log) { throw "로그 덮어쓰기 거절: $Name" }
    Write-Host "RUN $Name"
    & $Executable @ArgumentList *> $log
    $code = $LASTEXITCODE
    [ordered]@{ name=$Name; executable=$Executable; arguments=$ArgumentList; exitCode=$code } |
        ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $LogRoot 'commands.jsonl')
    Get-Content -LiteralPath $log | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { throw "$Name 실패: exit=$code, log=$log" }
}
function Assert-Checkout {
    param([string]$Stage)
    Invoke-Checked "head-$Stage" $Git @('rev-parse', 'HEAD')
    $actual = (Get-Content -LiteralPath (Join-Path $LogRoot "head-$Stage.log") -Raw).Trim()
    if ($actual -cne $ExpectedHead) { throw "HEAD 불일치: $actual" }
    $Run.actualHead = $actual
    Invoke-Checked "status-$Stage" $Git @('status', '--porcelain=v1', '--untracked-files=all')
    if ((Get-Item -LiteralPath (Join-Path $LogRoot "status-$Stage.log")).Length -ne 0) {
        throw 'checkout이 깨끗하지 않습니다.'
    }
    Invoke-Checked "diff-$Stage" $Git @('diff', '--check')
}
$PreviousLocation = Get-Location
try {
    Invoke-Checked 'git-version' $Git @('--version')
    Invoke-Checked 'cmake-version' $Cmake @('--version')
    Invoke-Checked 'clone' $Git @('clone', '--no-checkout', '--no-hardlinks', $ApprovedRepository, $Checkout)
    Set-Location -LiteralPath $Checkout
    Invoke-Checked 'checkout' $Git @('checkout', '--detach', $ExpectedHead)
    Assert-Checkout 'before'
    Invoke-Checked 'dotnet-info' $Dotnet @('--info')
    Invoke-Checked 'dotnet-version' $Dotnet @('--version')
    if ((Get-Content (Join-Path $LogRoot 'dotnet-version.log') -Raw).Trim() -cne '10.0.401') {
        throw 'SDK 버전 불일치'
    }
    Invoke-Checked 'dotnet-runtimes' $Dotnet @('--list-runtimes')
    $runtimes = Get-Content (Join-Path $LogRoot 'dotnet-runtimes.log') -Raw
    foreach ($runtime in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        if ($runtimes -notmatch ('(?m)^' + [regex]::Escape($runtime) + ' 10\.0\.12 \[')) {
            throw "필수 runtime 없음: $runtime 10.0.12"
        }
    }
    Invoke-Checked 'python-version' $Python312 @('-c',
        'import sys,struct; print(sys.version); print(sys.executable); assert sys.version_info[:2]==(3,12) and struct.calcsize("P")==8')
    Invoke-Checked 'python-venv' $Python312 @('-m', 'venv', (Join-Path $TestRoot 'venv'))
    $Python = Join-Path $TestRoot 'venv\Scripts\python.exe'
    Invoke-Checked 'viewer-tooling' $Python @('-m', 'pip', 'install', '--index-url', 'https://pypi.org/simple',
        '--only-binary=:all:', '--require-hashes', '-r', 'tools/requirements-n02-pdf-viewer.txt')
    Invoke-Checked 'python-packages' $Python @('-m', 'pip', 'freeze', '--all')
    Invoke-Checked 'source-bytes' $Python @('-c',
        'import hashlib,pathlib,subprocess; p="docs/SOURCE_PROMPT.txt"; b=subprocess.check_output(["git","show","HEAD:"+p]); c=pathlib.Path(p).read_bytes(); print(len(b),hashlib.sha256(b).hexdigest()); assert b==c')
    Invoke-Checked 'preparation' $Python @('tools/verify_preparation.py')
    Invoke-Checked 'python-tests' $Python @('-m', 'unittest', 'discover', '-s', 'tests', '-p', 'test_*.py', '-v')
    Invoke-Checked 'commit-safety' $Python @('tools/check_commit_safety.py')

    # 빌더가 고정 upstream commit/model 크기·SHA256/라이선스/PE 의존성을 검사한다.
    Invoke-Checked 'native-ocr' $Python @('tools/build_native_ocr.py', $OcrRoot)
    $env:MEMO_OCR_BUNDLE = Join-Path $OcrRoot 'probe'
    $NativeProperty = "-p:NativeOcrBundle=$env:MEMO_OCR_BUNDLE"
    Invoke-Checked 'solution-build' $Dotnet @('build', 'MemoApp.slnx', '-c', 'Release', $NativeProperty)
    Invoke-Checked 'core-full' $Dotnet @('run', '--project', 'tests/MemoApp.ContractChecks', '-c', 'Release', '--no-build')
    Invoke-Checked 'ocr-cleanup-fault' $Dotnet @('run', '--project', 'tests/MemoApp.ContractChecks', '-c', 'Release',
        '--no-build', '--', '--ocr-cleanup-fault-only')

    if (Test-Path -LiteralPath $Viewer) { throw '새 viewer 출력 경로가 필요합니다.' }
    $env:MEMO_N02_VIEWER_OUTPUT = $Viewer
    Invoke-Checked 'wpf-full' $Dotnet @('run', '--project', 'tests/MemoApp.WindowsChecks', '-c', 'Release', '--no-build')
    $Run.fullWpfPassed = $true
    Invoke-Checked 'viewer' $Python @('tools/verify_n02_pdf_viewer.py', $Viewer)
    $Run.viewerPassed = $true

    Assert-Checkout 'pre-publish'
    if (Test-Path -LiteralPath $Package) { throw '새 publish 경로가 필요합니다.' }
    Invoke-Checked 'publish' $Dotnet @('publish', 'src/MemoApp.Windows', $NativeProperty, '-c', 'Release',
        '-r', 'win-x64', '--self-contained', 'true', '-o', $Package)
    Copy-Item -LiteralPath (Join-Path $SdkRoot 'LICENSE.txt') -Destination (Join-Path $Package 'licenses\DotNet10.txt')
    Copy-Item -LiteralPath (Join-Path $SdkRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $Package 'licenses\DotNet10-ThirdPartyNotices.txt')
    Copy-Item -LiteralPath (Join-Path $SdkRoot 'sdk\10.0.401\Sdks\Microsoft.NET.Sdk.WindowsDesktop\LICENSE.TXT') -Destination (Join-Path $Package 'licenses\WindowsDesktop10.txt')
    Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $Package 'README.txt')
    Copy-Item -LiteralPath 'docs/USER-TESTS.md' -Destination (Join-Path $Package 'USER-TESTS.txt')
    Invoke-Checked 'package' $Python @('tools/package_trial.py', $Package)
    $manifest = Get-Content -LiteralPath (Join-Path $Package 'installation-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.sourceCommit -cne $ExpectedHead) { throw '패키지 manifest sourceCommit 불일치' }
    Invoke-Checked 'installed-package' $Dotnet @('run', '--project', 'tests/MemoApp.WindowsChecks', '-c', 'Release',
        '--no-build', '--', '--installed-package', $Package)
    $Run.installedPackagePassed = $true
    Assert-Checkout 'after'
    $Run.complete = $true
}
catch {
    $Run.failure = $_.Exception.Message
    throw
}
finally {
    Set-Location -LiteralPath $PreviousLocation.Path
    $Run['finishedUtc'] = [DateTime]::UtcNow.ToString('o')
    $Run | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $TestRoot 'run.json') -Encoding utf8
    $evidence = @((Get-Item -LiteralPath (Join-Path $TestRoot 'run.json')))
    foreach ($directory in @($LogRoot, $Viewer, $Package, (Join-Path $OcrRoot 'probe'))) {
        if (Test-Path -LiteralPath $directory) { $evidence += Get-ChildItem -LiteralPath $directory -File -Recurse }
    }
    $evidence | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($TestRoot, $_.FullName); bytes=$_.Length;
            sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $TestRoot 'evidence-sha256.json') -Encoding utf8
    Write-Host "보존할 실행 폴더: $TestRoot"
}
```

`package_trial.py`는 publish 디렉터리 안의 PDB/`createdump.exe`를 제거하고 파일별 크기·SHA256 및 현재 HEAD를 `installation-manifest.json`에 기록한다. 따라서 원본 설치나 기존 산출물 대신 위의 새 publish 경로만 넘긴다. `--installed-package`는 검사 내부의 새 `%TEMP%\memo-package-*` 폴더에 설치하고, 그 폴더의 실제 EXE를 `--portable`로 실행한다. 실제 파일 해시·패키지 내 runtime 로딩·바로가기·기본 잠금·잠긴 트레이 실행·자식 프로세스에만 보낸 session-ending 메시지·정상 종료를 검사한다. 시스템 로그오프/종료나 기존 사용자 설치 실행이 아니다. 검사 자체가 내부 임시 폴더를 정리하므로, 보존 대상은 위의 로그·입력 package·fixture·manifest다. `Install-User.cmd`나 `Start-Portable.cmd`를 따로 실행할 필요가 없다.

## 실패 진단과 근거 판정

- 전체 WPF는 이 실행에서 **한 번** 실행한다. 어떤 그룹이 실패해도 harness가 후속 그룹을 실행할 수 있지만 최종 종료 코드는 실패다. `basic/large/wrap` PDF가 존재하거나 renderer 그룹만 PASS해도 전체 WPF 통과가 아니다. 실패 시 뷰어·publish·package·installed gate를 진행하지 않는다.
- 현재 지원하는 집중 선택자는 `--pdf-rich-paragraph-only`(export/renderer/host 세 그룹), `--pdf-rich-renderer-only`, `--pdf-rich-host-only`다. 이전 `--pdf-rich-paragraph-host-only`는 지원하지 않으며 현재 파서는 알 수 없는 인자를 거절하지 않아 **전체 suite가 실행될 수 있다**. 집중 실행은 진단용이며 전체 gate를 대체하지 않는다.
- 진단을 따로 승인받아 실행할 때에도 위 함수와 검증된 clone에서 `dotnet run --project tests/MemoApp.WindowsChecks -c Release --no-build -- <지원 선택자>`를 `Invoke-Checked`로 호출한다. 매번 `$env:MEMO_N02_VIEWER_OUTPUT`을 **새 비존재 경로**로 지정하고 고유 로그명을 쓴다. host-only는 fixture를 만들지 않는다. 수정 후 전체 합격을 판정하려면 새 `$TestRoot`로 처음부터 다시 실행한다.
- `viewer/manifest.json`은 3개 PDF/reference의 해시·크기를 묶으며 자체 commit 필드는 없다. `run.json`의 정확한 HEAD, 전체 WPF 로그, 그 실행의 7개 fixture 파일, `evidence-sha256.json`을 함께 보존한다. 뷰어 JSON의 버전·세 비교 결과와 실제 종료 코드를 확인한다. 뷰어의 `native_wpf_verified:false`/`product_complete:false`는 유지되며, 독립 뷰어만으로 native 전체 통과를 선언하지 않는다.
- `logs/commands.jsonl`과 모든 단계 로그, `native-ocr/probe/native-build.json`, `installation-manifest.json.sourceCommit`, package 파일 해시를 함께 보고한다. 어느 단계가 미실행/실패했는지와 실제 환경을 적는다. 실패 로그·부분 fixture를 삭제하거나 픽셀 임계값·테스트·Actions 조건을 완화하지 않는다. 주입된 compressed-cap 정리 검사는 실제 문서 픽셀로 16 MiB compressed overflow가 발생했다는 증거가 아니다.
- 전 단계가 통과해도 이 합성 검증만으로 실제 PC 신뢰/ACL, 물리 IME·혼합 DPI, 모든 N02 형식·134 기능·사용자 수용을 완료 처리하지 않는다. 네트워크 관찰은 검사 대상 자식 PID의 특정 잠금 시작/idle 구간 TCP/UDP snapshot 범위뿐이다. 새 installer/portable 전달, Library 업로드 및 반환된 `library_file_id` 확보는 **후속 전달 단계**이며 이 문서 작성 또는 로컬 패키지 생성으로 완료를 주장하지 않는다.
