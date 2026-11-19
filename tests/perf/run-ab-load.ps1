# ============================================================
# run-ab-load.ps1 —— Debug/Release 同口径 A/B 负载一键脚本
#
# 保证两侧口径完全一致：同一个库（openmedia_compare）、同一份数据、同一个用户、
# 同一个负载脚本、同一档位时长。唯一变量是构建配置。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tests\perf\run-ab-load.ps1 -Tag debug
#   powershell -ExecutionPolicy Bypass -File tests\perf\run-ab-load.ps1 -Tag release
# ============================================================
param(
    [ValidateSet('debug','release','ci-smoke')][string]$Tag,
    [string]$Db = 'openmedia_compare',
    [int]$PgPort = 5432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$User = 'ab-user-001',
    [int]$Seconds = 15,
    [int]$Port = 8096,
    [switch]$StopOnly
)
$ErrorActionPreference = 'Stop'
# Tag 在这里兼两个角色：选择构建配置 + 给结果文件打标签。
# ci-smoke 是 CI 冒烟档位 —— 它走 Release（CI 只构建 Release），
# 但结果文件名要留 ci-smoke，artifact 上传步骤按 ab-ci-smoke-*.json 匹配。
$cfg = if($Tag -in @('release','ci-smoke')){ 'Release' } else { 'Debug' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$work = Join-Path $repoRoot "MediaBrowser.Server\bin\$cfg\net10.0"
$work = (Resolve-Path $work).Path
$dll = Join-Path $work 'MediaBrowser.Server.dll'
if(-not (Test-Path $dll)){ throw "找不到 $dll —— 先 dotnet build MediaBrowser.sln -c $cfg" }

function Get-Listener {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
}

if($StopOnly){
    $l = Get-Listener
    if($l){ Stop-Process -Id $l.OwningProcess -Force; Start-Sleep -Seconds 2; Write-Host "已停止 $Port`（PID $($l.OwningProcess)）" }
    else { Write-Host "$Port 未在监听" }
    return
}

$l = Get-Listener
if($l){ throw "$Port 已被占用（PID $($l.OwningProcess)）——先 -StopOnly" }

$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$env:OPENMEDIA_PGCONN = "Host=localhost;Port=$PgPort;Database=$Db;Username=$PgUser;Password=$PgPassword"
Write-Host "启动 $cfg 实例（PORT $Port，DB $Db，USER $User）..."
$proc = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList 'MediaBrowser.Server.dll' -WorkingDirectory $work -WindowStyle Hidden -PassThru
Write-Host "PID $($proc.Id)"

# 就绪门禁：健康 + 认证
$deadline = (Get-Date).AddSeconds(180)
$ok = $false
while((Get-Date) -lt $deadline){
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/emby/system/info/public" -TimeoutSec 5 -UseBasicParsing
        if($r.StatusCode -eq 200){ $ok = $true; break }
    } catch { }
    Start-Sleep -Seconds 3
}
if(-not $ok){ try { Stop-Process -Id $proc.Id -Force } catch {}; throw "健康门禁超时" }
Write-Host "健康 200 OK"

# 暖机（避免首次 JIT/连接池污染双侧）
Push-Location $PSScriptRoot
try {
    python load-ab.py --tag "$Tag-warmup" --user $User --seconds 5 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "暖机失败（load-ab.py 退出码 $LASTEXITCODE）" }
} finally { Pop-Location }
Write-Host "暖机完成，开始正式测量"

Push-Location $PSScriptRoot
try {
    python load-ab.py --tag $Tag --user $User --seconds $Seconds
    # 原生命令不会让 PowerShell 抛错，必须自己看退出码 ——
    # 之前 python 崩了这一步照样显示成功，结果文件根本没生成。
    if ($LASTEXITCODE -ne 0) { throw "load-ab.py 退出码 $LASTEXITCODE（性能数字未产出）" }
} finally { Pop-Location }

Write-Host ""
Write-Host "停止 $Tag 实例（PID $($proc.Id)）..."
Stop-Process -Id $proc.Id -Force
Start-Sleep -Seconds 2
Write-Host "完成：tests\perf\results\ab-$Tag-latest.json"
