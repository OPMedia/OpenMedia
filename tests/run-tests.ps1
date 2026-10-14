# OpenMedia API 矩阵测试：按 test-plan.json 逐操作发请求，记录状态码/键名风格/关键字段，
# 输出 Markdown 矩阵（PASS/FAIL/SKIP-EXPECTED/SKIP-P2）+ 原始 JSON 结果。
# P2（写/破坏性）一律跳过。幂等可重复（服务器可重启多次）。
# PowerShell 5.1 兼容。用法：
#   powershell -ExecutionPolicy Bypass -File run-tests.ps1 [-BaseUrl http://localhost:8096]
param(
    [string]$BaseUrl = 'http://localhost:8096',
    [string]$PlanPath = '',
    [string]$OutDir = '',
    [string]$AdminUser = 'admin',
    [string]$AdminPw = 'admin',
    [int]$RequestTimeoutSec = 60
)
$ErrorActionPreference = 'Continue'
if (-not $PlanPath) { $PlanPath = Join-Path $PSScriptRoot 'test-plan.json' }
if (-not $OutDir)   { $OutDir = Join-Path $PSScriptRoot 'results' }
. (Join-Path $PSScriptRoot 'lib\common.ps1')

$ApiBase = "$BaseUrl/emby"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

function SnippetOf([string]$Text) {
    if (-not $Text) { return '' }
    $t = $Text.Trim() -replace '\s+', ' '
    if ($t.Length -gt 120) { $t = $t.Substring(0, 120) + '…' }
    return $t
}
function Escape-Md([string]$Text) {
    if (-not $Text) { return '' }
    return (($Text -replace '\|', '\|') -replace "`r`n", ' ' -replace "`n", ' ')
}

# ============ 1. 预检 ============
$client = New-EmbyClient -TimeoutSec $RequestTimeoutSec
Write-Host "[预检] GET $ApiBase/System/Info/Public ..."
$probe = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Info/Public"
if ($probe.Status -eq 0) {
    Write-Host "服务器不可达（$($probe.Error)）。请先启动服务器后再运行本脚本。" -ForegroundColor Red
    exit 1
}
Write-Host "[预检] 状态 $($probe.Status)，服务器可达。"

# ============ 2. 认证 ============
$token = $null
$authMode = '未认证'
$auth = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw $AdminPw
if ($auth.Status -eq 200) {
    $token = (ConvertFrom-JsonSafe $auth.Body).AccessToken
    $authMode = "已认证（$AdminUser/$AdminPw）"
} else {
    $auth2 = Invoke-AuthenticateByName -Client $client -ApiBase $ApiBase -Username $AdminUser -Pw ''
    if ($auth2.Status -eq 200) {
        $token = (ConvertFrom-JsonSafe $auth2.Body).AccessToken
        $authMode = "已认证（$AdminUser/空密码回退）"
    } else {
        $authMode = "认证失败（admin/admin→$($auth.Status)，admin/空→$($auth2.Status)），以未认证运行"
    }
}
$baseHeaders = @{ 'X-Emby-Authorization' = (Get-EmbyAuthHeader) }
if ($token) { $baseHeaders['X-Emby-Token'] = $token }
Write-Host "[认证] $authMode"

# ============ 3. 上下文（真实 Id 采集） ============
$ctx = @{
    UserId = ''; ItemId = ''; MovieId = ''; PluginId = ''; SessionId = ''
    DeviceId = 'openmedia-test-01'; LogName = ''; HasItems = $false
    HasRealItem = $false; Token = $token; AuthMode = $authMode
}
$rUsers = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Users" -Headers $baseHeaders
if ($rUsers.Status -eq 200) {
    $users = @(ConvertFrom-JsonSafe $rUsers.Body)
    $admin = @($users | Where-Object { $_.Name -eq $AdminUser }) | Select-Object -First 1
    if (-not $admin) { $admin = @($users | Select-Object -First 1) }
    if ($admin) { $ctx.UserId = "$($admin.Id)" }
}
$rItems = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Items?Recursive=true&Limit=50" -Headers $baseHeaders
if ($rItems.Status -eq 200) {
    $items = @((ConvertFrom-JsonSafe $rItems.Body).Items)
    if ($items.Count -gt 0) {
        $ctx.HasItems = $true
        $movie = @($items | Where-Object { $_.Type -eq 'Movie' }) | Select-Object -First 1
        $pick = $movie
        if (-not $pick) { $pick = @($items | Select-Object -First 1) }
        if ($pick) {
            $ctx.ItemId = "$($pick.Id)"
            $ctx.HasRealItem = $true
            if ($movie) { $ctx.MovieId = "$($movie.Id)" }
        }
    }
}
if (-not $ctx.HasRealItem) { $ctx.ItemId = '00000000000000000000000000000001' }  # 合成 Id（无真实条目时）
$rPlugins = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Plugins" -Headers $baseHeaders
if ($rPlugins.Status -eq 200) {
    $plugins = @(ConvertFrom-JsonSafe $rPlugins.Body)
    if ($plugins.Count -gt 0) { $ctx.PluginId = "$($plugins[0].Id)" }
}
$rSessions = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/Sessions" -Headers $baseHeaders
if ($rSessions.Status -eq 200) {
    $sessions = @(ConvertFrom-JsonSafe $rSessions.Body)
    $own = @($sessions | Where-Object { "$($_.DeviceId)" -eq $ctx.DeviceId }) | Select-Object -First 1
    if (-not $own) { $own = @($sessions | Select-Object -First 1) }
    if ($own) { $ctx.SessionId = "$($own.Id)" }
}
$rLogs = Invoke-EmbyRequest -Client $client -Method 'GET' -Url "$ApiBase/System/Logs" -Headers $baseHeaders
if ($rLogs.Status -eq 200) {
    $logs = @(ConvertFrom-JsonSafe $rLogs.Body)
    if ($logs.Count -gt 0 -and $logs[0].PSObject.Properties['Name']) { $ctx.LogName = "$($logs[0].Name)" }
}
Write-Host ("[上下文] UserId={0} ItemId={1}（真实={2}） PluginId={3} SessionId={4} LogName={5}" -f `
    $ctx.UserId, $ctx.ItemId, $ctx.HasRealItem, $ctx.PluginId, $ctx.SessionId, $ctx.LogName)

# ============ 4. 参数解析器 ============
$syntheticId = '11111111111111111111111111111111'
function Resolve-ParamValue {
    param([string]$Name, [string]$Path)
    switch ($Name) {
        'UserId'      { if ($ctx.UserId) { return $ctx.UserId } return $syntheticId }
        'Id' {
            if ($Path -cmatch '^/Users') { if ($ctx.UserId) { return $ctx.UserId } return $syntheticId }
            if ($Path -cmatch '^/Plugins') { if ($ctx.PluginId) { return $ctx.PluginId } return $syntheticId }
            if ($Path -cmatch '^/Sessions') { if ($ctx.SessionId) { return $ctx.SessionId } return $syntheticId }
            if ($Path -cmatch '^/Devices') { return $ctx.DeviceId }
            if ($ctx.ItemId) { return $ctx.ItemId }
            return $syntheticId
        }
        'ItemId'      { if ($ctx.ItemId) { return $ctx.ItemId } return $syntheticId }
        'Type'        { if ($Path -cmatch 'Images') { return 'Primary' } return 'General' }
        'Index'       { return '0' }
        'Name'        { return 'Movie1' }
        'Container'   { return 'mkv' }
        'DeviceId'    { return $ctx.DeviceId }
        'Format'      { return 'original' }
        'Tag'         { return '0' }
        'MaxWidth'    { return '200' }
        'MaxHeight'   { return '200' }
        'PercentPlayed' { return '0' }
        'UnplayedCount' { return '0' }
        'Version'     { return '4.1.1.0' }
        'Language'    { return 'en' }
        'LogName'     { if ($ctx.LogName) { return $ctx.LogName } return 'test' }
        'SortName'    { return 'Movie1' }
        'Command'     { return 'DisplayContent' }
        default       { return 'test' }
    }
}

function Build-OpUrl {
    param($Op)
    $path = $Op.path
    foreach ($p in @($Op.params)) {
        if ($p.in -eq 'path') {
            $val = Resolve-ParamValue -Name $p.name -Path $Op.path
            $path = $path.Replace('{' + $p.name + '}', [uri]::EscapeDataString($val))
        }
    }
    $query = ''
    foreach ($p in @($Op.params)) {
        if ($p.in -eq 'query' -and $p.required) {
            $val = Resolve-ParamValue -Name $p.name -Path $Op.path
            if ($query) { $query += '&' } else { $query = '?' }
            $query += $p.name + '=' + [uri]::EscapeDataString($val)
        }
    }
    # P0 精调参数
    if ($Op.id -eq 'GET /Items') {
        if (-not $query) { $query = '?' }
        if ($query -notmatch 'Recursive') { $query += 'Recursive=true&' }
        if ($query -notmatch 'Limit') { $query += 'Limit=4' }
        $query = $query.TrimEnd('&')
    }
    if ($Op.id -eq 'GET /Users/{UserId}/Items' -and -not $query) { $query = '?Limit=4' }
    $base = $ApiBase
    if ($Op.urlBase -eq 'root') { $base = $BaseUrl }
    return $base + $path + $query
}

# ============ 5. P0 断言 ============
function Assert-P0 {
    param($Op, $R)
    $n = @()
    $j = $null
    if ($R.ContentType -match 'json') { $j = ConvertFrom-JsonSafe $R.Body }

    if ($Op.id -eq 'GET /web/index.html') {
        if ($R.Status -ne 200) { return @{ verdict = 'FAIL'; notes = @("status=$($R.Status)") } }
        if ($R.ContentType -notmatch 'text/html') { $n += "ContentType=$($R.ContentType)（期望 text/html）" }
        if ($R.BodyBytes -lt 100) { $n += "body 仅 $($R.BodyBytes) 字节" }
        if ($n.Count) { return @{ verdict = 'FAIL'; notes = $n } }
        return @{ verdict = 'PASS'; notes = @("text/html, $($R.BodyBytes)B") }
    }
    if ($Op.id -eq 'GET /System/Ping') {
        if ($R.Status -ne 200) { return @{ verdict = 'FAIL'; notes = @("status=$($R.Status)") } }
        return @{ verdict = 'PASS'; notes = @() }
    }
    if ($Op.id -in @('GET /Items/{Id}/Images/{Type}', 'GET /Items/{Id}/Images/{Type}/{Index}',
                     'GET /Items/{Id}/Images/{Type}/{Index}/{Tag}/{Format}/{MaxWidth}/{MaxHeight}/{PercentPlayed}/{UnplayedCount}')) {
        if (-not $ctx.HasRealItem) { return @{ verdict = 'SKIP-NOFIXTURE'; notes = @('无真实条目（先运行 scenarios.ps1 建库）') } }
        if ($R.Status -eq 200 -and $R.ContentType -match '^image/') { return @{ verdict = 'PASS'; notes = @("$($R.ContentType), $($R.BodyBytes)B") } }
        if ($R.Status -eq 404) { return @{ verdict = 'FAIL'; notes = @('404：条目存在但无 Primary 图（poster.jpg 未被扫描拾取或图片服务异常）') } }
        return @{ verdict = 'FAIL'; notes = @("status=$($R.Status) $($R.ContentType)") }
    }
    if ($Op.id -eq 'GET /Items/{Id}/Images') {
        if (-not $ctx.HasRealItem) { return @{ verdict = 'SKIP-NOFIXTURE'; notes = @('无真实条目') } }
        if ($R.Status -ne 200) { return @{ verdict = 'FAIL'; notes = @("status=$($R.Status)") } }
        return @{ verdict = 'PASS'; notes = @() }
    }

    # 其余 P0：JSON 结构断言
    if ($R.Status -ne 200) { return @{ verdict = 'FAIL'; notes = @("status=$($R.Status) $($R.Reason)") } }
    if ($null -eq $j) { return @{ verdict = 'FAIL'; notes = @('响应非 JSON') } }
    $pv = @(Get-PascalViolations $j)
    if ($pv.Count) { $n += '非 PascalCase 键: ' + ($pv -join ',') }

    switch ($Op.id) {
        'GET /System/Info/Public' {
            if (-not $j.PSObject.Properties['Id']) { $n += '缺 Id' }
            elseif (-not (Test-EmbyIdFormat "$($j.Id)")) { $n += "Id 非 32 位无连字符 hex: $($j.Id)" }
            foreach ($f in @('ServerName', 'Version')) {
                if (-not $j.PSObject.Properties[$f]) { $n += "缺 $f" }
            }
        }
        'GET /Users' {
            if (-not ($j -is [System.Array])) { $n += '非数组' }
            elseif ($j.Count -gt 0) {
                if (-not $j[0].PSObject.Properties['Id']) { $n += '缺 Id' }
                elseif (-not (Test-EmbyIdFormat "$($j[0].Id)")) { $n += "Id 格式异常: $($j[0].Id)" }
                if (-not $j[0].PSObject.Properties['Name']) { $n += '缺 Name' }
            } else { $n += '空数组（应有 admin 用户）' }
        }
        'GET /Users/Public' {
            if (-not ($j -is [System.Array])) { $n += '非数组' }
        }
        { $_ -in @('GET /Items', 'GET /Users/{UserId}/Items') } {
            if (-not $j.PSObject.Properties['Items']) { $n += '缺 Items' }
            if (-not $j.PSObject.Properties['TotalRecordCount']) { $n += '缺 TotalRecordCount' }
        }
        'GET /Users/{UserId}/Views' {
            if ($j -is [System.Array]) { if ($j.Count -eq 0) { $n += '空视图数组' } }
            else {
                if (-not $j.PSObject.Properties['Items']) { $n += '缺 Items' }
                if (-not $j.PSObject.Properties['TotalRecordCount']) { $n += '缺 TotalRecordCount' }
            }
        }
        'POST /Items/{Id}/PlaybackInfo' {
            if (-not $ctx.HasRealItem) { return @{ verdict = 'SKIP-NOFIXTURE'; notes = @('无真实条目') } }
            if (-not $j.PSObject.Properties['MediaSources']) { $n += '缺 MediaSources' }
            else {
                $msCount = @($j.MediaSources).Count
                if ($msCount -eq 0) { $n += "MediaSources 为空（$($msCount) 个，伪媒体 ffprobe 失败属预期，需人工复核）" }
            }
        }
        'GET /Sessions' {
            if (-not ($j -is [System.Array])) { $n += '非数组' }
            elseif ($j.Count -eq 0) { $n += '空数组（认证会话应存在）' }
            elseif (-not $j[0].PSObject.Properties['Id']) { $n += '缺 Id' }
        }
    }
    if ($n.Count) { return @{ verdict = 'FAIL'; notes = $n } }
    return @{ verdict = 'PASS'; notes = @() }
}

# ============ 6. 执行 ============
$plan = Get-Content -Raw -LiteralPath $PlanPath | ConvertFrom-Json
$ops = @($plan.ops)
$exec = @($ops | Where-Object { $_.priority -in @('P0', 'P1') })
$skipP2 = @($ops | Where-Object { $_.priority -eq 'P2' })
Write-Host "[执行] 计划 $($ops.Count) 操作：执行 P0/P1 共 $($exec.Count) 个，跳过 P2 $($skipP2.Count) 个"

$results = [System.Collections.Generic.List[object]]::new()
$i = 0
foreach ($op in $exec) {
    $i++
    $url = Build-OpUrl -Op $op
    $bodyJson = ''
    if ($op.method -eq 'POST' -and $op.id -eq 'POST /Items/{Id}/PlaybackInfo' -and $ctx.UserId) {
        $bodyJson = @{ UserId = $ctx.UserId } | ConvertTo-Json -Depth 3
    }
    $r = Invoke-EmbyRequest -Client $client -Method $op.method -Url $url -Headers $baseHeaders -BodyJson $bodyJson

    $j = $null
    if ($r.ContentType -match 'json') { $j = ConvertFrom-JsonSafe $r.Body }
    $pv = @(Get-PascalViolations $j)
    $keys = ''
    if ($null -ne $j) { $keys = Get-TopKeys $j }

    # —— 判定 ——
    $verdict = ''
    $notes = @()
    if ($op.expected404) {
        if ($r.Status -eq 404) { $verdict = 'SKIP-EXPECTED'; $notes = @($op.expected404) }
        elseif ($r.Status -in @(200, 204)) { $verdict = 'PASS'; $notes = @("预期 404 但返回 $($r.Status)（已实现？核对清单）") }
        else { $verdict = 'FAIL'; $notes = @("预期 404（$($op.expected404)），实际 $($r.Status)") }
    }
    elseif ($op.priority -eq 'P0') {
        $a = Assert-P0 -Op $op -R $r
        $verdict = $a.verdict
        $notes = @($a.notes)
    }
    else {
        if ($r.Status -in @(200, 204)) { $verdict = 'PASS' }
        elseif ($r.Status -eq 405) { $verdict = 'PASS'; $notes = @('405（verb 未注册，spec 全 verb 展开属预期）') }
        elseif ($r.Status -eq 404) {
            if (-not $ctx.HasRealItem -and $op.path -cmatch '\{(Id|ItemId)\}') {
                $verdict = 'SKIP-NOFIXTURE'; $notes = @('404：路径 Id 为合成值（无真实条目，先跑 scenarios.ps1）')
            } else { $verdict = 'FAIL'; $notes = @('404：不在 B6 暂缓清单') }
        }
        elseif ($r.Status -eq 401) { $verdict = 'FAIL'; $notes = @("401：认证失败（$authMode）") }
        elseif ($r.Status -eq 400) { $verdict = 'FAIL'; $notes = @('400：' + (SnippetOf $r.Body)) }
        else { $verdict = 'FAIL'; $notes = @("$($r.Status) $($r.Reason)：$(if ($r.Error) { $r.Error } else { (SnippetOf $r.Body) })") }
    }
    if ($pv.Count -and $verdict -eq 'PASS' -and $op.priority -eq 'P1') { $notes += '键名风格: ' + ($pv -join ',') }
    if ($pv.Count -and $verdict -eq 'PASS' -and $op.priority -eq 'P0') { $notes += 'PascalCase 违规: ' + ($pv -join ',') }

    $results.Add([pscustomobject]@{
        op = $op.id; method = $op.method; path = $op.path; domain = $op.domain
        priority = $op.priority; source = $op.source; url = $url
        status = $r.Status; contentType = $r.ContentType; elapsedMs = $r.ElapsedMs
        bodyBytes = $r.BodyBytes; verdict = $verdict
        pascalViolations = $pv; topKeys = $keys
        notes = ($notes -join '; '); error = $r.Error
        bodySnippet = (SnippetOf $r.Body)
    })

    $mark = $verdict
    if ($verdict -eq 'PASS') { $mark = 'ok' }
    Write-Host ("  [{0}/{1}] {2,-8} {3} -> {4} {5}" -f $i, $exec.Count, $verdict, $op.id, $r.Status, $r.Reason)
}
$client.Dispose()

# ============ 7. 输出 ============
$p0r = @($results | Where-Object { $_.priority -eq 'P0' })
$p1r = @($results | Where-Object { $_.priority -eq 'P1' })
$cnt = @{
    pass0 = @($p0r | Where-Object { $_.verdict -eq 'PASS' }).Count
    fail0 = @($p0r | Where-Object { $_.verdict -eq 'FAIL' }).Count
    skip0 = @($p0r | Where-Object { $_.verdict -like 'SKIP*' }).Count
    pass1 = @($p1r | Where-Object { $_.verdict -eq 'PASS' }).Count
    fail1 = @($p1r | Where-Object { $_.verdict -eq 'FAIL' }).Count
    exp1  = @($p1r | Where-Object { $_.verdict -eq 'SKIP-EXPECTED' }).Count
    nofx1 = @($p1r | Where-Object { $_.verdict -eq 'SKIP-NOFIXTURE' }).Count
}
$execPass = $cnt.pass0 + $cnt.pass1
$execTotal = @($results).Count
$execFail = $cnt.fail0 + $cnt.fail1
$rate = if ($execTotal -gt 0) { [Math]::Round(100.0 * $execPass / ($execTotal - $cnt.exp1 - $cnt.nofx1 - $cnt.skip0), 1) } else { 0 }

# 原始 JSON
$rawJson = [pscustomobject]@{
    runAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    baseUrl = $BaseUrl; apiBase = $ApiBase; authMode = $authMode
    context = [pscustomobject]@{ UserId = $ctx.UserId; ItemId = $ctx.ItemId; HasRealItem = $ctx.HasRealItem; PluginId = $ctx.PluginId; SessionId = $ctx.SessionId }
    summary = [pscustomobject]@{
        planTotal = @($ops).Count; executed = $execTotal; skippedP2 = $skipP2.Count
        p0Pass = $cnt.pass0; p0Fail = $cnt.fail0; p0Skip = $cnt.skip0
        p1Pass = $cnt.pass1; p1Fail = $cnt.fail1; p1SkipExpected = $cnt.exp1; p1SkipNoFixture = $cnt.nofx1
        passRate = $rate
    }
    results = $results
}
Write-ResultFile -Path (Join-Path $OutDir "api-results-$stamp.json") -Content ($rawJson | ConvertTo-Json -Depth 8)
Write-ResultFile -Path (Join-Path $OutDir 'api-results-latest.json') -Content ($rawJson | ConvertTo-Json -Depth 8)

# Markdown 矩阵
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# OpenMedia API 矩阵测试报告')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("> 运行时间：$($rawJson.runAt)　基址：$ApiBase　认证：$authMode")
[void]$sb.AppendLine("> 上下文：UserId=$($ctx.UserId)　ItemId=$($ctx.ItemId)（真实=$($ctx.HasRealItem)）　SessionId=$($ctx.SessionId)")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 汇总')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 级别 | 执行 | PASS | FAIL | SKIP-EXPECTED | SKIP-NOFIXTURE |')
[void]$sb.AppendLine('|------|------|------|------|---------------|----------------|')
[void]$sb.AppendLine("| P0 | $($p0r.Count) | $($cnt.pass0) | $($cnt.fail0) | — | $($cnt.skip0) |")
[void]$sb.AppendLine("| P1 | $($p1r.Count) | $($cnt.pass1) | $($cnt.fail1) | $($cnt.exp1) | $($cnt.nofx1) |")
[void]$sb.AppendLine("| P2 | $($skipP2.Count) | — | — | — | 全部跳过（写/破坏性） |")
[void]$sb.AppendLine('')
[void]$sb.AppendLine("**通过率（PASS / (PASS+FAIL)）：$rate%**（$execPass / $($execPass + $execFail)）")
[void]$sb.AppendLine('')
if ($execFail -gt 0) {
    [void]$sb.AppendLine('### 失败清单')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| 级别 | 操作 | 状态码 | 备注 |')
    [void]$sb.AppendLine('|------|------|--------|------|')
    foreach ($res in @($results | Where-Object { $_.verdict -eq 'FAIL' })) {
        [void]$sb.AppendLine("| $($res.priority) | ``$($res.op)`` | $($res.status) | $(Escape-Md $res.notes) |")
    }
    [void]$sb.AppendLine('')
}
[void]$sb.AppendLine('## 明细矩阵（按域分组）')
[void]$sb.AppendLine('')
foreach ($d in ($results | Group-Object domain | Sort-Object Name)) {
    [void]$sb.AppendLine("### 域：$($d.Name)（$($d.Count)）")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |')
    [void]$sb.AppendLine('|------|------|--------|----------|----------|------|------|')
    foreach ($res in ($d.Group | Sort-Object op)) {
        $style = '—'
        if ($res.contentType -match 'json') {
            if (@($res.pascalViolations).Count -eq 0) { $style = 'PascalCase ✓' } else { $style = '✗ ' + (@($res.pascalViolations) -join ',') }
        }
        $keysCol = $res.topKeys
        if ($keysCol.Length -gt 60) { $keysCol = $keysCol.Substring(0, 60) + '…' }
        [void]$sb.AppendLine("| ``$($res.op)`` | $($res.priority) | $($res.status) | $style | $(Escape-Md $keysCol) | **$($res.verdict)** | $(Escape-Md $res.notes) |")
    }
    [void]$sb.AppendLine('')
}
[void]$sb.AppendLine('## P2 跳过清单（写/破坏性操作，未执行）')
[void]$sb.AppendLine('')
foreach ($g in ($skipP2 | Group-Object domain | Sort-Object Name)) {
    $ids = (@($g.Group) | ForEach-Object { "``$($_.id)``" }) -join '、'
    [void]$sb.AppendLine("- **$($g.Name)**（$($g.Count)）：$ids")
}
Write-ResultFile -Path (Join-Path $OutDir 'api-matrix.md') -Content $sb.ToString()

Write-Host ''
Write-Host '=== API 矩阵测试完成 ==='
Write-Host ("P0: {0} PASS / {1} FAIL / {2} SKIP" -f $cnt.pass0, $cnt.fail0, $cnt.skip0)
Write-Host ("P1: {0} PASS / {1} FAIL / {2} SKIP-EXPECTED / {3} SKIP-NOFIXTURE" -f $cnt.pass1, $cnt.fail1, $cnt.exp1, $cnt.nofx1)
Write-Host ("P2: {0} 跳过" -f $skipP2.Count)
Write-Host ("通过率: {0}%（{1}/{2}）" -f $rate, $execPass, ($execPass + $execFail))
Write-Host "输出: $OutDir\api-matrix.md / api-results-$stamp.json"
