# OpenMedia JSON 契约抽查：/System/Info/Public（Id 32 位无连字符 hex + PascalCase）、
# /Items（Type 枚举字符串 + 无 null 字段 + PascalCase）、/Users、/System/Info、/Sessions。
# 输出 tests/results/contract.md + contract-results-latest.json。幂等可重复。
# PowerShell 5.1 兼容。用法：
#   powershell -ExecutionPolicy Bypass -File contract-check.ps1 [-BaseUrl http://localhost:8096]
param(
    [string]$BaseUrl = 'http://localhost:8096',
    [string]$OutDir = '',
    [string]$AdminUser = 'admin',
    [string]$AdminPw = 'admin'
)
$ErrorActionPreference = 'Continue'
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'results' }
. (Join-Path $PSScriptRoot 'lib\common.ps1')

$ApiBase = "$BaseUrl/emby"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check {
    param([string]$Endpoint, [string]$Rule, [string]$Result, [string]$Status, [string]$Detail = '')
    $checks.Add([pscustomobject]@{ endpoint = $Endpoint; rule = $Rule; result = $Result; status = $Status; detail = $Detail })
    $mark = ''
    if ($Status -eq 'PASS') { $mark = 'OK' } elseif ($Status -eq 'FAIL') { $mark = '!!' } else { $mark = '--' }
    Write-Host ("  [{0}] {1} / {2}: {3}" -f $mark, $Endpoint, $Rule, $Status)
}

# 已知 BaseItemDto Type 枚举值（抽查用，非穷举）
$knownTypes = @('Movie', 'Series', 'Season', 'Episode', 'Folder', 'CollectionFolder', 'UserView', 'AggregateFolder',
                'MusicAlbum', 'MusicArtist', 'MusicGenre', 'Audio', 'Video', 'Person', 'Genre', 'Studio', 'Year',
                'Channel', 'Program', 'Recording', 'BoxSet', 'Playlist', 'PlaylistsFolder', 'Trailer', 'Game',
                'GameSystem', 'Book', 'AudioBook', 'MusicVideo', 'LiveTvChannel', 'LiveTvProgram', 'Photo', 'PhotoAlbum')

function Get-NullProps {
    param($Obj)
    $nulls = @()
    if ($null -eq $Obj) { return $nulls }
    if ($Obj -is [System.Array]) {
        foreach ($el in $Obj) { $nulls += @(Get-NullProps $el) }
        return $nulls
    }
    if ($Obj -is [System.Management.Automation.PSCustomObject]) {
        foreach ($p in $Obj.PSObject.Properties) {
            if ($null -eq $p.Value) { $nulls += $p.Name }
        }
    }
    return $nulls
}

# ============ 预检 + 认证 ============
$client = New-EmbyClient -TimeoutSec 60
$probe = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Info/Public"
if ($probe.Status -eq 0) {
    Write-Host "服务器不可达（$($probe.Error)）。请先启动服务器。" -ForegroundColor Red
    exit 1
}

$token = $null
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw $AdminPw
if ($auth.Status -eq 200) { $token = (ConvertFrom-JsonSafe $auth.Body).AccessToken }
else {
    $auth2 = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw ''
    if ($auth2.Status -eq 200) { $token = (ConvertFrom-JsonSafe $auth2.Body).AccessToken }
}
$hdrs = @{ 'X-Emby-Authorization' = (Get-EmbyAuthHeader) }
if ($token) { $hdrs['X-Emby-Token'] = $token }

# ============ 1. /System/Info/Public ============
Write-Host "`n=== /System/Info/Public 契约 ===" -ForegroundColor Cyan
$r = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Info/Public"
$j = ConvertFrom-JsonSafe $r.Body
if ($r.Status -ne 200 -or $null -eq $j) {
    Add-Check '/System/Info/Public' 'HTTP 200 + JSON' "status=$($r.Status)" 'FAIL'
}
else {
    Add-Check '/System/Info/Public' 'HTTP 200 + JSON' 'status=200' 'PASS'
    # 键名 PascalCase
    $pv = @(Get-PascalViolations $j)
    Add-Check '/System/Info/Public' '键名 PascalCase' $(if ($pv.Count) { "违规: $($pv -join ',')" } else { "全部 PascalCase（$(Get-TopKeys $j)）" }) `
        $(if ($pv.Count) { 'FAIL' } else { 'PASS' }) -Notes ''
    # Id：32 位无连字符 hex
    $id = "$($j.Id)"
    $idOk = (Test-EmbyIdFormat $id) -and ($id -notmatch '-')
    Add-Check '/System/Info/Public' 'Id = 32 位无连字符 hex（Guid N 格式）' "Id=$id" $(if ($idOk) { 'PASS' } else { 'FAIL' }) `
        $(if (-not $idOk) { "格式不符（长度 $($id.Length)，含连字符=$($id -match '-'))" })
    # 关键字段
    foreach ($f in @('ServerName', 'Version', 'OperatingSystem')) {
        $has = [bool]$j.PSObject.Properties[$f]
        Add-Check '/System/Info/Public' "关键字段 $f" $(if ($has) { "存在（$($j.$f)）" } else { '缺失' }) $(if ($has) { 'PASS' } else { 'FAIL' })
    }
}

# ============ 2. /Users ============
Write-Host "`n=== /Users 契约 ===" -ForegroundColor Cyan
$rU = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Users" -Headers $hdrs
$ju = ConvertFrom-JsonSafe $rU.Body
if ($rU.Status -eq 200 -and $ju -is [System.Array] -and $ju.Count -gt 0) {
    Add-Check '/Users' 'HTTP 200 + 数组' "status=200 count=$($ju.Count)" 'PASS'
    $pvU = @(Get-PascalViolations $ju)
    Add-Check '/Users' '键名 PascalCase（首元素）' $(if ($pvU.Count) { "违规: $($pvU -join ',')" } else { '全部 PascalCase' }) $(if ($pvU.Count) { 'FAIL' } else { 'PASS' })
    $uid = "$($ju[0].Id)"
    Add-Check '/Users' 'User.Id = Guid N 格式（32 hex）' "Id=$uid" $(if (Test-EmbyIdFormat $uid) { 'PASS' } else { 'FAIL' })
    $hasName = [bool]$ju[0].PSObject.Properties['Name']
    Add-Check '/Users' '关键字段 Name' $(if ($hasName) { "存在（$($ju[0].Name)）" } else { '缺失' }) $(if ($hasName) { 'PASS' } else { 'FAIL' })
}
else {
    Add-Check '/Users' 'HTTP 200 + 数组' "status=$($rU.Status)$(if ($token) { '' } else { '（未认证）' })" 'FAIL'
    if (-not $token) { Add-Check '/Users' '认证' 'token 获取失败，跳过后续需认证契约检查' 'SKIP' }
}

# ============ 3. /Items ============
Write-Host "`n=== /Items 契约 ===" -ForegroundColor Cyan
$rI = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Items?Recursive=true&Limit=20" -Headers $hdrs
$ji = ConvertFrom-JsonSafe $rI.Body
if ($rI.Status -eq 200 -and $null -ne $ji -and $ji.PSObject.Properties['Items']) {
    $items = @($ji.Items)
    Add-Check '/Items' 'HTTP 200 + QueryResult{Items,TotalRecordCount}' "Items=$($items.Count) TotalRecordCount=$($ji.TotalRecordCount)" 'PASS'
    if ($items.Count -gt 0) {
        # Type 枚举字符串
        $badType = @()
        foreach ($it in $items) {
            $t = $it.PSObject.Properties['Type']
            if (-not $t) { $badType += "$($it.Name):无Type"; continue }
            $tv = "$($t.Value)"
            if ($tv -match '^\d+$') { $badType += "$($it.Name):数字($tv)" }
            elseif ($tv -notmatch '^[A-Z][A-Za-z]*$') { $badType += "$($it.Name):$tv" }
            elseif ($knownTypes -notcontains $tv) { $badType += "$($it.Name):未知枚举($tv)" }
        }
        $typeSummary = (@($items | Select-Object -First 5 | ForEach-Object { $_.Type }) -join ',')
        Add-Check '/Items' 'Type 为枚举字符串（非数字）' "前 5 个 Type: $typeSummary" `
            $(if ($badType.Count) { 'FAIL' } else { 'PASS' }) $(if ($badType.Count) { ($badType -join '; ') })
        # 无 null 字段（json-migration：null 省略）
        $nulls = @(Get-NullProps $items)
        Add-Check '/Items' '无 null 值字段（序列化应省略 null）' $(if ($nulls.Count) { "null 字段: $($nulls -join ',')" } else { '无 null 字段' }) `
            $(if ($nulls.Count) { 'FAIL' } else { 'PASS' })
        # 键名 PascalCase
        $pvI = @(Get-PascalViolations $items)
        Add-Check '/Items' '键名 PascalCase（首元素）' $(if ($pvI.Count) { "违规: $($pvI -join ',')" } else { '全部 PascalCase' }) $(if ($pvI.Count) { 'FAIL' } else { 'PASS' })
        # 关键字段
        $first = $items[0]
        foreach ($f in @('Id', 'Name', 'Type')) {
            $has = [bool]$first.PSObject.Properties[$f]
            Add-Check '/Items' "关键字段 $f" $(if ($has) { "存在" } else { '缺失' }) $(if ($has) { 'PASS' } else { 'FAIL' })
        }
        $iidOk = $true
        foreach ($it in $items) { if (-not (Test-EmbyIdFormat "$($it.Id)")) { $iidOk = $false; break } }
        Add-Check '/Items' 'Item.Id = Guid N 格式（32 hex）' $(if ($iidOk) { '全部合规' } else { "首个不合规: $($first.Id)" }) $(if ($iidOk) { 'PASS' } else { 'FAIL' })
    }
    else {
        Add-Check '/Items' '条目存在性' 'Items 为空（媒体库未扫描？先运行 scenarios.ps1）' 'SKIP'
    }
}
else {
    Add-Check '/Items' 'HTTP 200 + QueryResult' "status=$($rI.Status)" 'FAIL'
}

# ============ 4. /System/Info（认证） ============
Write-Host "`n=== /System/Info 契约 ===" -ForegroundColor Cyan
$rS = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Info" -Headers $hdrs
$js = ConvertFrom-JsonSafe $rS.Body
if ($rS.Status -eq 200 -and $null -ne $js) {
    Add-Check '/System/Info' 'HTTP 200 + JSON' 'status=200' 'PASS'
    $pvS = @(Get-PascalViolations $js)
    Add-Check '/System/Info' '键名 PascalCase' $(if ($pvS.Count) { "违规: $($pvS -join ',')" } else { '全部 PascalCase' }) $(if ($pvS.Count) { 'FAIL' } else { 'PASS' })
    $sid = "$($js.Id)"
    Add-Check '/System/Info' 'Id = Guid N 格式' "Id=$sid" $(if (Test-EmbyIdFormat $sid) { 'PASS' } else { 'FAIL' })
}
else {
    Add-Check '/System/Info' 'HTTP 200 + JSON' "status=$($rS.Status)" 'FAIL'
}
$client.Dispose()

# ============ 输出 ============
$pass = @($checks | Where-Object { $_.status -eq 'PASS' }).Count
$fail = @($checks | Where-Object { $_.status -eq 'FAIL' }).Count
$skip = @($checks | Where-Object { $_.status -eq 'SKIP' }).Count

$raw = [pscustomobject]@{
    runAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    baseUrl = $BaseUrl; apiBase = $ApiBase
    summary = [pscustomobject]@{ pass = $pass; fail = $fail; skip = $skip; total = @($checks).Count }
    checks = $checks
}
Write-ResultFile -Path (Join-Path $OutDir 'contract-results-latest.json') -Content ($raw | ConvertTo-Json -Depth 6)

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# OpenMedia JSON 契约抽查报告')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("> 运行时间：$($raw.runAt)　基址：$ApiBase")
[void]$sb.AppendLine('')
[void]$sb.AppendLine("**结果：PASS $pass / FAIL $fail / SKIP $skip（共 $(@($checks).Count)）**")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 端点 | 契约规则 | 实际 | 结果 | 详情 |')
[void]$sb.AppendLine('|------|----------|------|------|------|')
foreach ($c in $checks) {
    $detail = $c.detail
    if (-not $detail) { $detail = '' }
    $detail = (($detail -replace '\|', '\|') -replace "`r`n", ' ' -replace "`n", ' ')
    $result = (($c.result -replace '\|', '\|') -replace "`r`n", ' ')
    [void]$sb.AppendLine("| ``$($c.endpoint)`` | $($c.rule) | $result | **$($c.status)** | $detail |")
}
Write-ResultFile -Path (Join-Path $OutDir 'contract.md') -Content $sb.ToString()

Write-Host ''
Write-Host '=== JSON 契约抽查完成 ==='
Write-Host ("PASS {0} / FAIL {1} / SKIP {2}" -f $pass, $fail, $skip)
Write-Host "输出: $OutDir\contract.md / contract-results-latest.json"
