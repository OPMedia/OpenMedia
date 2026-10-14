# OpenMedia 功能场景测试：启动向导 / 认证 / 媒体库扫描 / WebSocket 回环 / 静态资源与范围请求
# 幂等可重复（服务器可重启多次；向导/媒体库已存在时自动跳过创建步骤）。
# 输出 tests/results/scenarios.md + scenarios-results-*.json。
# PowerShell 5.1 兼容。用法：
#   powershell -ExecutionPolicy Bypass -File scenarios.ps1 [-BaseUrl http://localhost:8096]
param(
    [string]$BaseUrl = 'http://localhost:8096',
    [string]$OutDir = '',
    [string]$AdminUser = 'admin',
    [string]$AdminPw = 'admin',
    [string]$MediaRoot = "D:\Dev\OpenMedia\tests\media"
)
$ErrorActionPreference = 'Continue'
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'results' }
. (Join-Path $PSScriptRoot 'lib\common.ps1')

$ApiBase = "$BaseUrl/emby"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

# ============ 辅助函数（定义在前，供下方调用） ============
function SnippetOf2([string]$Text) {
    if (-not $Text) { return '' }
    $t = $Text.Trim() -replace '\s+', ' '
    if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '…' }
    return $t
}
function EscMd2([string]$Text) {
    if (-not $Text) { return '' }
    return (($Text -replace '\|', '\|') -replace "`r`n", ' ' -replace "`n", ' ')
}

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result {
    param([string]$Section, [string]$Name, [string]$Expected, [string]$Actual, [string]$Status, [string]$Notes = '')
    $results.Add([pscustomobject]@{
        section = $Section; name = $Name; expected = $Expected; actual = $Actual
        status = $Status; notes = $Notes
    })
    $mark = ''
    if ($Status -eq 'PASS') { $mark = 'OK' } elseif ($Status -eq 'FAIL') { $mark = '!!' } else { $mark = '--' }
    Write-Host ("  [{0}] {1}: {2}" -f $mark, $Name, $Status)
}

# WebSocket 回环：连接 → 发 SessionsStart → 触发新会话（SessionStarted → 立即推送）→ 收消息
function Test-WsLoopback {
    param([System.Net.Http.HttpClient]$Client, [string]$Token, [string]$WsBase, [string]$Username, [string]$WorkingPw)
    Import-HttpAssembly
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $ct = [System.Threading.CancellationToken]::None
    try {
        $uri = [Uri]("$WsBase/embywebsocket?api_key=$Token&deviceId=openmedia-test-01")
        $connTask = $ws.ConnectAsync($uri, $ct)
        if (-not $connTask.Wait(15000) -or $ws.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
            return @{ status = 'FAIL'; actual = "连接失败（state=$($ws.State)）"; notes = 'R4 风险项：WebSocket 握手' }
        }
        # 发送 SessionsStart（SessionInfoWebSocketListener：Name="Sessions"，Data="timeoutMs,intervalMs"）
        $msg = '{"MessageType":"SessionsStart","Data":"0,500"}'
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
        $seg = [System.ArraySegment[byte]]::new($bytes)
        $sendTask = $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct)
        [void]$sendTask.Wait(10000)
        # 触发：新 DeviceId 认证 → SessionStarted → SendData(true) 立即推送 Sessions
        $null = Invoke-AuthenticateByName -Client $Client -ApiBase $ApiBase -Username $Username -Pw $WorkingPw -DeviceId 'openmedia-test-02'
        # 接收（单次，15s 超时）
        $buffer = New-Object byte[] 262144
        $rseg = [System.ArraySegment[byte]]::new($buffer)
        $rcvTask = $ws.ReceiveAsync($rseg, $ct)
        if ($rcvTask.Wait(15000)) {
            $rcv = $rcvTask.Result
            if ($rcv.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                return @{ status = 'FAIL'; actual = '服务器立即关闭连接'; notes = 'R4：消息协议/编码检测' }
            }
            $text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $rcv.Count)
            $j = ConvertFrom-JsonSafe $text
            if ($null -ne $j -and $j.PSObject.Properties['MessageType']) {
                return @{ status = 'PASS'; actual = "收到推送 MessageType=$($j.MessageType)（$($rcv.Count)B，EndOfMessage=$($rcv.EndOfMessage)）"; notes = '发送 SessionsStart → 服务器解析并回推，双向回环验证通过' }
            }
            return @{ status = 'FAIL'; actual = '收到非协议消息：' + (SnippetOf2 $text); notes = 'R4：WebSocketMessage JSON 协议' }
        }
        return @{ status = 'FAIL'; actual = '15s 内未收到服务器消息'; notes = 'R4：监听器未推送（SessionsStart 未被处理？）' }
    }
    catch {
        $inner = $_.Exception
        if ($inner -is [System.AggregateException] -and $inner.InnerException) { $inner = $inner.InnerException }
        return @{ status = 'FAIL'; actual = '异常：' + $inner.Message; notes = 'R4：WebSocket' }
    }
    finally {
        try {
            if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
                [void]$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', $ct).Wait(3000)
            } else { $ws.Abort() }
        } catch { $ws.Abort() }
    }
}

# ============ S0 预检 ============
Write-Host "`n=== S0 预检 ===" -ForegroundColor Cyan
$client = New-EmbyClient -TimeoutSec 60
$probe = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Info/Public"
if ($probe.Status -eq 0) {
    Write-Host "服务器不可达（$($probe.Error)）。请先启动服务器。" -ForegroundColor Red
    exit 1
}
$probeJson = ConvertFrom-JsonSafe $probe.Body
$serverId = ''
$serverVersion = ''
if ($probeJson) { $serverId = "$($probeJson.Id)"; $serverVersion = "$($probeJson.Version)" }
Add-Result -Section 'S0-预检' -Name 'GET /System/Info/Public' -Expected '200 + JSON' `
    -Actual "status=$($probe.Status) id=$serverId ver=$serverVersion" `
    -Status $(if ($probe.Status -eq 200) { 'PASS' } else { 'FAIL' }) `
    -Notes $(if (-not (Test-EmbyIdFormat $serverId)) { "Id 非 32 位无连字符 hex：$serverId" } else { 'Id 为 32 位无连字符 hex' })

# ============ S1 启动向导 ============
Write-Host "`n=== S1 启动向导 ===" -ForegroundColor Cyan
$rCfg = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Startup/Configuration"
if ($rCfg.Status -eq 200) {
    Add-Result -Section 'S1-启动向导' -Name 'GET /Startup/Configuration（无 token）' -Expected '200（向导未完成，免认证放行）' `
        -Actual "status=200 body=$(SnippetOf2 $rCfg.Body)" -Status 'PASS'
    $bodyUser = @{ Name = $AdminUser } | ConvertTo-Json -Depth 3
    $rUser = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/Startup/User" -BodyJson $bodyUser `
        -Headers @{ 'X-Emby-Authorization' = (Get-EmbyAuthHeader) }
    Add-Result -Section 'S1-启动向导' -Name 'POST /Startup/User {"Name":"admin"}' -Expected '200/204（设置初始用户名）' `
        -Actual "status=$($rUser.Status)" -Status $(if ($rUser.Status -in @(200, 204)) { 'PASS' } else { 'FAIL' }) `
        -Notes $(if ($rUser.Status -notin @(200, 204)) { (SnippetOf2 $rUser.Body) })
    $rDone = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/Startup/Complete" `
        -Headers @{ 'X-Emby-Authorization' = (Get-EmbyAuthHeader) }
    Add-Result -Section 'S1-启动向导' -Name 'POST /Startup/Complete' -Expected '200/204' `
        -Actual "status=$($rDone.Status)" -Status $(if ($rDone.Status -in @(200, 204)) { 'PASS' } else { 'FAIL' })
}
elseif ($rCfg.Status -eq 401) {
    Add-Result -Section 'S1-启动向导' -Name 'GET /Startup/Configuration（无 token）' -Expected '401（向导已完成，需认证）' `
        -Actual 'status=401' -Status 'PASS' -Notes '向导已完成，跳过向导步骤（幂等）'
}
else {
    Add-Result -Section 'S1-启动向导' -Name 'GET /Startup/Configuration（无 token）' -Expected '200 或 401' `
        -Actual "status=$($rCfg.Status)" -Status 'FAIL' -Notes (SnippetOf2 $rCfg.Body)
}

# ============ S2 认证 ============
Write-Host "`n=== S2 认证 ===" -ForegroundColor Cyan
$token = $null
$workingPw = ''
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw $AdminPw
$authJson = ConvertFrom-JsonSafe $auth.Body
if ($auth.Status -eq 200 -and $authJson -and $authJson.AccessToken) {
    $token = $authJson.AccessToken
    $workingPw = $AdminPw
    $tk = $token; if ($tk.Length -gt 12) { $tk = $tk.Substring(0, 12) + '…' }
    Add-Result -Section 'S2-认证' -Name "POST /Users/AuthenticateByName（$AdminUser/$AdminPw）" -Expected '200 + AccessToken' `
        -Actual "status=200 token=$tk userId=$($authJson.User.Id)" -Status 'PASS'
}
else {
    # 空密码回退（POST /Startup/User 只设用户名，初始用户无密码）
    $auth2 = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw ''
    $auth2Json = ConvertFrom-JsonSafe $auth2.Body
    if ($auth2.Status -eq 200 -and $auth2Json -and $auth2Json.AccessToken) {
        $token = $auth2Json.AccessToken
        $workingPw = ''
        Add-Result -Section 'S2-认证' -Name "POST /Users/AuthenticateByName（$AdminUser/admin）" -Expected '200 + AccessToken' `
            -Actual "admin/admin→$($auth.Status)；空密码→200" -Status 'PASS' -Notes '初始用户无密码（Startup/User 仅设 Name），空密码回退成功'
    }
    else {
        Add-Result -Section 'S2-认证' -Name 'POST /Users/AuthenticateByName' -Expected '200 + AccessToken' `
            -Actual "admin/admin→$($auth.Status)，admin/空→$($auth2.Status)" -Status 'FAIL' -Notes "无法获取 token，后续场景受限。$(SnippetOf2 $auth.Body)"
    }
}

$authBad = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw 'definitely-wrong-password'
Add-Result -Section 'S2-认证' -Name 'POST /Users/AuthenticateByName（错误密码）' -Expected '401' `
    -Actual "status=$($authBad.Status)" -Status $(if ($authBad.Status -eq 401) { 'PASS' } else { 'FAIL' }) `
    -Notes $(if ($authBad.Status -ne 401) { "期望 401，实际 $($authBad.Status)：$(SnippetOf2 $authBad.Body)" })

$hdrs = @{ 'X-Emby-Authorization' = (Get-EmbyAuthHeader) }
if ($token) { $hdrs['X-Emby-Token'] = $token }
$rUsers = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Users" -Headers $hdrs
Add-Result -Section 'S2-认证' -Name 'GET /Users（带 token 复用）' -Expected '200' `
    -Actual "status=$($rUsers.Status)" -Status $(if ($rUsers.Status -eq 200) { 'PASS' } else { 'FAIL' })
$rUsersNoAuth = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Users"
Add-Result -Section 'S2-认证' -Name 'GET /Users（无 token）' -Expected '401' `
    -Actual "status=$($rUsersNoAuth.Status)" -Status $(if ($rUsersNoAuth.Status -eq 401) { 'PASS' } else { 'FAIL' }) `
    -Notes $(if ($rUsersNoAuth.Status -ne 401) { "期望 401，实际 $($rUsersNoAuth.Status)" })

# ============ S3 媒体库 ============
Write-Host "`n=== S3 媒体库 ===" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'setup-media.ps1') -MediaRoot $MediaRoot | Out-Null
$mediaOk = (Test-Path "$MediaRoot\Movie1\Movie1.mkv") -and (Test-Path "$MediaRoot\Movie1\Movie1.nfo") -and `
           (Test-Path "$MediaRoot\Movie2\Movie2.mkv") -and (Test-Path "$MediaRoot\Movie2\Movie2.nfo")
Add-Result -Section 'S3-媒体库' -Name '测试媒体就绪（Movie1/Movie2 mkv+nfo+poster）' -Expected '文件存在' `
    -Actual $(if ($mediaOk) { '就绪' } else { '缺失' }) -Status $(if ($mediaOk) { 'PASS' } else { 'FAIL' })

$folderName = 'OpenMediaTests'
$folderExists = $false
$rFolders = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Library/VirtualFolders" -Headers $hdrs
if ($rFolders.Status -eq 200) {
    $folders = @(ConvertFrom-JsonSafe $rFolders.Body)
    $folderExists = (@($folders | Where-Object { "$($_.Name)" -eq $folderName })).Count -gt 0
    Add-Result -Section 'S3-媒体库' -Name 'GET /Library/VirtualFolders' -Expected '200' `
        -Actual "status=200 文件夹数=$($folders.Count)（$folderName 存在=$folderExists）" -Status 'PASS'
}
else {
    Add-Result -Section 'S3-媒体库' -Name 'GET /Library/VirtualFolders' -Expected '200' `
        -Actual "status=$($rFolders.Status)" -Status 'FAIL' -Notes (SnippetOf2 $rFolders.Body)
}

if (-not $folderExists) {
    $bodyFolder = @{
        Name = $folderName
        CollectionType = 'movies'
        Paths = @($MediaRoot)
        RefreshLibrary = $true
    } | ConvertTo-Json -Depth 5
    $rAdd = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/Library/VirtualFolders" -Headers $hdrs -BodyJson $bodyFolder
    Add-Result -Section 'S3-媒体库' -Name "POST /Library/VirtualFolders（添加 $folderName → $MediaRoot）" -Expected '200/204' `
        -Actual "status=$($rAdd.Status)" -Status $(if ($rAdd.Status -in @(200, 204)) { 'PASS' } else { 'FAIL' }) `
        -Notes $(if ($rAdd.Status -notin @(200, 204)) { (SnippetOf2 $rAdd.Body) })
}
else {
    Add-Result -Section 'S3-媒体库' -Name "POST /Library/VirtualFolders（添加 $folderName）" -Expected '—' `
        -Actual '已存在，跳过（幂等）' -Status 'SKIP'
}

$rRefresh = Invoke-EmbyRequest -Client $client -Method 'POST' -Url "$ApiBase/Library/Refresh" -Headers $hdrs
Add-Result -Section 'S3-媒体库' -Name 'POST /Library/Refresh（触发扫描）' -Expected '200/204' `
    -Actual "status=$($rRefresh.Status)" -Status $(if ($rRefresh.Status -in @(200, 204)) { 'PASS' } else { 'FAIL' })

Write-Host '  轮询 /Items 等待扫描完成（最长 120s）...'
$movies = @()
$deadline = (Get-Date).AddSeconds(120)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$foundSec = -1
while ((Get-Date) -lt $deadline) {
    $rItems = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Items?Recursive=true&IncludeItemTypes=Movie&Limit=20" -Headers $hdrs
    if ($rItems.Status -eq 200) {
        $movies = @((ConvertFrom-JsonSafe $rItems.Body).Items)
        if ($movies.Count -ge 2) { $foundSec = [Math]::Round($sw.Elapsed.TotalSeconds, 1); break }
    }
    Start-Sleep -Seconds 3
}
$sw.Stop()
Add-Result -Section 'S3-媒体库' -Name '扫描出 2 部电影（轮询 /Items?Recursive=true）' -Expected '≥2 个 Movie 条目（≤120s）' `
    -Actual "count=$($movies.Count)$(if ($foundSec -ge 0) { "，耗时 ${foundSec}s" })" `
    -Status $(if ($movies.Count -ge 2) { 'PASS' } else { 'FAIL' }) `
    -Notes $(if ($movies.Count -lt 2) { '扫描超时或未识别（检查服务器日志 ffprobe/媒体识别）' })

$movie1 = @($movies | Where-Object { "$($_.Name)" -eq 'Movie1' }) | Select-Object -First 1
$movie2 = @($movies | Where-Object { "$($_.Name)" -eq 'Movie2' }) | Select-Object -First 1
$movie1Id = ''
if ($movie1) {
    $movie1Id = "$($movie1.Id)"
    $yearOk = ("$($movie1.ProductionYear)" -eq '2020')
    Add-Result -Section 'S3-媒体库' -Name 'Movie1 元数据（nfo title/year）' -Expected 'Name=Movie1, ProductionYear=2020' `
        -Actual "Name=$($movie1.Name), Year=$($movie1.ProductionYear), Id=$movie1Id" `
        -Status $(if ($yearOk) { 'PASS' } else { 'FAIL' }) -Notes 'nfo 解析（title/year）'
}
if ($movie2) {
    $yearOk2 = ("$($movie2.ProductionYear)" -eq '2021')
    Add-Result -Section 'S3-媒体库' -Name 'Movie2 元数据（nfo title/year）' -Expected 'Name=Movie2, ProductionYear=2021' `
        -Actual "Name=$($movie2.Name), Year=$($movie2.ProductionYear)" `
        -Status $(if ($yearOk2) { 'PASS' } else { 'FAIL' })
}
if ($movie1) {
    $rImg = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Items/$movie1Id/Images/Primary" -Headers $hdrs
    Add-Result -Section 'S3-媒体库' -Name 'GET /Items/{Movie1}/Images/Primary（poster.jpg）' -Expected '200 + image/*' `
        -Actual "status=$($rImg.Status) type=$($rImg.ContentType) $($rImg.BodyBytes)B" `
        -Status $(if ($rImg.Status -eq 200 -and $rImg.ContentType -match '^image/') { 'PASS' } else { 'FAIL' }) `
        -Notes $(if ($rImg.Status -eq 404) { '404：poster.jpg 未被拾取为 Primary' })
}

# ============ S4 WebSocket ============
Write-Host "`n=== S4 WebSocket 回环 ===" -ForegroundColor Cyan
if ($token) {
    $wsBase = $BaseUrl -replace '^http', 'ws'
    $wsResult = Test-WsLoopback -Client $client -Token $token -WsBase $wsBase -Username $AdminUser -WorkingPw $workingPw
    Add-Result -Section 'S4-WebSocket' -Name 'ws://…/embywebsocket 连接 + SessionsStart 回环' `
        -Expected '连接成功，收到服务器推送消息（MessageType=Sessions）' `
        -Actual $wsResult.actual -Status $wsResult.status -Notes $wsResult.notes
}
else {
    Add-Result -Section 'S4-WebSocket' -Name 'WebSocket 回环' -Expected '—' -Actual '无 token，跳过' -Status 'SKIP'
}

# ============ S5 静态资源与范围请求 ============
Write-Host "`n=== S5 静态资源与范围请求 ===" -ForegroundColor Cyan
$rIdx = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$BaseUrl/web/index.html"
Add-Result -Section 'S5-静态资源' -Name 'GET /web/index.html' -Expected '200 + text/html' `
    -Actual "status=$($rIdx.Status) type=$($rIdx.ContentType) $($rIdx.BodyBytes)B" `
    -Status $(if ($rIdx.Status -eq 200 -and $rIdx.ContentType -match 'text/html') { 'PASS' } else { 'FAIL' })

$rRange = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$BaseUrl/web/index.html" -Headers @{ 'Range' = 'bytes=0-99' }
$rangeOk = ($rRange.Status -eq 206 -and $rRange.ContentRange -match '^bytes 0-99/\d+' -and $rRange.BodyBytes -le 100)
Add-Result -Section 'S5-静态资源' -Name 'GET /web/index.html + Range: bytes=0-99' -Expected '206 + Content-Range: bytes 0-99/N' `
    -Actual "status=$($rRange.Status) content-range=$($rRange.ContentRange) body=$($rRange.BodyBytes)B" `
    -Status $(if ($rangeOk) { 'PASS' } else { 'FAIL' }) `
    -Notes $(if (-not $rangeOk) { 'R3 风险项：范围请求语义（视频拖动依赖）' })

$rIdx2 = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/web/index.html"
Add-Result -Section 'S5-静态资源' -Name 'GET /emby/web/index.html（前缀克隆）' -Expected '200 + text/html' `
    -Actual "status=$($rIdx2.Status) type=$($rIdx2.ContentType)" `
    -Status $(if ($rIdx2.Status -eq 200 -and $rIdx2.ContentType -match 'text/html') { 'PASS' } else { 'FAIL' })

$rFav = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$BaseUrl/favicon.ico"
Add-Result -Section 'S5-静态资源' -Name 'GET /favicon.ico' -Expected '200' `
    -Actual "status=$($rFav.Status) type=$($rFav.ContentType)" `
    -Status $(if ($rFav.Status -eq 200) { 'PASS' } else { 'FAIL' })

$rRobots = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$BaseUrl/robots.txt"
Add-Result -Section 'S5-静态资源' -Name 'GET /robots.txt' -Expected '200' `
    -Actual "status=$($rRobots.Status) type=$($rRobots.ContentType)" `
    -Status $(if ($rRobots.Status -eq 200) { 'PASS' } else { 'FAIL' })

$rPing = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Ping"
Add-Result -Section 'S5-静态资源' -Name 'GET /emby/System/Ping' -Expected '200' `
    -Actual "status=$($rPing.Status) body=$(SnippetOf2 $rPing.Body)" `
    -Status $(if ($rPing.Status -eq 200) { 'PASS' } else { 'FAIL' })

if ($movie1Id) {
    $rVs = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Videos/$movie1Id/stream?static=true" -Headers @{ 'Range' = 'bytes=0-99' }
    $vsOk = ($rVs.Status -eq 206 -and $rVs.ContentRange -match '^bytes')
    Add-Result -Section 'S5-静态资源' -Name 'GET /Videos/{Movie1}/stream?static=true + Range' -Expected '206 + Content-Range（R3）' `
        -Actual "status=$($rVs.Status) content-range=$($rVs.ContentRange) type=$($rVs.ContentType) $($rVs.BodyBytes)B" `
        -Status $(if ($vsOk) { 'PASS' } else { 'FAIL' }) `
        -Notes $(if (-not $vsOk) { 'R3 风险项：流式范围请求（伪媒体 ffprobe 失败可能影响，需人工复核）' })
}
else {
    Add-Result -Section 'S5-静态资源' -Name 'GET /Videos/{Movie1}/stream + Range' -Expected '—' -Actual '无 Movie1 条目，跳过' -Status 'SKIP'
}
$client.Dispose()

# ============ 输出 ============
$pass = @($results | Where-Object { $_.status -eq 'PASS' }).Count
$fail = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$skip = @($results | Where-Object { $_.status -eq 'SKIP' }).Count

$raw = [pscustomobject]@{
    runAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    baseUrl = $BaseUrl; apiBase = $ApiBase
    serverId = $serverId; serverVersion = $serverVersion
    token = $(if ($token) { 'acquired' } else { 'FAILED' })
    summary = [pscustomobject]@{ pass = $pass; fail = $fail; skip = $skip; total = @($results).Count }
    results = $results
}
Write-ResultFile -Path (Join-Path $OutDir "scenarios-results-$stamp.json") -Content ($raw | ConvertTo-Json -Depth 6)
Write-ResultFile -Path (Join-Path $OutDir 'scenarios-results-latest.json') -Content ($raw | ConvertTo-Json -Depth 6)

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# OpenMedia 功能场景测试报告')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("> 运行时间：$($raw.runAt)　基址：$ApiBase　ServerId：$serverId　版本：$serverVersion")
[void]$sb.AppendLine('')
[void]$sb.AppendLine("**结果：PASS $pass / FAIL $fail / SKIP $skip（共 $(@($results).Count)）**")
[void]$sb.AppendLine('')
$section = ''
foreach ($r in $results) {
    if ($r.section -ne $section) {
        $section = $r.section
        [void]$sb.AppendLine("## $section")
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('| 场景 | 期望 | 实际 | 结果 | 备注 |')
        [void]$sb.AppendLine('|------|------|------|------|------|')
    }
    [void]$sb.AppendLine("| $(EscMd2 $r.name) | $(EscMd2 $r.expected) | $(EscMd2 $r.actual) | **$($r.status)** | $(EscMd2 $r.notes) |")
}
Write-ResultFile -Path (Join-Path $OutDir 'scenarios.md') -Content $sb.ToString()

Write-Host ''
Write-Host '=== 功能场景测试完成 ==='
Write-Host ("PASS {0} / FAIL {1} / SKIP {2}" -f $pass, $fail, $skip)
Write-Host "输出: $OutDir\scenarios.md / scenarios-results-$stamp.json"
