param([int[]]$Trials = @(3,4,5,8))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root 'Samples\MotionSources\CMU93'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$files = @('93.asf') + @($Trials | ForEach-Object { '93_{0:00}.amc' -f $_ })
$records = @()
foreach ($name in $files) {
    $target = Join-Path $destination $name
    $url = 'https://mocap.cs.cmu.edu/subjects/93/' + $name
    if (-not (Test-Path -LiteralPath $target)) {
        Invoke-WebRequest -Uri $url -OutFile $target -TimeoutSec 60
    }
    if ((Get-Item -LiteralPath $target).Length -lt 1000) { throw "Invalid motion file: $name" }
    $records += [ordered]@{ file=$name; source=$url; sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
    Write-Output "Prepared $name"
}
$source = [ordered]@{
    creator='Carnegie Mellon University Graphics Lab Motion Capture Database'
    subject=93
    description='Charleston Dance; not New Jazz'
    source='https://mocap.cs.cmu.edu/search.php?subjectnumber=93'
    terms='https://mocap.cs.cmu.edu/'
    retrieved='2026-11-02'
    usage='Free use in research and commercially sold products; do not resell motion data directly, even converted. Keep original files in development sources; distribute baked animation only as part of this application.'
    acknowledgement='The data used in this project was obtained from mocap.cs.cmu.edu. The database was created with funding from NSF EIA-0196217.'
    files=$records
}
$source | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'SOURCE.json') -Encoding utf8
@'
CMU Graphics Lab Motion Capture Database
Source: https://mocap.cs.cmu.edu/
Subject 93: Charleston Dance (NOT New Jazz).

The data used in this project was obtained from mocap.cs.cmu.edu.
The database was created with funding from NSF EIA-0196217.

The official database permits use in research and inclusion in commercially
sold products, and prohibits reselling the motion data directly, including
converted data. This application is a choreography and practice tool; original
ASF/AMC files are development sources and are not bundled as a motion asset pack.

Source motion is captured at 120 frames per second. Derived clips are retargeted,
trimmed, oriented, and timed by the application. A source description alone does
not establish the quality of those edits or the safety of arbitrary transitions.
'@ | Set-Content -LiteralPath (Join-Path $destination 'CREDITS.txt') -Encoding utf8
