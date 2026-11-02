param([string]$PythonPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $projectRoot 'Builds\Windows\ChoreoStudio.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) { throw '请先执行 Scripts\build.ps1。' }
if (-not [string]::IsNullOrWhiteSpace($PythonPath)) {
    $env:SONG_CHOREO_PYTHON = (Get-Command $PythonPath -ErrorAction Stop).Source
}
Start-Process -FilePath $applicationPath -WorkingDirectory $projectRoot -WindowStyle Normal
