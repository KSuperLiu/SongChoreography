param([string]$PackagingPython = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = '3.13.17'
$cache = Join-Path $projectRoot 'Samples\RuntimeSources'
$runtimeRoot = Join-Path $projectRoot 'Runtime\Python'
$packages = Join-Path $runtimeRoot 'Lib\site-packages'
New-Item -ItemType Directory -Path $cache,$runtimeRoot,$packages -Force | Out-Null
$archive = Join-Path $cache "python-$version-embed-amd64.zip"
$source = "https://www.python.org/ftp/python/$version/python-$version-embed-amd64.zip"
if (-not (Test-Path -LiteralPath $archive) -or (Get-Item -LiteralPath $archive).Length -lt 1000000) {
    & curl.exe -L --fail --max-time 60 --retry 1 -o ($archive + '.download') $source
    if ($LASTEXITCODE -ne 0) { throw 'Official Python download failed.' }
    Move-Item -LiteralPath ($archive + '.download') -Destination $archive -Force
}
Expand-Archive -LiteralPath $archive -DestinationPath $runtimeRoot -Force
@'
python313.zip
.
Lib/site-packages
import site
'@ | Set-Content -LiteralPath (Join-Path $runtimeRoot 'python313._pth') -Encoding ascii
# Resolve and vendor Windows CPython 3.13 wheels with the development Python.
# The embedded interpreter itself has no pip and never installs at runtime.
$locked = Join-Path $projectRoot 'Analyzer\requirements-windows.lock.txt'
$requirements = if (Test-Path -LiteralPath $locked) { $locked } else { Join-Path $projectRoot 'Analyzer\requirements.txt' }
& $PackagingPython -m pip install --target $packages --python-version 3.13 --platform win_amd64 --implementation cp --abi cp313 --only-binary=:all: --no-compile --ignore-installed -r $requirements
if ($LASTEXITCODE -ne 0) { throw 'Preparing portable analyzer dependencies failed.' }
$interpreter = Join-Path $runtimeRoot 'python.exe'
& $interpreter -I -c 'import sys,numpy,scipy,soundfile,librosa,numba; print(sys.version); print("Portable analyzer imports OK",librosa.__version__,numba.__version__)'
if ($LASTEXITCODE -ne 0) { throw 'Portable analyzer import check failed.' }
if (-not (Test-Path -LiteralPath $locked)) {
    & $interpreter -I -c 'import importlib.metadata as m; print("# Windows x64 / CPython 3.13; validated portable analyzer dependencies"); print("\n".join(sorted(d.metadata["Name"]+"=="+d.version for d in m.distributions())))' | Set-Content -LiteralPath $locked -Encoding utf8
}
$manifest = [ordered]@{
    pythonVersion=$version; platform='Windows x64'; source=$source
    pythonArchiveSha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    isolated=$true; dependencies='Lib/site-packages; original dist-info and package license files retained'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtimeRoot 'RUNTIME.json') -Encoding utf8
Copy-Item -LiteralPath $locked -Destination (Join-Path $runtimeRoot 'requirements-windows.lock.txt') -Force
Write-Output "Portable runtime prepared: $interpreter"
