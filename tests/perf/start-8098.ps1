# ============================================================
# 8098 验证实例标准启停脚本（10-16 U2 事故教训固化）
# 事故：重启 8098 漏带 OPENMEDIA_PGCONN → 实例连共享库 → scale 用户全 401，
#       测量无效。本脚本把环境变量 + 就绪门禁固化，杜绝人为遗漏。
# 端口：PG 默认 5432（-PgPort 可覆盖）；PG 端口不再是 15432。
param(
    [int]$PgPort = 5432,
    [switch]$Stop,
    [int]$TimeoutSec = 900
)
# 用法：
#   .\start-8098.ps1            # 启动并等到完全就绪（健康 + scale 用户认证全过才返回）
#   .\start-8098.ps1 -Stop      # 按端口占用者停止（群规：禁止按 CommandLine 匹配杀）
# 就绪门禁说明：1M 库启动回填期认证会 401（Users 集合未稳定，实测 5-15 分钟），
#               本脚本轮询 scale-user 认证直到 200——U2/soak 开跑前先跑这个。
# ============================================================
$ErrorActionPreference = 'Stop'

function Get-Listener {
    Get-NetTCPConnection -LocalPort 8098 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
}

if ($Stop) {
    $l = Get-Listener
    if (-not $l) { Write-Host '8098 未在监听'; return }
    $procId = $l.OwningProcess
    Write-Host "停止 8098（端口占用者 PID $procId）..."
    Stop-Process -Id $procId -Force
    Start-Sleep -Seconds 2
    if (Get-Listener) { throw '停止后端口仍被占用' } else { Write-Host '已停止' }
    return
}

# ---- 启动 ----
if (Get-Listener) { throw '8098 已在监听——先 .\start-8098.ps1 -Stop 再启动（防双实例）' }

$env:OPENMEDIA_PGCONN = "Host=localhost;Port=$PgPort;Database=openmedia_scale;Username=postgres;Password=123456"
Write-Host 'OPENMEDIA_PGCONN -> openmedia_scale 已设置'

Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList 'MediaBrowser.Server.dll' -WorkingDirectory 'D:\OpenMedia-8098' -WindowStyle Hidden
Write-Host '进程已拉起，等就绪门禁（健康 + scale 用户认证）...'

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$healthOk = $false
$authOk = $false
$authHdr = @{ 'X-Emby-Authorization' = 'MediaBrowser Client="start8098", Device="ops", DeviceId="start-8098-script", Version="4.1.1.0"' }
$authBody = '{"Username":"scale-user-00001","Pw":""}'

while ((Get-Date) -lt $deadline) {
    if (-not $healthOk) {
        try {
            $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8098/emby/system/info/public' -TimeoutSec 5 -UseBasicParsing
            if ($r.StatusCode -eq 200) { $healthOk = $true; Write-Host '健康 200 OK' }
        } catch { }
    }
    if ($healthOk -and -not $authOk) {
        try {
            $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8098/emby/Users/AuthenticateByName' -Method Post -Body $authBody -ContentType 'application/json' -Headers $authHdr -TimeoutSec 15 -UseBasicParsing
            if ($r.StatusCode -eq 200) { $authOk = $true; Write-Host 'scale-user 认证 200 OK（DB 绑定正确 + Users 就绪）' }
        } catch { Start-Sleep -Seconds 10 }
    }
    if ($healthOk -and $authOk) { break }
    Start-Sleep -Seconds 5
}

if (-not ($healthOk -and $authOk)) {
    throw "就绪门禁超时（${TimeoutSec}s）：health=$healthOk auth=$authOk —— auth 不过通常是 OPENMEDIA_PGCONN 绑错库或回填期过长"
}

$l = Get-Listener
Write-Host "8098 就绪：PID $($l.OwningProcess) —— U2/soak 可以开跑"
