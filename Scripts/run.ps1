param([string]$PythonPath = '',[switch]$PersonalLearning,[string]$BuildDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) { $BuildDirectory = if ($PersonalLearning) { 'Builds\PersonalWindows' } else { 'Builds\Windows' } }
$applicationPath = Join-Path (Join-Path $projectRoot $BuildDirectory) 'ChoreoStudio.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) { throw '请先执行 Scripts\build.ps1。' }
if (-not [string]::IsNullOrWhiteSpace($PythonPath)) {
    $env:SONG_CHOREO_PYTHON = (Get-Command $PythonPath -ErrorAction Stop).Source
}
Start-Process -FilePath $applicationPath -WorkingDirectory $projectRoot -WindowStyle Normal
