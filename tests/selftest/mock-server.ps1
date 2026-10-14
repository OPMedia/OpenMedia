# Mock Emby server for selftest (listens http://localhost:8196/, handles up to 300 requests)
# Endpoints: /emby/System/Info/Public, /emby/Users (401 w/o token), /emby/Users/AuthenticateByName,
#            /emby/Items, /emby/badkeys, /emby/System/Ping, /web/index.html (+Range 206), /embywebsocket
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add('http://localhost:8196/')
$listener.Start()
Write-Host "mock-server listening on http://localhost:8196/"

$id32 = '0123456789abcdef0123456789abcdef'
$html = '<html><body>mock index page for range testing ' + ('x' * 300) + '</body></html>'

function Send-Json($res, $obj, [int]$code = 200) {
    $res.StatusCode = $code
    $res.ContentType = 'application/json'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Depth 6))
    $res.ContentLength64 = $bytes.Length
    $res.OutputStream.Write($bytes, 0, $bytes.Length)
}

$count = 0
while ($listener.IsListening -and $count -lt 300) {
    $ctx = $listener.GetContext()
    $req = $ctx.Request
    $res = $ctx.Response
    $path = $req.Url.AbsolutePath
    $count++
    try {
        if ($path -eq '/emby/System/Info/Public') {
            Send-Json $res @{ ServerName = 'MockServer'; Id = $id32; Version = '4.1.1.0'; OperatingSystem = 'Windows' }
        }
        elseif ($path -eq '/emby/Users/AuthenticateByName' -and $req.HttpMethod -eq 'POST') {
            $body = New-Object byte[] 4096
            $n = $req.InputStream.Read($body, 0, 4096)
            $text = [System.Text.Encoding]::UTF8.GetString($body, 0, $n)
            if ($text -match '"Pw":\s*"definitely-wrong-password"') {
                Send-Json $res @{ } 401
            }
            else {
                Send-Json $res @{ AccessToken = $id32; User = @{ Id = $id32; Name = 'admin' } }
            }
        }
        elseif ($path -eq '/emby/Users') {
            $tok = $req.Headers['X-Emby-Token']
            if (-not $tok) { Send-Json $res @{ } 401 }
            else { Send-Json $res @(@{ Id = $id32; Name = 'admin'; HasPassword = $true }) }
        }
        elseif ($path -eq '/emby/Items') {
            Send-Json $res @{ Items = @(@{ Id = $id32; Name = 'Movie1'; Type = 'Movie'; ProductionYear = 2020 }); TotalRecordCount = 1 }
        }
        elseif ($path -eq '/emby/badkeys') {
            Send-Json $res @{ camelCaseKey = 'x'; Another = 'y' }
        }
        elseif ($path -eq '/emby/System/Ping') {
            $res.StatusCode = 200
            $bytes = [System.Text.Encoding]::UTF8.GetBytes('pong')
            $res.ContentLength64 = $bytes.Length
            $res.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        elseif ($path -eq '/web/index.html') {
            $range = $req.Headers['Range']
            $all = [System.Text.Encoding]::UTF8.GetBytes($html)
            if ($range -eq 'bytes=0-99') {
                $res.StatusCode = 206
                $res.ContentType = 'text/html'
                $res.AddHeader('Content-Range', "bytes 0-99/$($all.Length)")
                $res.ContentLength64 = 100
                $res.OutputStream.Write($all, 0, 100)
            }
            else {
                $res.StatusCode = 200
                $res.ContentType = 'text/html'
                $res.ContentLength64 = $all.Length
                $res.OutputStream.Write($all, 0, $all.Length)
            }
        }
        elseif ($path -eq '/embywebsocket') {
            # 注意：PS 5.1 会把 $null 转成空串传给 subProtocol（.NET 拒绝空串），
            # 故 mock 与 selftest 客户端约定子协议 'mocktest'（真实 Kestrel 服务器无子协议，不受影响）
            $wsCts = New-Object System.Threading.CancellationTokenSource(8000)
            $accTask = $ctx.AcceptWebSocketAsync('mocktest')
            if (-not $accTask.Wait(8000)) { throw 'ws accept timeout' }
            $wsCtx = $accTask.Result
            $ws = $wsCtx.WebSocket
            $buf = New-Object byte[] 16384
            $rcvTask = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $wsCts.Token)
            if (-not $rcvTask.Wait(8000)) { throw 'ws receive timeout' }
            $rcv = $rcvTask.Result
            $inText = [System.Text.Encoding]::UTF8.GetString($buf, 0, $rcv.Count)
            $reply = '{"MessageType":"Sessions","Data":[{"Id":"' + $id32 + '"}]}'
            $rb = [System.Text.Encoding]::UTF8.GetBytes($reply)
            [void]$ws.SendAsync([System.ArraySegment[byte]]::new($rb), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [System.Threading.CancellationToken]::None).Wait(5000)
            [void]$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [System.Threading.CancellationToken]::None).Wait(5000)
        }
        else {
            $res.StatusCode = 404
            $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"Message":"Not Found"}')
            $res.ContentType = 'application/json'
            $res.ContentLength64 = $bytes.Length
            $res.OutputStream.Write($bytes, 0, $bytes.Length)
        }
    }
    catch {
        try {
            $msg = $_.Exception.Message
            if ($_.Exception.InnerException) { $msg = $msg + ' | inner: ' + $_.Exception.InnerException.Message }
            Add-Content -LiteralPath 'D:\Dev\OpenMedia\tests\selftest\mock-error.log' -Value "[$(Get-Date -Format HH:mm:ss.fff)] $path $msg"
            $res.StatusCode = 500
        } catch {}
    }
    finally {
        try { $res.Close() } catch {}
    }
}
$listener.Stop()
Write-Host "mock-server stopped after $count requests"
