param([string]$UnityPath='I:\Program Files\Unity 2022.3.62f3\Editor\Unity.exe',[switch]$InPlace,[switch]$PublicRelease)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if ($PublicRelease -and $InPlace) { throw '公开发行必须使用隔离快照，避免改动本机音乐库。' }
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "找不到 Unity：$UnityPath" }
$buildRoot=$projectRoot
if (-not $InPlace) {
    $cacheBoundary=[IO.Path]::GetFullPath((Join-Path $projectRoot '.build-cache'))+[IO.Path]::DirectorySeparatorChar
    $buildRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot '.build-cache\AIStudioProject'))
    if (-not $buildRoot.StartsWith($cacheBoundary,[StringComparison]::OrdinalIgnoreCase)) { throw '构建目录越界。' }
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
    foreach ($name in @('Assets','Packages','ProjectSettings','Generation')) {
        $inputPath=Join-Path $projectRoot $name
        $targetPath=[IO.Path]::GetFullPath((Join-Path $buildRoot $name))
        if (-not $targetPath.StartsWith($buildRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '快照目录越界。' }
        & robocopy.exe $inputPath $targetPath /MIR /COPY:DAT /DCOPY:DAT /NFL /NDL /NJH /NJS /NP /XD '.idea' | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "快照复制失败：$name" }
    }
    if ($PublicRelease) {
        # Keep locally downloaded recordings on the user's machine. The public
        # build includes the project's original 100 BPM practice track instead.
        $musicPath=[IO.Path]::GetFullPath((Join-Path $buildRoot 'Assets\StreamingAssets\MusicLibrary'))
        $snapshotOutput=[IO.Path]::GetFullPath((Join-Path $buildRoot 'Builds\AIStudio'))
        foreach ($target in @($musicPath,$snapshotOutput)) {
            if (-not $target.StartsWith($buildRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '发行清理目录越界。' }
            if (Test-Path -LiteralPath $target) {
                if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '发行清理目录不能是链接。' }
                Remove-Item -LiteralPath $target -Recurse -Force
            }
        }
        New-Item -ItemType Directory -Path $musicPath -Force | Out-Null
        $practiceTrack=Join-Path $buildRoot 'Assets\StreamingAssets\Practice100\normal.wav'
        if (-not (Test-Path -LiteralPath $practiceTrack)) { throw '公开发行缺少原创 100 BPM 练习伴奏。' }
        Copy-Item -LiteralPath $practiceTrack -Destination (Join-Path $musicPath 'original100.wav')
        $publicMusic=[ordered]@{schemaVersion=1;purpose='Public release: original practice recording';tracks=@(
            [ordered]@{id='original-100';title='Original Groove - 100 BPM';artist='SongChoreography';genre='Practice';file='original100.wav';durationSeconds=38.4;sha256=(Get-FileHash -LiteralPath $practiceTrack -Algorithm SHA256).Hash.ToLowerInvariant();sourceUrl='https://github.com/KSuperLiu/SongChoreography';licenseName='Project original recording';licenseUrl='https://github.com/KSuperLiu/SongChoreography/blob/main/LICENSE'}
        )}
        $publicMusic | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $musicPath 'catalog.json') -Encoding utf8
    }
    # Reuse already installed packages only in the disposable build copy.
    # Preserve the user's source manifest and package additions exactly.
    $manifestPath=Join-Path $buildRoot 'Packages\manifest.json'
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $packageCache=Join-Path $projectRoot 'Library\PackageCache'
    if (Test-Path -LiteralPath $packageCache) {
        foreach ($package in Get-ChildItem -LiteralPath $packageCache -Directory) {
            $packageName=$package.Name.Split('@')[0]
            if ($packageName.StartsWith('com.unity.modules.')) { continue }
            $manifest.dependencies | Add-Member -NotePropertyName $packageName -NotePropertyValue ('file:'+$package.FullName.Replace('\','/')) -Force
        }
        $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    }
}
$resultFolder=Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultFolder -Force | Out-Null
$log=Join-Path $resultFolder 'ai-studio-build.log'
$arguments='-batchmode -nographics -quit -projectPath "'+$buildRoot+'" -executeMethod SongChoreography.Editor.StudioBuild.Build -logFile "'+$log+'"'
$build=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$build.WaitForExit()
if ($build.ExitCode -ne 0) { throw "构建失败，请查看 $log" }
$output=Join-Path $projectRoot $(if ($PublicRelease) { 'Builds\PublicAIStudio' } else { 'Builds\AIStudio' })
if (-not $InPlace) {
    $sourceOutput=Join-Path $buildRoot 'Builds\AIStudio'
    $outputBoundary=[IO.Path]::GetFullPath((Join-Path $projectRoot 'Builds'))+[IO.Path]::DirectorySeparatorChar
    $resolvedOutput=[IO.Path]::GetFullPath($output)
    if (-not $resolvedOutput.StartsWith($outputBoundary,[StringComparison]::OrdinalIgnoreCase)) { throw '产物目录越界。' }
    & robocopy.exe $sourceOutput $resolvedOutput /MIR /COPY:DAT /DCOPY:DAT /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw '复制新构建失败。' }
    $snapshotAssets=Join-Path $buildRoot 'Assets'
    foreach ($meta in Get-ChildItem -LiteralPath $snapshotAssets -Filter '*.meta' -Recurse -File) {
        $relative=[IO.Path]::GetRelativePath($snapshotAssets,$meta.FullName)
        $destination=Join-Path (Join-Path $projectRoot 'Assets') $relative
        if ((Test-Path -LiteralPath $destination.Substring(0,$destination.Length-5)) -and -not (Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $meta.FullName -Destination $destination }
    }
}
$runtime=Join-Path $projectRoot 'Runtime\Python'
if (Test-Path -LiteralPath (Join-Path $runtime 'python.exe')) {
    $destination=Join-Path $output 'Runtime'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $runtime -Destination $destination -Recurse -Force
} else { Write-Warning '音乐分析所需便携 Python 未准备；请运行 Scripts\prepare-runtime.ps1。' }
Write-Output (Join-Path $output 'ChoreoAIStudio.exe')
