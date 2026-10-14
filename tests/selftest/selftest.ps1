# 自检：用本地 mock 服务器（selftest\mock-server.ps1，端口 8196）验证测试引擎运行时行为。
# 覆盖：Invoke-EmbyRequest（状态码/ContentType/Body/Content-Range）、认证助手、
#       PascalCase 违规检测、Id 格式校验、Range 206、401 语义、WebSocket 回环。
# 用法：先在后台启动 mock-server.ps1，再运行本脚本；或直接运行（会自动提示）。
param([string]$MockBase = 'http://localhost:8196')
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\common.ps1')

$pass = 0; $fail = 0
function Assert([string]$Name, [bool]$Cond, [string]$Detail = '') {
    if ($Cond) { $script:pass++; Write-Host "  [OK] $Name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  [!!] $Name  $Detail" -ForegroundColor Red }
}

$api = "$MockBase/emby"
$client = New-EmbyClient -TimeoutSec 15

# 1. 预检式请求
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/System/Info/Public"
Assert 'GET /System/Info/Public → 200' ($r.Status -eq 200) "status=$($r.Status)"
Assert 'ContentType json' ($r.ContentType -match 'json') "ct=$($r.ContentType)"
$j = ConvertFrom-JsonSafe $r.Body
Assert 'JSON 解析 + ServerName' ($null -ne $j -and $j.ServerName -eq 'MockServer')
Assert 'Id 32hex 校验函数' (Test-EmbyIdFormat "$($j.Id)")
$pv = @(Get-PascalViolations $j)
Assert 'PascalCase 无违规' ($pv.Count -eq 0) "$($pv -join ',')"
Assert 'Get-TopKeys 输出' ((Get-TopKeys $j) -match 'ServerName')

# 2. 违规检测
$rBad = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/badkeys"
$jBad = ConvertFrom-JsonSafe $rBad.Body
$pvBad = @(Get-PascalViolations $jBad)
Assert 'camelCaseKey 违规被检出' ($pvBad.Count -eq 1 -and $pvBad[0] -eq 'camelCaseKey') "$($pvBad -join ',')"

# 3. 认证助手 + 401
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $api -Username 'admin' -Pw 'admin'
Assert 'AuthenticateByName → 200' ($auth.Status -eq 200) "status=$($auth.Status)"
$aj = ConvertFrom-JsonSafe $auth.Body
Assert 'AccessToken 解析' ($null -ne $aj -and $aj.AccessToken)
$authBad = Invoke-AuthenticateByName -Client $client -ApiBase $api -Username 'admin' -Pw 'definitely-wrong-password'
Assert '错误密码 → 401' ($authBad.Status -eq 401) "status=$($authBad.Status)"

# 4. token 头透传
$rU = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/Users" -Headers @{ 'X-Emby-Token' = $aj.AccessToken }
Assert '带 token GET /Users → 200' ($rU.Status -eq 200) "status=$($rU.Status)"
$rU2 = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/Users"
Assert '无 token GET /Users → 401' ($rU2.Status -eq 401) "status=$($rU2.Status)"

# 5. QueryResult 结构
$rI = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/Items"
$ji = ConvertFrom-JsonSafe $rI.Body
Assert 'Items + TotalRecordCount' ($ji.PSObject.Properties['Items'] -and $ji.PSObject.Properties['TotalRecordCount'])

# 6. Range 206 + Content-Range
$rFull = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$MockBase/web/index.html"
Assert 'GET /web/index.html → 200 text/html' ($rFull.Status -eq 200 -and $rFull.ContentType -match 'text/html') "s=$($rFull.Status) ct=$($rFull.ContentType)"
$rRange = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$MockBase/web/index.html" -Headers @{ 'Range' = 'bytes=0-99' }
Assert 'Range → 206' ($rRange.Status -eq 206) "status=$($rRange.Status)"
Assert 'Content-Range 头捕获' ($rRange.ContentRange -match '^bytes 0-99/\d+') "cr=$($rRange.ContentRange)"
Assert 'Range body ≤ 100B' ($rRange.BodyBytes -le 100) "bytes=$($rRange.BodyBytes)"

# 7. 404 捕获
$r404 = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/nonexistent"
Assert '404 状态捕获' ($r404.Status -eq 404) "status=$($r404.Status)"

# 8. WebSocket 回环（ClientWebSocket 用法与 scenarios.ps1 一致）
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.Options.AddSubProtocol('mocktest')   # mock 服务器约定子协议（真实服务器无子协议）
$ct = [System.Threading.CancellationToken]::None
$wsOk = $false
$wsMsg = ''
try {
    $uri = [Uri]"$($MockBase -replace '^http','ws')/embywebsocket?api_key=$($aj.AccessToken)&deviceId=selftest-01"
    [void]$ws.ConnectAsync($uri, $ct).Wait(10000)
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $msg = '{"MessageType":"SessionsStart","Data":"0,500"}'
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
        [void]$ws.SendAsync([System.ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait(5000)
        $buf = New-Object byte[] 65536
        $rcvTask = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $ct)
        if ($rcvTask.Wait(10000)) {
            $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $rcvTask.Result.Count)
            $wj = ConvertFrom-JsonSafe $text
            if ($wj -and $wj.MessageType -eq 'Sessions') { $wsOk = $true; $wsMsg = $text }
        }
    }
}
catch {
    $inner = $_.Exception
    while ($inner.InnerException) { $inner = $inner.InnerException }
    $wsMsg = "$($inner.GetType().Name): $($inner.Message)"
}
finally {
    try { if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) { [void]$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', $ct).Wait(3000) } else { $ws.Abort() } } catch { $ws.Abort() }
}
Assert 'WebSocket 连接+发送+接收回环' $wsOk $wsMsg

# 9. Ping 文本响应
$rP = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$api/System/Ping"
Assert 'GET /System/Ping → 200' ($rP.Status -eq 200 -and $rP.Body.Trim() -eq 'pong')

$client.Dispose()
Write-Host ''
Write-Host "=== 自检结果：PASS $pass / FAIL $fail ==="
if ($fail -gt 0) { exit 1 } else { exit 0 }
