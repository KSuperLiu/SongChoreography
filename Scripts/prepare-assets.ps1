param([string]$PythonPath = 'Runtime\Python\python.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($PythonPath)) { $PythonPath = Join-Path $projectRoot $PythonPath }
if (-not (Test-Path -LiteralPath $PythonPath)) { throw '请先运行 Scripts/prepare-runtime.ps1，或通过 -PythonPath 指定已安装分析依赖的 Python。' }
& $PythonPath -I -c 'import numpy,scipy,librosa,soundfile'
if ($LASTEXITCODE -ne 0) { throw 'Python 缺少分析依赖，请先准备运行环境。' }
foreach ($name in @('Superhero_Female_FullBody.fbx','Superhero_Male_FullBody.fbx','License_Standard.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "Assets\CharacterArt\Quaternius\$name"))) {
        throw '缺少仓库内的 CC0 角色。请恢复 Assets/CharacterArt/Quaternius；重新下载可使用安装了 requests 的 Python 运行 Scripts/prepare-character.py。'
    }
}
& (Join-Path $PSScriptRoot 'prepare-mocap.ps1')
foreach ($script in @('prepare-mocap.py','prepare-music.py','make-demo.py')) {
    & $PythonPath -I (Join-Path $PSScriptRoot $script)
    if ($LASTEXITCODE -ne 0) { throw "准备素材失败：$script" }
}
Write-Output '素材已准备。接下来运行 Scripts/build.ps1，或在 Unity 执行 Tools > Song Choreography > Prepare Prototype。'
