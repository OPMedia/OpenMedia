# OpenMedia 测试剧集生成（幂等）：tests\media-tv\ 下 1 部剧 / 2 季 / 4 集
# 真实视频（ffmpeg testsrc 合成 H.264+AAC）+ Kodi 风格 nfo（tvshow.nfo / episodedetails）。
# 目录结构 Show\Season NN\Show SxxExx.mkv —— 对齐 SeriesResolver/SeasonResolver/EpisodeResolver 预期。
# PowerShell 5.1 兼容。
param(
    [string]$MediaRoot = "D:\Dev\OpenMedia\tests\media-tv",
    [string]$Ffmpeg = ''
)
$ErrorActionPreference = 'Stop'

# ffmpeg 定位：显式 -Ffmpeg > PATH 解析 > 历史硬编码路径（与 setup-media.ps1 一致）。
if (-not $Ffmpeg) {
    $ffcmd = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($ffcmd) { $Ffmpeg = $ffcmd.Source }
    elseif (Test-Path -LiteralPath 'D:\OpenMedia\ffmpeg.exe') { $Ffmpeg = 'D:\OpenMedia\ffmpeg.exe' }
    else { throw '找不到 ffmpeg.exe：用 -Ffmpeg 指定，或把 ffmpeg 加入 PATH' }
}
if (-not (Test-Path -LiteralPath $Ffmpeg)) { throw "ffmpeg 不存在: $Ffmpeg" }

$show = 'Show Alpha'

# 集定义：季 / 集号 / 标题 / 播出日期 / 音频频率(Hz)
$episodes = @(
    @{ Season=1; Ep=1; Title='Pilot';      Aired='2019-04-01'; Freq=440 },
    @{ Season=1; Ep=2; Title='Aftermath';  Aired='2019-04-08'; Freq=494 },
    @{ Season=2; Ep=1; Title='Return';     Aired='2020-04-06'; Freq=523 },
    @{ Season=2; Ep=2; Title='Finale';     Aired='2020-04-13'; Freq=587 }
)

$created = @()
foreach ($e in $episodes) {
    $dir = Join-Path $MediaRoot ("{0}\Season {1:D2}" -f $show, [int]$e.Season)
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

    $base = "{0} S{1:D2}E{2:D2}" -f $show, [int]$e.Season, [int]$e.Ep
    $mkv = Join-Path $dir "$base.mkv"
    if (-not ((Test-Path -LiteralPath $mkv) -and ((Get-Item -LiteralPath $mkv).Length -ge 20KB))) {
        & $Ffmpeg -y -hide_banner -loglevel error `
            -f lavfi -i "testsrc=duration=6:size=320x180:rate=15" `
            -f lavfi -i "sine=frequency=$($e.Freq):duration=6" `
            -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest "$mkv"
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg 失败: $base (exit $LASTEXITCODE)" }
        $created += $mkv
    }

    # 集 nfo（Kodi episodedetails）
    $nfo = Join-Path $dir "$base.nfo"
    if (-not (Test-Path -LiteralPath $nfo)) {
        $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<episodedetails>
  <title>$($e.Title)</title>
  <season>$($e.Season)</season>
  <episode>$($e.Ep)</episode>
  <aired>$($e.Aired)</aired>
  <plot>Test episode $($e.Title) of $show.</plot>
</episodedetails>
"@
        [System.IO.File]::WriteAllText($nfo, $xml, [System.Text.Encoding]::UTF8)
        $created += $nfo
    }
}

# 剧集 nfo（tvshow）
$showDir = Join-Path $MediaRoot $show
$tvnfo = Join-Path $showDir 'tvshow.nfo'
if (-not (Test-Path -LiteralPath $tvnfo)) {
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<tvshow>
  <title>$show</title>
  <year>2019</year>
  <plot>Synthetic test series with two seasons.</plot>
</tvshow>
"@
    [System.IO.File]::WriteAllText($tvnfo, $xml, [System.Text.Encoding]::UTF8)
    $created += $tvnfo
}

# 剧集海报 poster.jpg（1x1 最小合法 JPEG，同 setup-media.ps1）
$poster = Join-Path $showDir 'poster.jpg'
if (-not (Test-Path -LiteralPath $poster)) {
    $posterB64 = '/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwcJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDIzNP/AABEIAAEAAQMBIgACEQEDEQH/xAAfAAABBQEBAQEBAQAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscHRJDNS8OHxY3ODNCkxIlYkRUjNCS1JWVk5RTjVcLWlNXWVhZTc4OW5cVmdo/9k='
    try {
        $bytes = [Convert]::FromBase64String($posterB64)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xD8 -and $bytes[2] -eq 0xFF) {
            [System.IO.File]::WriteAllBytes($poster, $bytes)
            $created += $poster
        }
    } catch {}
    if (-not (Test-Path -LiteralPath $poster)) {
        # 兜底：System.Drawing 生成 16x16（同 setup-media.ps1）
        Add-Type -AssemblyName System.Drawing | Out-Null
        $bmp = New-Object System.Drawing.Bitmap(16, 16)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::SeaGreen)
            $bmp.Save($poster, [System.Drawing.Imaging.ImageFormat]::Jpeg)
            $created += $poster
        }
        finally { $g.Dispose(); $bmp.Dispose() }
    }
}

Write-Host "剧集目录: $MediaRoot"
Get-ChildItem -LiteralPath $MediaRoot -Recurse -File | ForEach-Object {
    Write-Host ("  {0}  {1} bytes" -f $_.FullName.Substring($MediaRoot.Length + 1), $_.Length)
}
if ($created.Count -eq 0) { Write-Host '（全部已存在，幂等跳过）' }
else { Write-Host "新建 $($created.Count) 个文件" }
