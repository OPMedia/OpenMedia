# OpenMedia 测试音乐生成（幂等）：tests\media-music\ 下 3 艺人 / 4 专辑 / 9 曲目
# 真实音频（ffmpeg 正弦波合成）+ ID3/FLAC/AAC 元数据标签（artist/album/title/track/date/genre）。
# 目录结构 Artist\Album\Track —— 对齐 MusicArtistResolver/MusicAlbumResolver 预期。
# PowerShell 5.1 兼容。
param(
    [string]$MediaRoot = "D:\Dev\OpenMedia\tests\media-music",
    [string]$Ffmpeg = "D:\OpenMedia\ffmpeg.exe"
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Ffmpeg)) { throw "ffmpeg 不存在: $Ffmpeg" }

# 曲目定义：艺人 / 专辑 / 年份 / 曲目号 / 标题 / 频率(Hz) / 扩展名 / 编码器参数
$tracks = @(
    @{ Artist='Alpha Artist'; Album='Alpha Album One'; Year=2020; Track=1; Title='Alpha First Tone'; Freq=440;  Ext='mp3'; Enc=@('-codec:a','libmp3lame','-b:a','96k') },
    @{ Artist='Alpha Artist'; Album='Alpha Album One'; Year=2020; Track=2; Title='Alpha Second Tone'; Freq=523; Ext='mp3'; Enc=@('-codec:a','libmp3lame','-b:a','96k') },
    @{ Artist='Alpha Artist'; Album='Alpha Album One'; Year=2020; Track=3; Title='Alpha Third Tone'; Freq=587;  Ext='mp3'; Enc=@('-codec:a','libmp3lame','-b:a','96k') },

    @{ Artist='Alpha Artist'; Album='Alpha Album Two'; Year=2022; Track=1; Title='Alpha New Wave'; Freq=659;  Ext='mp3';   Enc=@('-codec:a','libmp3lame','-b:a','96k') },
    @{ Artist='Alpha Artist'; Album='Alpha Album Two'; Year=2022; Track=2; Title='Alpha Lossless'; Freq=698; Ext='flac';  Enc=@('-codec:a','flac') },

    @{ Artist='Beta Band';    Album='Beta Debut';     Year=2021; Track=1; Title='Beta Opening'; Freq=392;    Ext='mp3'; Enc=@('-codec:a','libmp3lame','-b:a','96k') },
    @{ Artist='Beta Band';    Album='Beta Debut';     Year=2021; Track=2; Title='Beta Encore'; Freq=494;     Ext='m4a'; Enc=@('-codec:a','aac','-b:a','96k') },

    @{ Artist='Gamma Singer'; Album='Gamma Solo';     Year=2023; Track=1; Title='Gamma Ballad'; Freq=330;    Ext='mp3'; Enc=@('-codec:a','libmp3lame','-b:a','96k') },
    @{ Artist='Gamma Singer'; Album='Gamma Solo';     Year=2023; Track=2; Title='Gamma Reprise'; Freq=349;   Ext='mp3'; Enc=@('-codec:a','libmp3lame','-b:a','96k') }
)

$created = @()
foreach ($t in $tracks) {
    $dir = Join-Path $MediaRoot "$($t.Artist)\$($t.Album)"
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

    $name = "{0:D2} - {1}.{2}" -f [int]$t.Track, $t.Title, $t.Ext
    $path = Join-Path $dir $name
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Length -ge 10KB)) { continue }

    # 元数据：title/artist/album_artist/album/track/date/genre（TagLib 读取的正是这些帧）
    $args = @(
        '-y', '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', ("sine=frequency={0}:duration=10" -f $t.Freq),
        '-metadata', "title=$($t.Title)",
        '-metadata', "artist=$($t.Artist)",
        '-metadata', "album_artist=$($t.Artist)",
        '-metadata', "album=$($t.Album)",
        '-metadata', "track=$($t.Track)",
        '-metadata', "date=$($t.Year)",
        '-metadata', "genre=TestTone"
    ) + $t.Enc + @($path)

    & $Ffmpeg @args
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg 失败: $name (exit $LASTEXITCODE)" }
    $created += $path
}

# 专辑封面（folder.jpg）：Alpha Album One，供专辑 Primary 图片端点验证。
# 1x1 JPEG（base64 最小合法图）。
$albumCoverDir = Join-Path $MediaRoot "Alpha Artist\Alpha Album One"
$coverPath = Join-Path $albumCoverDir 'folder.jpg'
if (-not (Test-Path -LiteralPath $coverPath)) {
    # System.Drawing 生成 64x64 JPEG（base64 最小图在部分环境解码失败，实测静默跳过过一次）
    try {
        Add-Type -AssemblyName System.Drawing | Out-Null
        $bmp = New-Object System.Drawing.Bitmap(64, 64)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::DarkSlateBlue)
            $bmp.Save($coverPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
            $created += $coverPath
        }
        finally { $g.Dispose(); $bmp.Dispose() }
    } catch {}
}

Write-Host "音乐目录: $MediaRoot"
Get-ChildItem -LiteralPath $MediaRoot -Recurse -File | ForEach-Object {
    Write-Host ("  {0}  {1} bytes" -f $_.FullName.Substring($MediaRoot.Length + 1), $_.Length)
}
if ($created.Count -eq 0) { Write-Host '（全部已存在，幂等跳过）' }
else { Write-Host "新建 $($created.Count) 个文件" }
