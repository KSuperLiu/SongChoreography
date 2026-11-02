param(
    [string]$UnityPath = 'I:\Program Files\Unity 2022.3.62f3\Editor\Unity.exe',
    [switch]$Isolated,
    [string]$OutputDirectory = 'Builds\Windows'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "找不到 Unity：$UnityPath" }
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'TestResults') -Force | Out-Null
$buildRoot = $projectRoot
$buildLog = Join-Path $projectRoot 'TestResults\unity-build.log'
if ($Isolated) {
    $buildRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-cache\UnityProject'))
    $cacheBoundary = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-cache')) + [IO.Path]::DirectorySeparatorChar
    if (-not $buildRoot.StartsWith($cacheBoundary,[StringComparison]::OrdinalIgnoreCase)) { throw '隔离构建目录越界。' }
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
    # Only mirror explicitly named inputs into our disposable build snapshot.
    # Every purge target is verified under .build-cache before robocopy runs.
    foreach ($name in @('Assets','Packages','ProjectSettings','Analyzer','Samples\Demo','Samples\MotionSources\CMU93')) {
        $inputPath = Join-Path $projectRoot $name
        if (-not (Test-Path -LiteralPath $inputPath)) { continue }
        $snapshotPath = [IO.Path]::GetFullPath((Join-Path $buildRoot $name))
        if (-not $snapshotPath.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '隔离快照路径越界。' }
        & robocopy.exe $inputPath $snapshotPath /MIR /COPY:DAT /DCOPY:DAT /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "复制隔离输入失败：$name" }
    }
    $buildLog = Join-Path $projectRoot 'TestResults\unity-build-isolated.log'
}
$buildArgs = '-batchmode -nographics -quit -projectPath "' + $buildRoot + '" -executeMethod PrototypeBuilder.Build -logFile "' + $buildLog + '"'
$buildProcess = Start-Process -FilePath $UnityPath -ArgumentList $buildArgs -WindowStyle Hidden -PassThru
$buildProcess.WaitForExit()
if ($buildProcess.ExitCode -ne 0) { throw "Unity 构建失败，请检查 $buildLog" }
if ($Isolated) {
    $snapshotAssets = Join-Path $buildRoot 'Assets'
    foreach ($metadata in Get-ChildItem -LiteralPath $snapshotAssets -Filter '*.meta' -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($snapshotAssets,$metadata.FullName)
        $sourceMetadata = Join-Path (Join-Path $projectRoot 'Assets') $relative
        $sourceAsset = $sourceMetadata.Substring(0,$sourceMetadata.Length-5)
        if ((Test-Path -LiteralPath $sourceAsset) -and -not (Test-Path -LiteralPath $sourceMetadata)) {
            Copy-Item -LiteralPath $metadata.FullName -Destination $sourceMetadata
        }
    }
}
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$produced = Join-Path $buildRoot 'Builds\Windows'
if (-not $produced.Equals($outputRoot,[StringComparison]::OrdinalIgnoreCase)) {
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    & robocopy.exe $produced $outputRoot /E /COPY:DAT /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw '复制 Windows 构建产物失败。' }
}
$portableRuntime = Join-Path $projectRoot 'Runtime\Python'
if (Test-Path -LiteralPath (Join-Path $portableRuntime 'RUNTIME.json')) {
    $runtimeDestination = Join-Path $outputRoot 'Runtime'
    New-Item -ItemType Directory -Path $runtimeDestination -Force | Out-Null
    Copy-Item -LiteralPath $portableRuntime -Destination $runtimeDestination -Recurse -Force
} else {
    Write-Warning '便携分析引擎未准备；仅开发构建可回退系统 Python。成品发布前请运行 Scripts/prepare-runtime.ps1。'
}
Write-Output (Join-Path $outputRoot 'ChoreoStudio.exe')
