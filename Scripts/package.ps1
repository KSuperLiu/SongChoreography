param([string]$CandidateDirectory='Builds\PublicWindows051',[string]$Version='0.5.1',[string]$PortableResultPath='TestResults\portable-public-051-result.json',[string]$PortableEnvironmentPath='TestResults\portable-public-051-environment.json')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$source=(Resolve-Path -LiteralPath $CandidateDirectory).Path
$distributionPath=Join-Path $source 'DISTRIBUTION.json'
if(-not(Test-Path -LiteralPath $distributionPath)) { throw '缺少分发检查记录，请从当前源码重新构建公开版本。' }
if(Test-Path -LiteralPath $distributionPath) {
    $distribution=Get-Content -LiteralPath $distributionPath -Raw | ConvertFrom-Json
    if($distribution.personalUseOnly -or $distribution.containsRestrictedMotions) { throw '此构建含个人学习动作，禁止打入公开发行包。' }
}
if(Test-Path -LiteralPath (Join-Path $source 'ChoreoStudio_Data\StreamingAssets\PersonalMotionCredits')) { throw '发现个人素材署名目录，不能公开打包。' }
$product=Get-Content -LiteralPath (Join-Path $source 'ProductAcceptance\results.json') -Raw | ConvertFrom-Json
$cameras=Get-Content -LiteralPath (Join-Path $source 'Acceptance\results.json') -Raw | ConvertFrom-Json
$portable=Get-Content -LiteralPath (Join-Path $projectRoot $PortableResultPath) -Raw | ConvertFrom-Json
$portableEnvironment=Get-Content -LiteralPath (Join-Path $projectRoot $PortableEnvironmentPath) -Raw | ConvertFrom-Json
$assemblyHash=(Get-FileHash -LiteralPath (Join-Path $source 'ChoreoStudio_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash
if(-not $product.allPassed -or $product.version -ne $Version -or $cameras.version -ne $Version -or $product.frames -lt 1000 -or $product.style -ne 'charleston' -or -not $cameras.allPassed -or $cameras.checks.Count -lt 57 -or -not $portable.analyzerImported -or -not $portable.analyzerBundled -or -not $portable.reloadWorks -or $portableEnvironment.originalRuntimeAssemblySha256 -ne $assemblyHash -or $portableEnvironment.copiedRuntimeAssemblySha256 -ne $assemblyHash) { throw '最终候选的版本、成品、角色镜头或便携验收尚未全部通过，不能打包。' }
$joints=Get-Content -LiteralPath (Join-Path $source 'JointRange\results.json') -Raw | ConvertFrom-Json
$transitions=Get-Content -LiteralPath (Join-Path $source 'CharlestonReview\results.json') -Raw | ConvertFrom-Json
if(-not $product.jointRangesPassed -or $joints.cases.Count -ne 16 -or @($joints.cases | Where-Object {-not $_.passed -or $_.id -notlike 'charleston-*'}).Count -gt 0 -or $transitions.version -ne $Version -or $transitions.cases.Count -ne 40 -or @($transitions.cases | Where-Object {-not $_.passed -or $_.from -notlike 'charleston-*' -or $_.to -notlike 'charleston-*'}).Count -gt 0) { throw '当前版本的公共关节或连接检查未通过。' }
if($product.backgroundQa -and $product.explicitStageRenders -lt 1000) { throw '后台验收缺少实际舞台渲染。' }
$releaseRoot=Join-Path $projectRoot 'Release'
$destination=[IO.Path]::GetFullPath((Join-Path $releaseRoot ('ChoreoStudio-'+$Version+'-Windows')))
if(-not $destination.StartsWith([IO.Path]::GetFullPath($releaseRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '发布路径越界。' }
if(Test-Path -LiteralPath $destination) { throw "发布目录已存在，保留旧发布：$destination" }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $destination
Copy-Item -LiteralPath $distributionPath -Destination $destination
foreach($name in @('ChoreoStudio.exe','ChoreoStudio_Data','UnityPlayer.dll','UnityCrashHandler64.exe','MonoBleedingEdge','Runtime','DemoProject')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination -Recurse -Force
}
$docs=Join-Path $destination '说明与验收';New-Item -ItemType Directory -Path $docs -Force | Out-Null
foreach($name in @('QUICK_START.md','THIRD_PARTY_NOTICES.md','MUSIC_CREDITS.md','CHARACTERS_AND_CAMERA.md','WINDOWS_051_RELEASE.md','ANATOMY_051_VERIFICATION.md','ANATOMY_051_RESULTS.json')) { Copy-Item -LiteralPath (Join-Path $projectRoot ('Docs\'+$name)) -Destination $docs }
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs\QUICK_START.md') -Destination (Join-Path $destination '开始使用.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Analyzer\requirements-windows.lock.txt') -Destination (Join-Path $destination 'Runtime\Python\requirements-windows.lock.txt') -Force
Copy-Item -LiteralPath (Join-Path $source 'ProductAcceptance') -Destination $docs -Recurse
Copy-Item -LiteralPath (Join-Path $source 'Acceptance') -Destination $docs -Recurse
foreach($name in @('CharlestonReview','JointRange')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $docs -Recurse }
Copy-Item -LiteralPath (Join-Path $projectRoot $PortableResultPath) -Destination (Join-Path $docs 'portable-result.json')
Copy-Item -LiteralPath (Join-Path $projectRoot $PortableEnvironmentPath) -Destination (Join-Path $docs 'portable-environment.json')
$guide=@'
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>CHOREO __VERSION__ · 开始使用</title>
<style>body{background:#0c131e;color:#e1edf2;font:17px/1.8 system-ui;margin:40px auto;max-width:1100px;padding:0 24px}h1{font-size:36px}h2{margin-top:32px;color:#42ddc2}a{color:#42ddc2}img{width:100%;border:1px solid #263746;border-radius:12px}li{margin:8px 0}.note{color:#9eafbe}</style>
<h1>CHOREO / 编舞练习室 __VERSION__</h1><p>双击同目录的 <b>ChoreoStudio.exe</b>，点击右上角<b>一键演示</b>。完整目录可离线运行，无需安装 Python。</p>
<img src="说明与验收/Acceptance/female_front.png" alt="0.5.1 公共版实际运行画面">
<h2>开始练习</h2><ol><li>选择内置免费音乐，或导入自己的 WAV／MP3，截取 30～60 秒。</li><li>首次跟学建议 0.5×／0.75×；使用背面或镜像，切换脚步特写。</li><li>点击片段定位、替换或锁定；“换一版”保留锁定片段。</li><li>保存后可再次打开。移动项目请复制整个项目文件夹。</li></ol>
<p><a href="开始使用.md">完整使用说明</a> · <a href="说明与验收/THIRD_PARTY_NOTICES.md">资源署名</a></p>
<h2>验收证据</h2><p><a href="说明与验收/Acceptance/report.html">两角色、七镜头与15张实机图</a> · <a href="说明与验收/CharlestonReview/report.html">20条连接、40张播放器采样渲染板</a> · <a href="说明与验收/ProductAcceptance/results.json">完整展示与后台显式渲染测量</a> · <a href="说明与验收/WINDOWS_051_RELEASE.md">本版说明</a></p>
<p class="note">公共版舞种为 Charleston 练习改编（四段动捕裁剪与四个左右变体）。New Jazz 个人学习动作受来源许可限制，不随公共版分发。采用本地规则编排；没有生成式动作模型。技术和 Codex 画面复查不代表舞蹈教师认证；软件时钟差不包含扬声器硬件延迟。实际点击与自动验收的范围见随包验证记录。</p>
</html>
'@
[IO.File]::WriteAllText((Join-Path $destination '开始使用.html'),$guide.Replace('__VERSION__',$Version))
$verificationFiles=Get-ChildItem -LiteralPath $destination -File -Recurse | ForEach-Object { [ordered]@{path=[IO.Path]::GetRelativePath($destination,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} }
[ordered]@{version=$Version;createdUtc=[DateTime]::UtcNow.ToString('o');unity='2022.3.62f3';runtimeAssemblySha256=$assemblyHash;files=@($verificationFiles)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'PACKAGE.json') -Encoding utf8
$zip=$destination+'.zip'
if(Test-Path -LiteralPath $zip) { throw "ZIP 已存在：$zip" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($destination,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($zip) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output $destination
Write-Output $zip
