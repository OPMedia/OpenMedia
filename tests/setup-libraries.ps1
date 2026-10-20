# OpenMedia 媒体库 fixture（幂等）：把 TV / 音乐目录注册为媒体库并触发扫描。
# 背景：API 矩阵 ⑤ 类 fixture（/Artists|/Albums|/Shows|... ）需要库内有真实实体，
#       否则 {Name} 占位符取不到值 → 端点 404 → 误判为 FAIL（2026-10-20 全量回归定位）。
# 前置：先跑 setup-tv.ps1 / setup-music.ps1 生成素材。
param(
    [string]$ApiBase = 'http://localhost:8096/emby',
    [string]$AdminUser = 'admin',
    [string]$AdminPw = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\common.ps1')

$client = New-EmbyClient
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw $AdminPw
$authJson = ConvertFrom-JsonSafe $auth.Body
if (-not ($auth.Status -eq 200 -and $authJson -and $authJson.AccessToken)) { throw "认证失败: $($auth.Status)" }
$token = $authJson.AccessToken
$hdrs = @{ 'X-Emby-Token' = $token }

$rFolders = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Library/VirtualFolders" -Headers $hdrs
$folders = @((ConvertFrom-JsonSafe $rFolders.Body))

$targets = @(
    @{ Name = 'OpenMediaTestsTV';     Type = 'tvshows'; Path = 'D:\Dev\OpenMedia\tests\media-tv' },
    @{ Name = 'OpenMediaTestsMusic';  Type = 'music';    Path = 'D:\Dev\OpenMedia\tests\media-music' }
)

foreach ($t in $targets) {
    $exists = (@($folders | Where-Object { "$($_.Name)" -eq $t.Name })).Count -gt 0
    if ($exists) { Write-Host "跳过（已存在）: $($t.Name)"; continue }
    $body = @{ Name = $t.Name; CollectionType = $t.Type; Paths = @($t.Path); RefreshLibrary = $false } | ConvertTo-Json -Depth 5
    $r = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/Library/VirtualFolders" -Headers $hdrs -BodyJson $body
    Write-Host "添加 $($t.Name) [$($t.Type)] -> $($t.Path) : status=$($r.Status)"
    if ($r.Status -notin @(200, 204)) { Write-Host ("  body: " + $r.Body) }
}

$rRefresh = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/Library/Refresh" -Headers $hdrs
Write-Host "触发刷新: status=$($rRefresh.Status)"

# 轮询等待 TV / 音乐条目落库（Series / Episode / Audio）
$deadline = (Get-Date).AddSeconds(180)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$counts = @{}
while ((Get-Date) -lt $deadline) {
    $r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Items?Recursive=true&Limit=500" -Headers $hdrs
    if ($r.Status -eq 200) {
        $items = @((ConvertFrom-JsonSafe $r.Body).Items)
        $series = @($items | Where-Object { "$($_.Type)" -eq 'Series' }).Count
        $episodes = @($items | Where-Object { "$($_.Type)" -eq 'Episode' }).Count
        $audio = @($items | Where-Object { "$($_.Type)" -eq 'AudioAlbum' -or "$($_.Type)" -eq 'Audio' -or "$($_.Type)" -eq 'MusicAlbum' }).Count
        $counts = @{ Series = $series; Episode = $episodes; Audio = $audio; Total = $items.Count }
        if ($series -ge 1 -and $episodes -ge 1 -and $audio -ge 1) { break }
    }
    Start-Sleep -Seconds 4
}
$sw.Stop()
Write-Host ("扫描耗时 {0}s，最终计数: {1}" -f [Math]::Round($sw.Elapsed.TotalSeconds,1), ($counts | ConvertTo-Json -Compress))
