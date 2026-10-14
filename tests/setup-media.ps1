# OpenMedia 测试媒体生成（幂等）：tests\media\ 下两部假电影
# Movie1/Movie2：.mkv（EBML 魔数 + 256KB 确定性伪数据，非空以支持 Range 测试）+ .nfo（title/year）+ poster.jpg（Movie1，供 Primary 图片端点）
# PowerShell 5.1 兼容。
param(
    [string]$MediaRoot = "D:\Dev\OpenMedia\tests\media"
)
$ErrorActionPreference = 'Stop'

$movies = @(
    @{ Folder = 'Movie1'; Title = 'Movie1'; Year = 2020; Poster = $true },
    @{ Folder = 'Movie2'; Title = 'Movie2'; Year = 2021; Poster = $false }
)

function Write-FakeMkv {
    param([string]$Path, [int]$SizeKB = 256, [int]$Seed = 12345)
    if ((Test-Path -LiteralPath $Path) -and ((Get-Item -LiteralPath $Path).Length -ge 1KB)) { return $false }
    $fs = [System.IO.File]::Create($Path)
    try {
        $header = [byte[]](0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20)
        $fs.Write($header, 0, $header.Length)
        $rnd = New-Object System.Random($Seed)
        $chunk = New-Object byte[] 4096
        $rnd.NextBytes($chunk)
        $remaining = $SizeKB * 1024 - $header.Length
        while ($remaining -gt 0) {
            $n = [Math]::Min($chunk.Length, $remaining)
            $fs.Write($chunk, 0, $n)
            $remaining -= $n
        }
    }
    finally { $fs.Dispose() }
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
    if (Write-FakeMkv -Path $mkv -Seed (12345 + $m.Year)) { $created += $mkv }

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
