param([string]$CandidateDirectory='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$archived=[string]::IsNullOrWhiteSpace($CandidateDirectory)
$candidate=if($archived) { Join-Path $projectRoot 'TestResults\CharlestonReview' } else { (Resolve-Path -LiteralPath $CandidateDirectory).Path }
$review=Get-Content -LiteralPath (Join-Path $projectRoot 'Assets\SongChoreography\Editor\MocapData\reviewed-charleston.json') -Raw | ConvertFrom-Json
$transitions=Get-Content -LiteralPath (Join-Path $candidate $(if($archived){'all-candidate-transitions.json'}else{'TransitionReview\results.json'})) -Raw | ConvertFrom-Json
$loops=Get-Content -LiteralPath (Join-Path $candidate $(if($archived){'full-loop-results.json'}else{'MotionPreview\review.json'})) -Raw | ConvertFrom-Json
$destination=Join-Path $projectRoot 'TestResults\CharlestonReview'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$selected=@()
foreach($motion in $review.motions) {
    $loop=@($loops.clips | Where-Object id -EQ $motion.id)
    if($loop.Count -ne 1 -or $loop[0].loopPositionGap -ge .002 -or $loop[0].loopRotationGap -ge .5 -or $loop[0].lowestSole -lt 0 -or $loop[0].maxFrameRotationGap -gt 8) { throw "动作循环未通过：$($motion.id)" }
    foreach($next in $motion.safeNext) {
        foreach($character in @('female','male')) {
            $cases=@($transitions.cases | Where-Object { $_.from -eq $motion.id -and $_.to -eq $next -and $_.character -eq $character })
            if($cases.Count -ne 1 -or -not $cases[0].passed) { throw "连接未通过：$character / $($motion.id) -> $next" }
            $case=$cases[0];$selected+=$case
            if($archived) {
                if(-not (Test-Path -LiteralPath (Join-Path $destination $case.screenshot))) { throw "缺少已归档的实机图：$($case.screenshot)" }
            } else { Copy-Item -LiteralPath (Join-Path $candidate ('TransitionReview\'+$case.screenshot)) -Destination $destination -Force }
        }
    }
}
if(-not $archived) {
    Copy-Item -LiteralPath (Join-Path $candidate 'TransitionReview\results.json') -Destination (Join-Path $destination 'all-candidate-transitions.json') -Force
    Copy-Item -LiteralPath (Join-Path $candidate 'MotionPreview\review.json') -Destination (Join-Path $destination 'full-loop-results.json') -Force
}
$review | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $destination 'reviewed-charleston.json') -Encoding utf8
$selected | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'retained-transitions.json') -Encoding utf8
$html=[Text.StringBuilder]::new("<!doctype html><meta charset='utf-8'><title>Charleston 动作复查</title><style>body{background:#0d1722;color:#deedf3;font:16px system-ui;margin:32px}img{width:100%;max-width:1600px}article{margin:32px 0}</style><h1>Charleston 练习动作与连接复查</h1><p>2026-11-02 · 四段 CMU subject 93 动捕裁剪，加四个左右变体；八拍练习改编。88 项候选连接技术检查通过，正式库仅保留已逐图复查的 20 条有向连接（男女角色共 40 张六帧实机图）。Codex 技术和画面复查，不代表舞蹈教师认证；New Jazz 素材缺口仍保留。</p>")
foreach($case in $selected) {
    [void]$html.Append('<article><h2>').Append([Net.WebUtility]::HtmlEncode($case.character+' / '+$case.from+' → '+$case.to)).Append('</h2><p>切换间隙 ').Append(($case.cutPositionGap*1000).ToString('0.00')).Append(' mm · 120 Hz 最大单帧旋转 ').Append($case.maxFrameAngle.ToString('0.00')).Append("°</p><img src='").Append($case.screenshot).Append("'></article>")
}
[IO.File]::WriteAllText((Join-Path $destination 'report.html'),$html.ToString())
Write-Output "Archived $($selected.Count) reviewed character transitions: $destination"
