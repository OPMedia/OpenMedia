# ============================================================
# T3.2 复测：② 类 11 个转码/HLS 端点 + 转码端到端（docs/phase2-design.md §3.2）
# 前提：服务器已启动（8096）、ffmpeg 已接入（T3.1 完成）、媒体为真实文件
# 输出：results/retest-ffmpeg-<ts>.json（含受测 PID，R6 约定）
# 幂等可重复；PS 5.1 兼容
# ============================================================
param(
    [int]$Port = 8096,
    [string]$TranscodeDirOverride = ''
)

. "$PSScriptRoot\lib\common.ps1"

$baseUrl = "http://127.0.0.1:$Port"
$apiBase = "$baseUrl/emby"
$deviceId = 'tester-t32-01'
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
# transcoding-temp：默认按部署目录探测；验证实例（8097 等）用 -TranscodeDirOverride 指定
$transcodeDir = $null
if ($TranscodeDirOverride) {
    $transcodeDir = $TranscodeDirOverride
} else {
    foreach ($cand in @('D:\OpenMedia\ProgramData-Server\transcoding-temp', (Join-Path $PSScriptRoot '..\MediaBrowser.Server\bin\Debug\net10.0\ProgramData-Server\transcoding-temp'))) {
        if (Test-Path $cand) { $transcodeDir = (Resolve-Path $cand).Path; break }
    }
}

$client = New-EmbyClient -TimeoutSec 120
$script:Cases = [System.Collections.Generic.List[object]]::new()

function Add-Case {
    param([string]$Group, [string]$Op, [string]$Url, [string]$Expect, $R, [string]$Note = '')
    $snippet = ''
    if ($null -ne $R -and $R.Body) { $snippet = ($R.Body -replace "\r?\n", ' '); if ($snippet.Length -gt 160) { $snippet = $snippet.Substring(0, 160) + '...' } }
    $script:Cases.Add([pscustomobject]@{
        group = $Group; op = $Op; url = $Url; expect = $Expect
        status = $(if ($null -ne $R) { $R.Status } else { 0 })
        contentType = $(if ($null -ne $R) { $R.ContentType } else { '' })
        bodyBytes = $(if ($null -ne $R) { $R.BodyBytes } else { 0 })
        elapsedMs = $(if ($null -ne $R) { $R.ElapsedMs } else { 0 })
        snippet = $snippet; note = $Note
        error = $(if ($null -ne $R) { $R.Error } else { 'not run' })
    })
}

# ---------- 预检 ----------
$pub = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$apiBase/System/Info/Public"
if ($pub.Status -ne 200) { Write-Host "预检失败：/System/Info/Public => $($pub.Status)"; exit 1 }
$listen = (netstat -ano | Where-Object { $_ -match (':' + $Port + '\s+.+LISTENING') } | Select-Object -First 1)
$serverPid = ''
if ($listen) { $serverPid = ($listen.Trim() -split '\s+')[-1] }
Write-Host "受测实例 PID=$serverPid  Version=$((ConvertFrom-JsonSafe $pub.Body).Version)"

# ---------- 认证 ----------
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $apiBase -Username 'admin' -Pw 'admin'
if ($auth.Status -eq 401) { $auth = Invoke-AuthenticateByName -Client $client -ApiBase $apiBase -Username 'admin' -Pw '' }
$authJson = ConvertFrom-JsonSafe $auth.Body
if (-not $authJson -or -not $authJson.AccessToken) { Write-Host "认证失败：$($auth.Status)"; exit 1 }
$token = $authJson.AccessToken
$userId = $authJson.User.Id
$headers = @{ 'X-Emby-Token' = $token }
Write-Host "认证 OK userId=$userId"

# ---------- 上下文：Movie1 ----------
# 注：/Users/{uid}/Items + IncludeItemTypes 当前返回空（BUG-20，见报告）；用 /Items 口径
$items = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$apiBase/Items?Recursive=true&IncludeItemTypes=Movie&Limit=20&Fields=Path,MediaSources" -Headers $headers
$itemsJson = ConvertFrom-JsonSafe $items.Body
if (-not $itemsJson -or $itemsJson.TotalRecordCount -lt 1) { Write-Host "无 Movie 条目"; exit 1 }
$movie = $itemsJson.Items[0]
$movieId = $movie.Id
$mediaSourceId = $movie.MediaSources[0].Id
Write-Host "Movie1 Id=$movieId MediaSourceId=$mediaSourceId Path=$($movie.Path)"

# ---------- A. ② 类 11 端点（原矩阵口径，Id 换当前 Movie1）----------
$origCases = @(
    @{ op = 'GET /Audio/{Id}/stream.{Container}'; url = "/Audio/$movieId/stream.mkv" },
    @{ op = 'GET /Audio/{Id}/stream'; url = "/Audio/$movieId/stream?Container=mkv" },
    @{ op = 'GET /Videos/{Id}/stream.{Container}'; url = "/Videos/$movieId/stream.mkv" },
    @{ op = 'GET /Videos/{Id}/stream'; url = "/Videos/$movieId/stream?Container=mkv" },
    @{ op = 'GET /Videos/{Id}/subtitles.m3u8'; url = "/Videos/$movieId/subtitles.m3u8?SubtitleSegmentLength=test&ManifestSubtitles=test" },
    @{ op = 'GET /Videos/{Id}/live.m3u8'; url = "/Videos/$movieId/live.m3u8?Container=mkv" },
    @{ op = 'GET /Audio/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}'; url = "/Audio/$movieId/hls1/test/test.test?Container=mkv" },
    @{ op = 'GET /Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}'; url = "/Videos/$movieId/hls1/test/test.test?Container=mkv" },
    @{ op = 'GET /Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}'; url = "/Videos/$movieId/hls/test/test.test?Container=mkv" },
    @{ op = 'GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}'; url = "/Videos/$movieId/test/Subtitles/0/Stream.original" },
    @{ op = 'GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/{StartPositionTicks}/Stream.{Format}'; url = "/Videos/$movieId/test/Subtitles/0/test/Stream.original" }
)
foreach ($c in $origCases) {
    $r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$apiBase$($c.url)" -Headers $headers
    Add-Case -Group 'A-原口径复测' -Op $c.op -Url "$apiBase$($c.url)" -Expect '不再出现 ffprobe 缺失类 500（Cannot start process...）' -R $r
    Write-Host ("[A] {0} => {1} ({2}ms)" -f $c.op, $r.Status, $r.ElapsedMs)
}

# ---------- B. 端到端：PlaybackInfo → live.m3u8 → 分片 → 停止 → 清理 ----------
# B1 PlaybackInfo：MediaStreams 应有 ffprobe 探测出的真实流
$pbi = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$apiBase/Items/$movieId/PlaybackInfo?UserId=$userId" -Headers $headers
$pbiJson = ConvertFrom-JsonSafe $pbi.Body
$streamSummary = ''
if ($pbiJson -and $pbiJson.MediaSources -and $pbiJson.MediaSources.Count -gt 0) {
    $ms = $pbiJson.MediaSources[0]
    $vid = @($ms.MediaStreams | Where-Object { $_.Type -eq 'Video' })
    $aud = @($ms.MediaStreams | Where-Object { $_.Type -eq 'Audio' })
    if ($vid.Count -gt 0) { $streamSummary += "video=$($vid[0].Codec) $($vid[0].Width)x$($vid[0].Height); " }
    if ($aud.Count -gt 0) { $streamSummary += "audio=$($aud[0].Codec); " }
    $streamSummary += "runTime=$($ms.RunTimeTicks)"
}
Add-Case -Group 'B-端到端' -Op 'GET /Items/{Id}/PlaybackInfo' -Url "$apiBase/Items/$movieId/PlaybackInfo?UserId=$userId" -Expect '200 + MediaStreams 含 codec/分辨率' -R $pbi -Note $streamSummary
Write-Host "[B1] PlaybackInfo => $($pbi.Status)  $streamSummary"

# B2 live.m3u8 探测（短超时客户端：BUG-22 会挂起，20s 即判超时记录）
$playSessionId = [Guid]::NewGuid().ToString('N')
$clientShort = New-EmbyClient -TimeoutSec 20
$liveUrl = "$apiBase/Videos/$movieId/live.m3u8?DeviceId=$deviceId&PlaySessionId=$playSessionId&MediaSourceId=$mediaSourceId&SegmentContainer=ts"
$live = Invoke-EmbyRequest -Client $clientShort -Method 'GET' -Url $liveUrl -Headers $headers
$liveOk = ($live.Status -eq 200 -and $live.Body -match '#EXTM3U')
Add-Case -Group 'B-端到端' -Op 'GET /Videos/{Id}/live.m3u8（真实参数）' -Url $liveUrl -Expect '200 + #EXTM3U（已知 BUG-21/22：可能 500 或挂起）' -R $live
Write-Host "[B2] live.m3u8 => $($live.Status)  err=$($live.Error)"

# B3 动态 HLS（web 播放器实际链路）：master → main → 分片
$beforeFiles = @()
if ($transcodeDir) { $beforeFiles = @(Get-ChildItem -Path $transcodeDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) }
$masterUrl = "$apiBase/Videos/$movieId/master.m3u8?DeviceId=$deviceId&PlaySessionId=$playSessionId&MediaSourceId=$mediaSourceId&VideoCodec=h264&AudioCodec=aac"
$master = Invoke-EmbyRequest -Client $client -Method 'GET' -Url $masterUrl -Headers $headers
Add-Case -Group 'B-端到端' -Op 'GET /Videos/{Id}/master.m3u8（真实参数）' -Url $masterUrl -Expect '200 + #EXTM3U' -R $master
Write-Host "[B3] master.m3u8 => $($master.Status)  bodyBytes=$($master.BodyBytes)"

$mainUrl = "$apiBase/Videos/$movieId/main.m3u8?DeviceId=$deviceId&PlaySessionId=$playSessionId&MediaSourceId=$mediaSourceId&VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts"
$main = Invoke-EmbyRequest -Client $client -Method 'GET' -Url $mainUrl -Headers $headers
$mainOk = ($main.Status -eq 200 -and $main.Body -match '#EXTM3U')
Add-Case -Group 'B-端到端' -Op 'GET /Videos/{Id}/main.m3u8（真实参数）' -Url $mainUrl -Expect '200 + 分片列表' -R $main
Write-Host "[B4] main.m3u8 => $($main.Status)  bodyBytes=$($main.BodyBytes)"

# B5 取分片（首取可能撞 BUG-23 竞态 500，2s 后重试一次）
$segUrls = @()
if ($mainOk) {
    foreach ($line in ($main.Body -split "\r?\n")) {
        if ($line -match '^hls1/') { $segUrls += $line.Trim() }
    }
}
$segResults = @()
$i = 0
foreach ($su in ($segUrls | Select-Object -First 3)) {
    $segUrl = "$apiBase/Videos/$movieId/$su"
    $sg = Invoke-EmbyRequest -Client $client -Method 'GET' -Url $segUrl -Headers $headers
    $note = ''
    if ($sg.Status -ne 200) {
        Start-Sleep -Seconds 2
        $sg2 = Invoke-EmbyRequest -Client $client -Method 'GET' -Url $segUrl -Headers $headers
        $note = "retry: $($sg.Status)->$($sg2.Status)"
        if ($sg2.Status -eq 200) { $sg = $sg2 }
    }
    $segResults += [pscustomobject]@{ seg = $su.Substring(0, [Math]::Min(60, $su.Length)); status = $sg.Status; bytes = $sg.BodyBytes; ms = $sg.ElapsedMs; note = $note }
    Add-Case -Group 'B-端到端' -Op 'GET /Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}（真实分片）' -Url $segUrl -Expect '200 + 分片字节 > 0（首取可能 500=BUG-23 竞态，重试应 200）' -R $sg -Note $note
    Write-Host ("[B5] segment[{0}] => {1} ({2} bytes, {3}ms) {4}" -f $i, $sg.Status, $sg.BodyBytes, $sg.ElapsedMs, $note)
    $i++
}

# B6 transcoding-temp 应出现分片文件（相对会话前新增）
Start-Sleep -Seconds 2
$ttFiles = @()
if ($transcodeDir) { $ttFiles = @(Get-ChildItem -Path $transcodeDir -File -ErrorAction SilentlyContinue) }
$newFiles = @($ttFiles | Where-Object { $beforeFiles -notcontains $_.Name })
$ttInfo = "total=$($ttFiles.Count) newThisSession=$($newFiles.Count) newSize=$([Math]::Round(($newFiles | Measure-Object Length -Sum).Sum / 1KB, 0))KB"
Write-Host "[B6] transcoding-temp ($transcodeDir): $ttInfo"

# B7 停止转码：DELETE /Videos/ActiveEncodings
$del = Invoke-EmbyRequest -Client $client -Method 'DELETE' -Url "$apiBase/Videos/ActiveEncodings?DeviceId=$deviceId&PlaySessionId=$playSessionId" -Headers $headers
Add-Case -Group 'B-端到端' -Op 'DELETE /Videos/ActiveEncodings' -Url "$apiBase/Videos/ActiveEncodings?DeviceId=$deviceId&PlaySessionId=$playSessionId" -Expect '204（停止转码）' -R $del
Write-Host "[B7] DELETE ActiveEncodings => $($del.Status)"

# B8 清理验证：本会话新增文件应被清掉（轮询最多 90s；历史泄漏文件不算）
$cleanSec = -1
$swClean = [System.Diagnostics.Stopwatch]::StartNew()
while ($swClean.Elapsed.TotalSeconds -lt 90) {
    Start-Sleep -Seconds 5
    if (-not $transcodeDir) { break }
    $cur = @(Get-ChildItem -Path $transcodeDir -File -ErrorAction SilentlyContinue)
    $stillNew = @($cur | Where-Object { $beforeFiles -notcontains $_.Name })
    if ($stillNew.Count -eq 0) { $cleanSec = [int]$swClean.Elapsed.TotalSeconds; break }
}
$ffmpegLeft = @(Get-Process -Name ffmpeg -ErrorAction SilentlyContinue).Count
Write-Host "[B8] 本会话文件清理耗时=$cleanSec s（-1=未清理）  残留 ffmpeg 进程=$ffmpegLeft"

# ---------- 收尾：服务器仍存活 ----------
$pingEnd = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$apiBase/System/Ping" -Headers $headers
Write-Host "收尾 Ping => $($pingEnd.Status)"

# ---------- 输出 ----------
$out = [pscustomobject]@{
    runAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    task = 'T3.2 ②类11端点复测 + 转码端到端'
    baseUrl = $baseUrl
    serverPid = $serverPid
    context = @{ UserId = $userId; ItemId = $movieId; MediaSourceId = $mediaSourceId; PlaySessionId = $playSessionId }
    playbackStreams = $streamSummary
    e2e = [pscustomobject]@{
        liveM3u8Status = $live.Status; liveM3u8Error = $live.Error
        masterM3u8Status = $master.Status; mainM3u8Status = $main.Status
        segmentCountInPlaylist = $segUrls.Count
        segments = $segResults; transcodeTempDuringPlay = $ttInfo; transcodeDir = $transcodeDir
        deleteActiveEncodingsStatus = $del.Status
        cleanupSeconds = $cleanSec; ffmpegProcessesAfterStop = $ffmpegLeft
        serverAliveAtEnd = ($pingEnd.Status -eq 200)
    }
    cases = $script:Cases
}
$json = $out | ConvertTo-Json -Depth 6
Write-ResultFile -Path "$PSScriptRoot\results\retest-ffmpeg-$ts.json" -Content $json
Write-ResultFile -Path "$PSScriptRoot\results\retest-ffmpeg-latest.json" -Content $json
Write-Host ""
Write-Host "结果已写入 results\retest-ffmpeg-$ts.json"
