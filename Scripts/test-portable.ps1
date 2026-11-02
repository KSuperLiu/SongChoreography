param([string]$BuildDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) { $BuildDirectory = Join-Path $projectRoot 'Builds\Windows' }
$sourceRoot = (Resolve-Path -LiteralPath $BuildDirectory).Path
$results = Join-Path $projectRoot 'TestResults'
$target = Join-Path $results ('便携验证 Space\' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('ChoreoStudio.exe','ChoreoStudio_Data','UnityPlayer.dll','UnityCrashHandler64.exe','MonoBleedingEdge','Runtime')) {
    $file = Join-Path $sourceRoot $name
    if (-not (Test-Path -LiteralPath $file)) { throw "发布包缺少必需文件：$name" }
    Copy-Item -LiteralPath $file -Destination $target -Recurse -Force
}
$log = Join-Path $target 'portable.log'
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = Join-Path $target 'ChoreoStudio.exe'
$startInfo.WorkingDirectory = $target
$startInfo.Arguments = '-portable-smoke -screen-width 1920 -screen-height 1080 -logFile "' + $log + '"'
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables.Remove('SONG_CHOREO_PYTHON')
# This applies to this test child only. It does not change the user's environment.
$startInfo.EnvironmentVariables['PATH'] = Join-Path $env:SystemRoot 'System32'
$process = [System.Diagnostics.Process]::Start($startInfo)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw '便携导入检查超时，请检查 portable.log。' }
if ($process.ExitCode -ne 0) { throw "便携导入检查失败：$($process.ExitCode)，日志：$log" }
$resultPath = Join-Path $target 'smoke-result.json'
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (-not $result.analyzerImported -or -not $result.analyzerBundled -or -not $result.reloadWorks) { throw '内置分析或保存恢复未通过。' }
Copy-Item -LiteralPath $resultPath -Destination (Join-Path $results 'portable-result.json') -Force
[ordered]@{
    movedPackage=$target
    nonAsciiAndSpacePath=$true
    systemPythonRemovedFromChildPath=$true
    analyzerEnvironmentOverrideRemoved=$true
    exitCode=$process.ExitCode
    originalExecutableSha256=(Get-FileHash -LiteralPath (Join-Path $sourceRoot 'ChoreoStudio.exe') -Algorithm SHA256).Hash
    copiedExecutableSha256=(Get-FileHash -LiteralPath (Join-Path $target 'ChoreoStudio.exe') -Algorithm SHA256).Hash
    originalRuntimeAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $sourceRoot 'ChoreoStudio_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash
    copiedRuntimeAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $target 'ChoreoStudio_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'portable-environment.json') -Encoding utf8
Write-Output "Portable test passed: $target"
