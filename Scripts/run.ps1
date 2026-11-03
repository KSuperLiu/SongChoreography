param([switch]$Smoke)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$application=Join-Path $projectRoot 'Builds\AIStudio\ChoreoAIStudio.exe'
if (-not (Test-Path -LiteralPath $application)) { throw '请先运行 Scripts\build.ps1。' }
$options=@{FilePath=$application;WorkingDirectory=$projectRoot;WindowStyle='Normal'}
if ($Smoke) { $options.ArgumentList='-smoke -report-dir "'+(Join-Path $projectRoot 'TestResults\AIStudioSmoke')+'"' }
Start-Process @options
