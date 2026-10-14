# OpenMedia LiveTv 测试 fixture（幂等）：XmlTv EPG + M3U 频道 + 录制链路
# 产物（tests\livetv\）：
#   guide.xml    —— XmlTv 节目单：2 频道 × 2 节目，时间窗以"当前时间"为中心
#                   （-1h ~ +3h，保证节目正在播出 → timer 可即时触发录制）
#   channels.m3u —— M3U 频道表：tvg-id 与 guide.xml 频道 id 对齐（EPG 映射靠它），
#                   流地址指向服务器自身的 Movie1 静态流（真实可播放的 HTTP 源）
# 然后通过 API 幂等配置：XmlTv ListingProvider + M3U TunerHost + 触发 guide 刷新。
# PowerShell 5.1 兼容。
param(
    [string]$ApiBase = "http://127.0.0.1:8096/emby",
    [string]$FixtureRoot = "D:\Dev\OpenMedia\tests\livetv",
    [string]$AdminUser = 'admin',
    [string]$AdminPw = ''
)
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\common.ps1')
$client = New-EmbyClient

# ---------- 认证 ----------
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw $AdminPw
$authJson = ConvertFrom-JsonSafe $auth.Body
if (-not ($auth.Status -eq 200 -and $authJson -and $authJson.AccessToken)) { throw "认证失败: $($auth.Status)" }
$token = $authJson.AccessToken
$hdrs = @{ 'X-Emby-Token' = $token }
Write-Host "认证 OK"

# ---------- 1. guide.xml（时间窗以当前 UTC 为中心）----------
if (-not (Test-Path -LiteralPath $FixtureRoot)) { New-Item -ItemType Directory -Force -Path $FixtureRoot | Out-Null }
$now = [DateTime]::UtcNow
$start = $now.AddHours(-1).ToString('yyyyMMddHHmmss')
$end = $now.AddHours(3).ToString('yyyyMMddHHmmss')
$guidePath = Join-Path $FixtureRoot 'guide.xml'
$guideXml = @"
<?xml version="1.0" encoding="UTF-8"?>
<tv source-info-name="OpenMediaTest">
  <channel id="ch1.test.1">
    <display-name>Test One</display-name>
    <display-name>101</display-name>
  </channel>
  <channel id="ch2.test.2">
    <display-name>Test Two</display-name>
    <display-name>102</display-name>
  </channel>
  <programme start="$start +0000" stop="$end +0000" channel="ch1.test.1">
    <title lang="en">Alpha Show Live</title>
    <desc lang="en">Currently airing test programme on channel one.</desc>
    <category lang="en">News</category>
  </programme>
  <programme start="$start +0000" stop="$end +0000" channel="ch2.test.2">
    <title lang="en">Beta Movie Live</title>
    <desc lang="en">Currently airing test movie on channel two.</desc>
    <category lang="en">Movie</category>
  </programme>
</tv>
"@
[System.IO.File]::WriteAllText($guidePath, $guideXml, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "guide.xml: 窗口 $start -> $end UTC（节目正在播出）"

# ---------- 2. channels.m3u（流地址 = 服务器自身 Movie1 静态流）----------
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Items?Recursive=true&IncludeItemTypes=Movie&Limit=1" -Headers $hdrs
$movieId = ''
if ($r.Status -eq 200) {
    $items = @(ConvertFrom-JsonSafe $r.Body)
    if ($items.Count -gt 0) { $movieId = "$($items[0].Items[0].Id)" }
}
if (-not $movieId) { throw "找不到 Movie 条目（先跑 setup-media.ps1 并扫描）" }
$m3uPath = Join-Path $FixtureRoot 'channels.m3u'
$streamUrl = "$ApiBase/Videos/$movieId/stream?static=true".Replace('http://127.0.0.1:8096', 'http://127.0.0.1:8096')
$m3u = @"
#EXTM3U
#EXTINF:-1 tvg-id="ch1.test.1" tvg-name="Test One" group-title="Test",Test One
$streamUrl
#EXTINF:-1 tvg-id="ch2.test.2" tvg-name="Test Two" group-title="Test",Test Two
$streamUrl
"@
[System.IO.File]::WriteAllText($m3uPath, $m3u, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "channels.m3u: 2 频道 -> $streamUrl"

# ---------- 3. XmlTv ListingProvider（幂等：按 Path 去重，分隔符不敏感）----------
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/LiveTv/ListingProviders" -Headers $hdrs
$providers = @()
if ($r.Status -eq 200) { $providers = @(ConvertFrom-JsonSafe $r.Body) }
$guidePathNorm = $guidePath.Replace('/', '\')
$exists = @($providers | Where-Object { "$($_.Path)".Replace('/', '\') -eq $guidePathNorm }).Count -gt 0
if (-not $exists) {
    $body = @{
        Type = 'xmltv'
        Path = $guidePath
        Name = 'XmlTV Test'
        KidsCategories = @()
        MovieCategories = @('Movie')
        NewsCategories = @('News')
        SportsCategories = @()
    } | ConvertTo-Json -Depth 5
    $r = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/LiveTv/ListingProviders" -Headers $hdrs -BodyJson $body
    if ($r.Status -notin @(200, 204)) { throw "添加 ListingProvider 失败: $($r.Status) $($r.Body)" }
    Write-Host "ListingProvider(xmltv) 已添加"
} else { Write-Host "ListingProvider(xmltv) 已存在，跳过" }

# ---------- 4. M3U TunerHost（幂等：按 Url 去重，分隔符不敏感）----------
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/LiveTv/TunerHosts" -Headers $hdrs
$hosts = @()
if ($r.Status -eq 200) { $hosts = @(ConvertFrom-JsonSafe $r.Body) }
$m3uPathNorm = $m3uPath.Replace('/', '\')
$exists = @($hosts | Where-Object { "$($_.Url)".Replace('/', '\') -eq $m3uPathNorm }).Count -gt 0
if (-not $exists) {
    $body = @{ Type = 'm3u'; Url = $m3uPath; Name = 'M3U Test' } | ConvertTo-Json -Depth 5
    $r = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/LiveTv/TunerHosts" -Headers $hdrs -BodyJson $body
    if ($r.Status -notin @(200, 204)) { throw "添加 TunerHost 失败: $($r.Status) $($r.Body)" }
    Write-Host "TunerHost(m3u) 已添加"
} else { Write-Host "TunerHost(m3u) 已存在，跳过" }

# ---------- 5. 触发 guide 刷新（节目单/频道入库）----------
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/ScheduledTasks" -Headers $hdrs
$tasks = @(ConvertFrom-JsonSafe $r.Body)
$guideTask = @($tasks | Where-Object { "$($_.Name)" -eq 'Refresh Guide' }) | Select-Object -First 1
if ($guideTask) {
    Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/ScheduledTasks/Running/$($guideTask.Id)" -Headers $hdrs | Out-Null
    Write-Host "guide 刷新已触发（等待 8s）..."
    Start-Sleep -Seconds 8
}

# ---------- 6. 验收断言 ----------
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/LiveTv/Channels" -Headers $hdrs
$channels = @()
if ($r.Status -eq 200) { $channels = @((ConvertFrom-JsonSafe $r.Body).Items) }
$ok = $channels.Count -ge 2
Write-Host ("验收 /LiveTv/Channels: count={0}（期望 >=2）{1}" -f $channels.Count, $(if ($ok) { ' [PASS]' } else { ' [FAIL]' }))
if (-not $ok) { throw "频道未发现（检查 M3U 路径与服务器日志）" }

$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/LiveTv/Programs" -Headers $hdrs
$programs = @()
if ($r.Status -eq 200) { $programs = @((ConvertFrom-JsonSafe $r.Body).Items) }
$ok = $programs.Count -ge 2
Write-Host ("验收 /LiveTv/Programs: count={0}（期望 >=2，正在播出）{1}" -f $programs.Count, $(if ($ok) { ' [PASS]' } else { ' [FAIL]' }))
if (-not $ok) { throw "节目单为空（检查 XmlTv provider 与 guide 刷新）" }

Write-Host ''
Write-Host 'LiveTv fixture 就绪。DVR 验证（可选）：'
Write-Host "  POST $ApiBase/LiveTv/Timers  body: ServiceName=Emby + ProgramId=<上面任一节目 Id> + ChannelId=<对应频道 Id> + StartDate/EndDate"
Write-Host '  录制文件落 ProgramData-Server\data\livetv\recordings\（.ts + nfo），/LiveTv/Recordings 可见'
