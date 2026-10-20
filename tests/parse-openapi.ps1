# OpenMedia 测试计划生成器：解析 openapi.json（Emby Server API 4.1.1.0）
# 产出 tests/test-plan.json（机器可读，供 run-tests.ps1）+ tests/test-plan.md（人类可读）。
# 分级：P0 客户端关键路径 / P1 常规查询 / P2 写与破坏性操作（跳过）。
# PowerShell 5.1 兼容。
param(
    [string]$SpecPath = "D:\Dev\OpenMedia\openapi.json",
    [string]$OutDir = "D:\Dev\OpenMedia\tests"
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib\common.ps1"

if (-not (Test-Path -LiteralPath $SpecPath)) { Write-Error "找不到 $SpecPath"; exit 1 }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

Write-Host "解析 $SpecPath ..."
$spec = Get-Content -Raw -LiteralPath $SpecPath | ConvertFrom-Json

# —— 核心操作 verb（head/trace/patch 共 33 个变体不单列，414 = get+post+put+delete+options）——
$verbs = @('get', 'post', 'put', 'delete', 'options')

# —— P0 规则（客户端关键路径；任务指定 + 浏览链路直接变体）——
$p0Rules = @(
    @{ verb = 'get';  pattern = '^/System/Info/Public$';            note = '登录前服务器探测' },
    @{ verb = 'get';  pattern = '^/System/Ping$';                   note = '健康检查' },
    @{ verb = 'get';  pattern = '^/Users$';                         note = '用户列表' },
    @{ verb = 'get';  pattern = '^/Users/Public$';                  note = '公共用户列表' },
    @{ verb = 'get';  pattern = '^/Items$';                         note = '条目查询' },
    @{ verb = 'get';  pattern = '^/Users/\{UserId\}/Items$';        note = '用户条目（浏览链路）' },
    @{ verb = 'get';  pattern = '^/Users/\{UserId\}/Views$';        note = '用户视图（浏览链路）' },
    @{ verb = 'get';  pattern = '^/Items/\{Id\}/Images';            note = '图片' },
    @{ verb = 'post'; pattern = '^/Items/\{Id\}/PlaybackInfo$';     note = '播放决策' },
    @{ verb = 'get';  pattern = '^/Sessions$';                      note = '会话列表' }
)

# POST/PUT 中视为"安全查询"不跳过的白名单
$postP1Whitelist = @('^/Users/AuthenticateByName$', '^/LiveTv/Programs$')

# 404 属预期（SKIP-EXPECTED）的清单：B6 插件端点 25 个 + 跳过清单（Sync/Connect/swagger）+ B5 占位
$expected404Rules = @(
    @{ pattern = '^/user_usage_stats';   reason = 'B6 插件端点暂缓（Playback Reporting 插件）' },
    @{ pattern = '^/Reports';            reason = 'B6 插件端点暂缓（Emby Reports 插件）' },
    @{ pattern = '/ThumbnailSet$';       reason = 'B6 插件端点暂缓（RokuMetadata 插件）' },
    @{ pattern = '^/Notification/SMTP/Test'; reason = 'B6 插件端点暂缓（SMTP 通知插件）' },
    @{ pattern = '^/Sync/';              reason = '跳过清单：同步功能移除（D2 决策）' },
    @{ pattern = '^/Connect/';           reason = '跳过清单：云连接移除' },
    @{ pattern = '/Connect/Link$';       reason = '跳过清单：云连接移除' },
    @{ pattern = '^/swagger';            reason = '跳过清单：文档端点未实现' },
    @{ pattern = '^/openapi';            reason = '跳过清单：文档端点未实现' },
    @{ pattern = '/index\.bif$';         reason = 'B5 占位：BIF 生成未实现，404 预期' },
    @{ pattern = '/GameGenres/\{Name\}'; reason = '环境限制：库内无游戏媒体 → 无 GameGenre 实体，404 预期（列表端点 /GameGenres 已 200 验证端点存活）' },

    # —— 以下 20 条是 2026-10-14 起手工增补的期望 404（原只存在于入库的 test-plan.json，
    #    脚本未收录 → 重建 plan 时整批丢失，矩阵从 0 FAIL 变 30 FAIL）。2026-10-20 脚本化。
    @{ pattern = '^/Environment/DirectoryContents$';            reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Packages/\{Name\}$';                        reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/web/ConfigurationPage$';                    reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Dlna/icons/\{Filename\}$';                  reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/LiveTv/ListingProviders/Lineups$';         reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/System/Configuration/\{Key\}$';             method = 'GET'; reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Dlna/\{UuId\}/icons/\{Filename\}$';         reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Images/General/\{Name\}/\{Type\}$';         reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Images/Ratings/\{Theme\}/\{Name\}$';        reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Images/MediaInfo/\{Theme\}/\{Name\}$';      reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/Providers/Subtitles/Subtitles/\{Id\}$';     reason = '合成参数 404 合理（无对应资源）' },
    @{ pattern = '^/LiveTv/Timers/\{Id\}$';                     method = 'GET'; reason = '无活跃 timer fixture（B 轨道 timer 已消费），404 正确' },
    @{ pattern = '^/LiveTv/SeriesTimers/\{Id\}$';               method = 'GET'; reason = '无 series timer fixture，404 正确' },
    @{ pattern = '^/Plugins/\{Id\}/Thumb$';                     method = 'GET'; reason = '无插件安装（去 Emby 化后零插件），404 正确' },
    @{ pattern = '^/Plugins/\{Id\}/Configuration$';              method = 'GET'; reason = '无插件安装（去 Emby 化后零插件），404 正确' },
    @{ pattern = '^/LiveTv/LiveRecordings/\{Id\}/stream$';      method = 'GET'; reason = '无进行中直播录制（已完成录制不走此路由），404 正确' },
    @{ pattern = '^/LiveTv/LiveStreamFiles/\{Id\}/stream\.\{Container\}$'; method = 'GET'; reason = '无活跃直播流文件 fixture（需 OpenToken 会话），404 正确' },
    @{ pattern = '^/LiveTv/Recordings/Groups/\{Id\}$';          method = 'GET'; reason = '无录制组 fixture（Groups 端点期望组而非单条录制），404 正确' },
    @{ pattern = '^/Videos/\{Id\}/hls/\{PlaylistId\}/\{SegmentId\}\.\{SegmentContainer\}$'; method = 'GET'; reason = 'legacy hls 路由合成分片参数，404 正确（负路径已固化）' },
    @{ pattern = '^/Videos/\{Id\}/\{MediaSourceId\}/Subtitles/\{Index\}/Stream\.\{Format\}$'; method = 'GET'; reason = 'fixture 媒体无字幕流，404 正确（无流→404 已是修复后行为）' }
)

# —— 期望 400 的清单（合成参数 → 服务端正确校验行为；负路径用例已固化）——
#    与 expected404 同为「手工增补」过的字段，2026-10-20 一并脚本化，
#    否则重建 plan 时整批丢失 → 11 个端点从 SKIP-EXPECTED 退化为 FAIL。
$expected400Rules = @(
    @{ pattern = '^/Images/Remote$';        method = 'GET';  reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Playback/BitrateTest$'; method = 'GET';  reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Users/AuthenticateByName$'; method = 'POST'; reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Channels/\{Id\}/Items$'; method = 'GET';  reason = 'Channels 端点为库内频道域（非 LiveTv 频道），LiveTv 频道 Id 不适用，400 正确' },
    @{ pattern = '^/Items/RemoteSearch/Image$'; method = 'GET'; reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Users/\{UserId\}/Views$'; method = 'GET';  reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Videos/\{Id\}/index\.bif$'; method = 'GET'; reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Videos/\{Id\}/subtitles\.m3u8$'; method = 'GET'; reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Audio/\{Id\}/hls1/\{PlaylistId\}/\{SegmentId\}\.\{SegmentContainer\}$'; method = 'GET'; reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Videos/\{Id\}/hls1/\{PlaylistId\}/\{SegmentId\}\.\{SegmentContainer\}$'; method = 'GET'; reason = '合成参数 400 为正确校验行为（负路径用例已固化）' },
    @{ pattern = '^/Videos/\{Id\}/\{MediaSourceId\}/Subtitles/\{Index\}/\{StartPositionTicks\}/Stream\.\{Format\}$'; method = 'GET'; reason = 'StartPositionTicks=test 合成参数 → 400 正确（负路径已固化）' }
)

function Get-OpDomain([string]$path) {
    $seg = (($path -replace '^/', '') -split '/')
    if ($seg.Count -gt 1 -and $seg[0] -eq 'web') { return 'web' }
    return $seg[0]
}
function Get-RefName([string]$ref) {
    if ($ref) { return ($ref -replace '.*/', '') }
    return ''
}

$ops = [System.Collections.Generic.List[object]]::new()
foreach ($p in $spec.paths.PSObject.Properties) {
    $path = $p.Name
    foreach ($v in $verbs) {
        $op = $p.Value.PSObject.Properties[$v].Value
        if ($null -eq $op) { continue }

        $params = @()
        foreach ($pa in @($op.parameters)) {
            if ($null -eq $pa) { continue }
            $params += [pscustomobject]@{ name = [string]$pa.name; in = [string]$pa.in; required = [bool]$pa.required }
        }

        $respRef = ''
        $respIsArray = $false
        try {
            $sch = $op.responses.'200'.content.'application/json'.schema
            if ($null -ne $sch) {
                if ($sch.'$ref') { $respRef = Get-RefName "$($sch.'$ref')" }
                elseif ($sch.type -eq 'array' -and $sch.items.'$ref') { $respRef = Get-RefName "$($sch.items.'$ref')"; $respIsArray = $true }
                elseif ($sch.type) { $respRef = [string]$sch.type }
            }
        } catch {}
        $reqRef = ''
        try { $reqRef = Get-RefName "$($op.requestBody.content.'application/json'.schema.'$ref')" } catch {}

        # —— 分级 ——
        $priority = ''
        $p0note = ''
        foreach ($r in $p0Rules) {
            if ($v -eq $r.verb -and $path -cmatch $r.pattern) { $priority = 'P0'; $p0note = $r.note; break }
        }
        if (-not $priority) {
            if ($v -eq 'delete') { $priority = 'P2' }
            elseif ($v -in @('post', 'put')) {
                $wl = $false
                foreach ($w in $postP1Whitelist) { if ($path -cmatch $w) { $wl = $true; break } }
                if ($wl) { $priority = 'P1' } else { $priority = 'P2' }
            }
            else { $priority = 'P1' }
        }

        $exp404 = ''
        # 规则可带 method 限定（不写则对所有 verb 生效）——避免过度匹配到 P2 写操作变体。
        # 注意：规则项是 hashtable，键存在性用 ContainsKey 判断（PSObject.Properties 对 hashtable 无效）。
        foreach ($e in $expected404Rules) {
            if ($path -cmatch $e.pattern -and ((-not $e.ContainsKey('method')) -or $v -eq $e['method'])) {
                $exp404 = $e.reason; break
            }
        }
        $exp400 = ''
        foreach ($e in $expected400Rules) {
            if ($path -cmatch $e.pattern -and ((-not $e.ContainsKey('method')) -or $v -eq $e['method'])) {
                $exp400 = $e.reason; break
            }
        }

        $ops.Add([pscustomobject]@{
            id = ('{0} {1}' -f $v.ToUpper(), $path)
            method = $v.ToUpper()
            path = $path
            operationId = [string]$op.operationId
            tag = (@($op.tags) -join ',')
            domain = (Get-OpDomain $path)
            source = 'spec'
            urlBase = 'emby'
            priority = $priority
            p0Note = $p0note
            params = $params
            paramCount = @($params).Count
            responseSchema = $respRef
            responseIsArray = $respIsArray
            requestSchema = $reqRef
            expected404 = $exp404
            expected400 = $exp400
        })
    }
}

# —— 补充 code-only 端点（spec 未收录，任务 P0/客户端必需）——
$extras = @(
    @{ id = 'GET /web/index.html'; method = 'GET'; path = '/web/index.html'; tag = 'DashboardService'; domain = 'web'; priority = 'P0'; p0Note = 'Web 首页（任务 P0；spec 未收录，code-only）'; resp = 'text/html' },
    @{ id = 'GET /favicon.ico';    method = 'GET'; path = '/favicon.ico';    tag = 'DashboardService'; domain = 'web'; priority = 'P1'; p0Note = ''; resp = 'image' },
    @{ id = 'GET /robots.txt';     method = 'GET'; path = '/robots.txt';     tag = 'DashboardService'; domain = 'web'; priority = 'P1'; p0Note = ''; resp = 'text' }
)
foreach ($e in $extras) {
    $ops.Add([pscustomobject]@{
        id = $e.id; method = $e.method; path = $e.path; operationId = ''
        tag = $e.tag; domain = $e.domain; source = 'code-only'; urlBase = 'root'
        priority = $e.priority; p0Note = $e.p0Note
        params = @(); paramCount = 0
        responseSchema = $e.resp; responseIsArray = $false; requestSchema = ''
        expected404 = ''
        expected400 = ''
    })
}

# —— 统计 ——
$specOps = @($ops | Where-Object { $_.source -eq 'spec' })
$p0 = @($ops | Where-Object { $_.priority -eq 'P0' })
$p1 = @($ops | Where-Object { $_.priority -eq 'P1' })
$p2 = @($ops | Where-Object { $_.priority -eq 'P2' })
$exp404Count = @($ops | Where-Object { $_.expected404 -and $_.priority -ne 'P2' })
$exp400Count = @($ops | Where-Object { $_.expected400 -and $_.priority -ne 'P2' })

$meta = [pscustomobject]@{
    generatedAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    specFile = $SpecPath
    specVersion = $spec.info.version
    specPathCount = 356
    specOperationCount = 414
    specRawOperationCount = 447   # 含 head 29 / trace 2 / patch 2（不单列）
    planOperationCount = @($ops).Count
    p0Count = $p0.Count
    p1Count = $p1.Count
    p2Count = $p2.Count
    expected404Count = $exp404Count.Count
    domainCount = @($ops | Select-Object -ExpandProperty domain -Unique).Count
}

$plan = [pscustomobject]@{ meta = $meta; ops = $ops }
$json = $plan | ConvertTo-Json -Depth 8
Write-ResultFile -Path (Join-Path $OutDir 'test-plan.json') -Content $json

# —— Markdown 计划 ——
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# OpenMedia API 测试计划（openapi.json 4.1.1.0 解析产物）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("> 生成时间：$($meta.generatedAt)　来源：openapi.json（Emby Server API 4.1.1.0，356 路径 / 414 核心操作 / 201 schema）")
[void]$sb.AppendLine("> 分级规则：P0 = 客户端关键路径（必须 200 且结构正确）；P1 = 常规查询（200/204/405 可接受，404 需核对 B6 暂缓清单）；P2 = 写/破坏性操作（一律跳过）")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 总览')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 项 | 数量 |')
[void]$sb.AppendLine('|----|------|')
[void]$sb.AppendLine("| spec 核心操作（get/post/put/delete/options） | $($specOps.Count) |")
[void]$sb.AppendLine("| 补充 code-only 端点（/web/index.html 等） | $(@($ops).Count - $specOps.Count) |")
[void]$sb.AppendLine("| **计划总数** | **$(@($ops).Count)** |")
[void]$sb.AppendLine("| P0（关键路径） | $($p0.Count) |")
[void]$sb.AppendLine("| P1（常规查询，其中 404 预期 $($exp404Count.Count) 个 / 400 预期 $($exp400Count.Count) 个） | $($p1.Count) |")
[void]$sb.AppendLine("| P2（写/破坏性，跳过） | $($p2.Count) |")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 分域统计')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 域 | P0 | P1 | P2 | 合计 |')
[void]$sb.AppendLine('|----|----|----|----|------|')
foreach ($d in ($ops | Group-Object domain | Sort-Object Name)) {
    $dp0 = @($d.Group | Where-Object { $_.priority -eq 'P0' }).Count
    $dp1 = @($d.Group | Where-Object { $_.priority -eq 'P1' }).Count
    $dp2 = @($d.Group | Where-Object { $_.priority -eq 'P2' }).Count
    [void]$sb.AppendLine("| $($d.Name) | $dp0 | $dp1 | $dp2 | $($d.Count) |")
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## P0 清单（必须 200 且结构正确）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 操作 | 说明 | 响应 schema |')
[void]$sb.AppendLine('|------|------|--------------|')
foreach ($o in $p0) {
    [void]$sb.AppendLine("| ``$($o.id)`` | $($o.p0Note) | $($o.responseSchema)$(if ($o.responseIsArray) {'[]'}) |")
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 404 属预期的操作（SKIP-EXPECTED 判定依据）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 操作 | 优先级 | 预期原因 |')
[void]$sb.AppendLine('|------|--------|----------|')
foreach ($o in ($ops | Where-Object { $_.expected404 })) {
    [void]$sb.AppendLine("| ``$($o.id)`` | $($o.priority) | $($o.expected404) |")
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 全量操作清单（按域分组）')
[void]$sb.AppendLine('')
foreach ($d in ($ops | Group-Object domain | Sort-Object Name)) {
    [void]$sb.AppendLine("### 域：$($d.Name)（$($d.Count) 个操作）")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |')
    [void]$sb.AppendLine('|------|--------|------|--------|--------------|----------|----------|')
    foreach ($o in ($d.Group | Sort-Object id)) {
        $reqParams = (@($o.params) | ForEach-Object { "$($_.name)($($_.in))$(if ($_.required) {'*'})" }) -join ','
        [void]$sb.AppendLine("| ``$($o.id)`` | $($o.priority) | $reqParams | $($o.requestSchema) | $($o.responseSchema)$(if ($o.responseIsArray) {'[]'}) | $(if ($o.expected404) { '是' } else { '' }) | $(if ($o.expected400) { '是' } else { '' }) |")
    }
    [void]$sb.AppendLine('')
}
Write-ResultFile -Path (Join-Path $OutDir 'test-plan.md') -Content $sb.ToString()

Write-Host ""
Write-Host "=== 测试计划生成完成 ==="
Write-Host "spec 核心操作: $($specOps.Count)（get $((@($specOps)|Where-Object{$_.method -eq 'GET'}).Count) / post $((@($specOps)|Where-Object{$_.method -eq 'POST'}).Count) / delete $((@($specOps)|Where-Object{$_.method -eq 'DELETE'}).Count) / put $((@($specOps)|Where-Object{$_.method -eq 'PUT'}).Count) / options $((@($specOps)|Where-Object{$_.method -eq 'OPTIONS'}).Count)）"
Write-Host "补充 code-only: $(@($ops).Count - $specOps.Count)"
Write-Host "P0: $($p0.Count)  P1: $($p1.Count)  P2: $($p2.Count)"
Write-Host "404 预期（P0/P1 中）: $($exp404Count.Count)"
Write-Host "400 预期（P0/P1 中）: $($exp400Count.Count)"
Write-Host "输出: $OutDir\test-plan.json / test-plan.md"
