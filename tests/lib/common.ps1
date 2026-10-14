# OpenMedia 测试共享库（PowerShell 5.1 兼容）
# 用法：. "$PSScriptRoot\lib\common.ps1"
# 提供 HttpClient 封装、Emby 认证、PascalCase/契约校验助手。

$script:OMHttpLoaded = $false
function Import-HttpAssembly {
    if (-not $script:OMHttpLoaded) {
        Add-Type -AssemblyName System.Net.Http | Out-Null
        $script:OMHttpLoaded = $true
    }
}

function New-EmbyClient {
    param([int]$TimeoutSec = 90)
    Import-HttpAssembly
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $true
    $handler.MaxAutomaticRedirections = 5
    $handler.AutomaticDecompression = ([System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate)
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    return $client
}

# 发送一条 HTTP 请求并捕获状态码/头/体（截断保护）。
# 返回 pscustomobject：Ok/Status/Reason/ContentType/Body/BodyBytes/ContentRange/Location/Error/ElapsedMs
function Invoke-EmbyRequest {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Url,
        [hashtable]$Headers,
        [string]$BodyJson,
        [int]$MaxBodyBytes = 4194304
    )
    Import-HttpAssembly
    $res = [pscustomobject]@{
        Ok = $false; Status = 0; Reason = ''; ContentType = ''
        Body = ''; BodyBytes = 0; ContentRange = ''; Location = ''
        Error = ''; ElapsedMs = 0
    }
    $methodObj = New-Object System.Net.Http.HttpMethod($Method)
    $req = New-Object System.Net.Http.HttpRequestMessage($methodObj, $Url)
    if ($Headers) {
        foreach ($k in @($Headers.Keys)) {
            [void]$req.Headers.TryAddWithoutValidation($k, [string]$Headers[$k])
        }
    }
    if ($BodyJson) {
        $req.Content = New-Object System.Net.Http.StringContent($BodyJson, [System.Text.Encoding]::UTF8, 'application/json')
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $sendTask = $Client.SendAsync($req, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead)
        if (-not $sendTask.Wait(120000)) { $res.Error = 'send timeout (120s)'; return $res }
        $resp = $sendTask.Result
        $res.Status = [int]$resp.StatusCode
        $res.Reason = [string]$resp.ReasonPhrase
        try { if ($resp.Content.Headers.ContentType) { $res.ContentType = $resp.Content.Headers.ContentType.MediaType } } catch {}
        try { if ($resp.Content.Headers.ContentRange) { $res.ContentRange = $resp.Content.Headers.ContentRange.ToString() } } catch {}
        try { if ($resp.Headers.Location) { $res.Location = $resp.Headers.Location.ToString() } } catch {}
        if ($null -ne $resp.Content) {
            $ms = New-Object System.IO.MemoryStream
            $copyTask = $resp.Content.CopyToAsync($ms)
            if ($copyTask.Wait(60000)) {
                $bytes = $ms.ToArray()
                $res.BodyBytes = $bytes.Length
                if ($bytes.Length -le $MaxBodyBytes) {
                    $res.Body = [System.Text.Encoding]::UTF8.GetString($bytes)
                } else {
                    $res.Body = [System.Text.Encoding]::UTF8.GetString($bytes, 0, $MaxBodyBytes) + '...[TRUNCATED]'
                }
            } else {
                $res.Error = 'body read timeout (60s)'
            }
            $ms.Dispose()
        }
        $res.Ok = $true
    }
    catch {
        $inner = $_.Exception
        if ($inner -is [System.AggregateException] -and $inner.InnerException) { $inner = $inner.InnerException }
        $res.Error = "request failed: $($inner.Message)"
    }
    finally {
        $sw.Stop()
        $res.ElapsedMs = $sw.ElapsedMilliseconds
    }
    return $res
}

function ConvertFrom-JsonSafe {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try { return ($Text | ConvertFrom-Json) } catch { return $null }
}

# 检查 JSON 顶层键名（数组取首元素）是否全部 PascalCase；返回违规键名数组。
function Get-PascalViolations {
    param($Json)
    $v = @()
    if ($null -eq $Json) { return $v }
    $obj = $Json
    if ($obj -is [System.Array]) {
        if ($obj.Count -eq 0) { return $v }
        $obj = $obj[0]
    }
    if ($obj -is [System.Management.Automation.PSCustomObject]) {
        foreach ($p in $obj.PSObject.Properties) {
            if ($p.Name -cnotmatch '^[A-Z][A-Za-z0-9]*$') { $v += $p.Name }
        }
    }
    return $v
}

# 顶层键名列表（数组取首元素），逗号连接；供矩阵"关键字段"列。
function Get-TopKeys {
    param($Json, [int]$Max = 24)
    if ($null -eq $Json) { return '' }
    $obj = $Json
    if ($obj -is [System.Array]) {
        if ($obj.Count -eq 0) { return '(空数组)' }
        $obj = $obj[0]
    }
    if ($obj -is [System.Management.Automation.PSCustomObject]) {
        $keys = @($obj.PSObject.Properties | ForEach-Object { $_.Name })
        if ($keys.Count -gt $Max) {
            return (($keys | Select-Object -First $Max) -join ',') + ",…($($keys.Count))"
        }
        return ($keys -join ',')
    }
    return "(非对象: $($obj.GetType().Name))"
}

function Get-EmbyAuthHeader {
    param([string]$DeviceId = 'openmedia-test-01')
    return 'MediaBrowser Client="OpenMediaTest", Device="TestRunner", DeviceId="' + $DeviceId + '", Version="4.1.1.0"'
}

# POST /Users/AuthenticateByName；返回原始响应（调用方解析 AccessToken）。
function Invoke-AuthenticateByName {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$ApiBase,
        [string]$Username = 'admin',
        [string]$Pw = 'admin',
        [string]$DeviceId = 'openmedia-test-01'
    )
    $body = @{ Username = $Username; Pw = $Pw } | ConvertTo-Json -Depth 5
    return Invoke-EmbyRequest -Client $Client -Method 'POST' -Url "$ApiBase/Users/AuthenticateByName" `
        -Headers @{ 'X-Emby-Authorization' = (Get-EmbyAuthHeader $DeviceId) } -BodyJson $body
}

# 32 位无连字符 hex（Emby Guid "N" 格式）校验
function Test-EmbyIdFormat {
    param([string]$Value)
    if ([string]::IsNullOrEmpty($Value)) { return $false }
    return ($Value -cmatch '^[0-9a-fA-F]{32}$')
}

function Write-ResultFile {
    param([string]$Path, [string]$Content)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.Encoding]::UTF8)
}
