# OpenMedia 按名实体/用户图片 fixture（幂等）：给矩阵 ⑤ 类图片端点补 Primary 图
# 覆盖端点（run-tests.ps1 以 {Name}=<实体名> 打）：
#   /Artists|/Persons|/Genres|/MusicGenres|/GameGenres|/Studios /<实体名>/Images/Primary[/{Index}]
#   /Users/{adminId}/Images/Primary[/{Index}]
# 方式：POST /Items/{Id}/Images/Primary（body=Base64 图片串）——按名实体无按名 POST 路由，
#       先 GET /{Endpoint}/{Name} 解析 Id 再走通用条目路由；用户走 /Users/{Id}/Images。
# 图片：System.Drawing 生成 64x64 JPEG（对齐 setup-media.ps1 兜底模式）。
# 实体名：默认按端点从库里取第一个实体（与 run-tests.ps1 的 Get-FirstItem 同口径）——
#       旧实现硬编码 'Movie1'，只有 MusicGenres 恰好叫 Movie1，其余端点全部对不上
#       （2026-10-20 全量回归暴露）。
# PowerShell 5.1 兼容。
param(
    [string]$ApiBase = "http://127.0.0.1:8096/emby",
    [string]$AdminUser = 'admin',
    [string]$AdminPw = '',
    [string]$EntityName = ''
)
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\common.ps1')
$client = New-EmbyClient

# ---------- 认证 ----------
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw $AdminPw
$authJson = ConvertFrom-JsonSafe $auth.Body
if (-not ($auth.Status -eq 200 -and $authJson -and $authJson.AccessToken)) { throw "认证失败: $($auth.Status)" }
$token = $authJson.AccessToken
$userId = "$($authJson.User.Id)"
$hdrs = @{ 'X-Emby-Token' = $token }
Write-Host "认证 OK（userId=$userId）"

# ---------- 生成测试图（幂等：文件已存在则复用）----------
$imgPath = Join-Path $PSScriptRoot 'livetv\entityimg.jpg'
if (-not (Test-Path -LiteralPath $imgPath)) {
    Add-Type -AssemblyName System.Drawing | Out-Null
    $bmp = New-Object System.Drawing.Bitmap(64, 64)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.Clear([System.Drawing.Color]::SteelBlue)
        $bmp.Save($imgPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    }
    finally { $g.Dispose(); $bmp.Dispose() }
}
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($imgPath))
Write-Host "测试图: $imgPath ($($b64.Length) B64 chars)"

# 原生 POST：body=Base64 串、Content-Type=image/jpeg（服务端按 mime 推导图片格式；
# common.ps1 的 BodyJson 走 application/json 会被拒——"Unable to determine image file
# extension from mime type application/json"）
function Post-ImageBase64 {
    param([string]$Url, [string]$Token, [string]$Base64)
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'POST'
    $req.ContentType = 'image/jpeg'
    $req.Headers.Add('X-Emby-Token', $Token)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Base64)
    $req.ContentLength = $bytes.Length
    $s = $req.GetRequestStream()
    try { $s.Write($bytes, 0, $bytes.Length) } finally { $s.Close() }
    try {
        $resp = $req.GetResponse()
        $code = [int]$resp.StatusCode
        $resp.Close()
        return $code
    } catch [System.Net.WebException] {
        $code = [int]$_.Exception.Response.StatusCode
        $_.Exception.Response.Close()
        return $code
    }
}

# ---------- 按名实体：解析 Id → 已有图则跳过，否则 POST ----------
$endpoints = @('Artists', 'Persons', 'Genres', 'MusicGenres', 'GameGenres', 'Studios')

# 取实体名：显式 -EntityName 则全局用它；否则按端点从库里取第一个实体
# （与 run-tests.ps1 的 Get-FirstItem 同口径，否则补的图和矩阵打的实体对不上）。
function Get-FirstEntityName {
    param([string]$Endpoint)
    $r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/$Endpoint`?Limit=1" -Headers $hdrs
    if ($r.Status -ne 200) { return '' }
    $j = ConvertFrom-JsonSafe $r.Body
    $items = @($j.Items)
    if ($items.Count -eq 0) { return '' }
    return "$($items[0].Name)"
}

$entityNames = @{}
foreach ($ep in $endpoints) {
    if ($EntityName) { $entityNames[$ep] = $EntityName }
    else { $entityNames[$ep] = Get-FirstEntityName -Endpoint $ep }
}

$entityIds = @{}
foreach ($ep in $endpoints) {
    $name = $entityNames[$ep]
    if (-not $name) { Write-Host "[$ep] 库里无实体（fixture 缺失，无法补图），跳过"; continue }
    $r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/$ep/$([uri]::EscapeDataString($name))" -Headers $hdrs
    if ($r.Status -ne 200) { Write-Host "[$ep] 解析失败 $($r.Status)（实体名=$name），跳过"; continue }
    $j = ConvertFrom-JsonSafe $r.Body
    $id = "$($j.Id)"
    $entityIds[$ep] = $id

    # 幂等检查：已有 Primary 则跳过
    $g = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/$ep/$([uri]::EscapeDataString($name))/Images/Primary" -Headers $hdrs
    if ($g.Status -eq 200) { Write-Host "[$ep] 已有 Primary 图，跳过（实体名=$name）"; continue }

    $code = Post-ImageBase64 -Url "$ApiBase/Items/$id/Images/Primary" -Token $token -Base64 $b64
    if ($code -notin @(200, 204)) { Write-Host "[$ep] 上传失败 $code"; continue }
    Write-Host "[$ep] Primary 图已上传（实体名=$name Id=$id）"
}

# ---------- 用户图片：POST /Users/{Id}/Images/Primary ----------
$g = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Users/$userId/Images/Primary" -Headers $hdrs
if ($g.Status -eq 200) {
    Write-Host "[User] 已有 Primary 图，跳过"
} else {
    $code = Post-ImageBase64 -Url "$ApiBase/Users/$userId/Images/Primary" -Token $token -Base64 $b64
    if ($code -in @(200, 204)) { Write-Host "[User] Primary 图已上传" }
    else { Write-Host "[User] 上传失败 $code" }
}

# ---------- 验收断言：12 个矩阵点全绿（实体缺失的端点记 SKIP，不计失败）----------
$fail = 0
$skip = 0
foreach ($ep in $endpoints) {
    $name = $entityNames[$ep]
    if (-not $name) {
        $skip += 2
        Write-Host ("  GET {0}/<无实体>{1}: SKIP（库里无该类实体）" -f $ep, '/Images/Primary')
        continue
    }
    foreach ($suffix in @('/Images/Primary', '/Images/Primary/0')) {
        $r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/$ep/$([uri]::EscapeDataString($name))$suffix" -Headers $hdrs
        $ok = ($r.Status -eq 200 -and $r.ContentType -match '^image/')
        if (-not $ok) { $fail++ }
        Write-Host ("  GET {0}/{1}{2}: {3} {4}{5}" -f $ep, $name, $suffix, $r.Status, $r.ContentType, $(if ($ok) { ' [PASS]' } else { ' [FAIL] ' + $r.Body.Substring(0, [Math]::Min(60, $r.Body.Length)) }))
    }
}
foreach ($suffix in @('/Images/Primary', '/Images/Primary/0')) {
    $r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Users/$userId$suffix" -Headers $hdrs
    $ok = ($r.Status -eq 200 -and $r.ContentType -match '^image/')
    if (-not $ok) { $fail++ }
    Write-Host ("  GET Users/{0}{1}: {2} {3}{4}" -f $userId, $suffix, $r.Status, $r.ContentType, $(if ($ok) { ' [PASS]' } else { ' [FAIL]' }))
}

if ($fail -gt 0) { throw "$fail 个图片端点未转绿" }
Write-Host ''
Write-Host "图片端点全绿（SKIP $skip 个：库里无该类实体）。"
