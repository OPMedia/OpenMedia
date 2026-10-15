# OpenMedia 测试媒体生成（幂等）：tests\media\ 下两部电影
# Movie1/Movie2：.mkv（ffmpeg 真实 H.264+AAC，-force_key_frames 每 2s —— 关键帧稀疏会让
#   HLS seek 分片退化为整文件拷贝，2026-10-15 复验实证）+ .nfo（title/year）+ poster.jpg（Movie1）
# 兼容旧假文件（256KB 伪 EBML）：存在且 <300KB 或无视频流时自动重生成真视频。
# PowerShell 5.1 兼容。
param(
    [string]$MediaRoot = "D:\Dev\OpenMedia\tests\media",
    [string]$Ffmpeg = "D:\OpenMedia\ffmpeg.exe"
)
$ErrorActionPreference = 'Stop'

$movies = @(
    @{ Folder = 'Movie1'; Title = 'Movie1'; Year = 2020; Poster = $true;  Seconds = 10 },
    @{ Folder = 'Movie2'; Title = 'Movie2'; Year = 2021; Poster = $false; Seconds = 8 }
)

function Test-RealVideo {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    if ((Get-Item -LiteralPath $Path).Length -lt 300KB) { return $false }
    # ffprobe 探测有视频流才算真视频（假文件探测必失败）
    $probe = & $Ffmpeg.Replace('ffmpeg.exe', 'ffprobe.exe') -v error -select_streams v -show_entries stream=codec_name -of csv=p=0 $Path 2>$null
    return ($LASTEXITCODE -eq 0 -and $probe)
}

function Write-RealMkv {
    param([string]$Path, [int]$Seconds)
    & $Ffmpeg -y -hide_banner -loglevel error `
        -f lavfi -i "testsrc2=size=640x360:rate=30:duration=$Seconds" `
        -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=$Seconds" `
        -c:v libx264 -preset veryfast -pix_fmt yuv420p -force_key_frames "expr:gte(t,n_forced*2)" `
        -c:a aac -b:a 128k -shortest $Path 2>$null
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg 生成失败: $Path" }
    return $true
}

# 1x1 JPEG（base64，经典最小合法图）；失败时回退 System.Drawing 生成 16x16。
$posterB64 = '/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwcJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDIzNP/AABEIAAEAAQMBIgACEQEDEQH/xAAfAAABBQEBAQEBAQAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscHRJDNS8OHxY3ODNCkxIlYkRUjNCS1JWVk5RTjVcLWlNXWVhZTc4OW5cVmdo/9k='

function Write-PosterJpg {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) { return $false }
    $ok = $false
    try {
        $bytes = [Convert]::FromBase64String($posterB64)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xD8 -and $bytes[2] -eq 0xFF) {
            [System.IO.File]::WriteAllBytes($Path, $bytes)
            $ok = $true
        }
    } catch {}
    if (-not $ok) {
        Add-Type -AssemblyName System.Drawing | Out-Null
        $bmp = New-Object System.Drawing.Bitmap(16, 16)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::SteelBlue)
            $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
            $ok = $true
        }
        finally { $g.Dispose(); $bmp.Dispose() }
    }
    return $ok
}

$created = @()
foreach ($m in $movies) {
    $dir = Join-Path $MediaRoot $m.Folder
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

    $mkv = Join-Path $dir "$($m.Folder).mkv"
    if (-not (Test-RealVideo $mkv)) {
        if (Test-Path -LiteralPath $mkv) { Remove-Item -LiteralPath $mkv -Force }
        if (Write-RealMkv -Path $mkv -Seconds $m.Seconds) { $created += $mkv }
    }

    $nfo = Join-Path $dir "$($m.Folder).nfo"
    if (-not (Test-Path -LiteralPath $nfo)) {
        $xml = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<movie>`r`n  <title>$($m.Title)</title>`r`n  <year>$($m.Year)</year>`r`n</movie>`r`n"
        [System.IO.File]::WriteAllText($nfo, $xml, [System.Text.Encoding]::UTF8)
        $created += $nfo
    }

    if ($m.Poster) {
        $poster = Join-Path $dir 'poster.jpg'
        if (Write-PosterJpg -Path $poster) { $created += $poster }
    }
}

Write-Host "媒体目录: $MediaRoot"
Get-ChildItem -LiteralPath $MediaRoot -Recurse -File | ForEach-Object {
    Write-Host ("  {0}  {1} bytes" -f $_.FullName.Substring($MediaRoot.Length + 1), $_.Length)
}
if ($created.Count -eq 0) { Write-Host '（全部已存在，幂等跳过）' }
else { Write-Host "新建 $($created.Count) 个文件" }
