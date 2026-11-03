$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & dotnet run --project Tests/CoreTests.csproj -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'C# 核心检查失败。' }
    & python -X utf8 -m unittest discover -s Tests -p test_generation.py -v
    if ($LASTEXITCODE -ne 0) { throw '生成任务检查失败。' }
    & python -X utf8 -m unittest discover -s Tests -p test_mixamo_registration.py -v
    if ($LASTEXITCODE -ne 0) { throw 'Mixamo 素材登记检查失败。' }
    $python=Join-Path $projectRoot 'Runtime\Python\python.exe'
    if (-not (Test-Path -LiteralPath $python)) { $python='python' }
    & $python -I -m unittest discover -s Tests -p test_analyzer.py -v
    if ($LASTEXITCODE -ne 0) { throw '音频检查失败。' }
} finally { Pop-Location }
