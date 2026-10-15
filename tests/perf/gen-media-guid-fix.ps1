# S8 前置：媒体条目 GUID 修正生成器（路径派生，对齐 GetNewItemId 公式）
# 根因：fixture 用 gen_random_uuid()，扫描按 MD5(UTF16(typeFullName + path)) 匹配 →
#       GUID 对不上 → 判 missing 删除（10-15 22:27 扫描事故实证：TV 树 57 万条被删）。
# 本脚本从 scale 库导出 (id, type, path)，算公式 GUID，生成 UPDATE SQL。
# 用法：.\gen-media-guid-fix.ps1  → 输出 s7-fix-media-guids.sql
$ErrorActionPreference = 'Stop'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$env:PGPASSWORD = '123456'
$env:PGCLIENTENCODING = 'UTF8'

# 导出 (id, type, path)
Write-Host "[1/3] 导出媒体路径..."
& $psql -h localhost -p 15432 -U postgres -d openmedia_scale -A -t -F '|' -c "
SELECT id, type, path FROM media_items WHERE type IN (2,3,4,5,6,7,8,9,20) AND path IS NOT NULL ORDER BY id;" | Out-File -FilePath "$env:TEMP\scale-paths.txt" -Encoding utf8
$lines = [IO.File]::ReadAllLines("$env:TEMP\scale-paths.txt") | Where-Object { $_ -match '^\d+\|' }
Write-Host "  导出 $($lines.Count) 条"

# 类型全名映射
$typeNames = @{
    2  = 'MediaBrowser.Controller.Entities.Movies.Movie'
    3  = 'MediaBrowser.Controller.Entities.TV.Series'
    4  = 'MediaBrowser.Controller.Entities.TV.Season'
    5  = 'MediaBrowser.Controller.Entities.TV.Episode'
    6  = 'MediaBrowser.Controller.Entities.Audio.Audio'
    7  = 'MediaBrowser.Controller.Entities.Audio.MusicAlbum'
    8  = 'MediaBrowser.Controller.Entities.Book'
    9  = 'MediaBrowser.Controller.Entities.Photo'
    20 = 'MediaBrowser.Controller.Entities.Video'
}
$md5 = [System.Security.Cryptography.MD5]::Create()

Write-Host "[2/3] 计算公式 GUID + 生成 SQL..."
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("-- S8 前置：媒体 GUID 修正（路径派生，GetNewItemId 公式对齐）")
[void]$sb.AppendLine("-- 生成器：tests/perf/gen-media-guid-fix.ps1；执行顺序：path 修正之后")
[void]$sb.AppendLine("CREATE TEMP TABLE guid_fix (id bigint PRIMARY KEY, new_guid uuid);")
$n = 0
foreach ($line in $lines) {
    $parts = $line -split '\|', 3
    $id = [long]$parts[0]; $type = [int]$parts[1]; $path = $parts[2]
    $key = $typeNames[[int]$type] + $path
    $bytes = $md5.ComputeHash([System.Text.Encoding]::Unicode.GetBytes($key))
    $g = [Guid]::new($bytes).ToString('D')
    [void]$sb.AppendLine("INSERT INTO guid_fix VALUES ($id, '$g');")
    $n++
    if (($n % 200000) -eq 0) { Write-Host "  $n / $($lines.Count)" }
}
[void]$sb.AppendLine("UPDATE media_items m SET guid = g.new_guid FROM guid_fix g WHERE m.id = g.id;")
[void]$sb.AppendLine("SELECT 'guids fixed' AS c, count(*) FROM media_items m JOIN guid_fix g ON m.id = g.id;")
[IO.File]::WriteAllText("$PSScriptRoot\s7-fix-media-guids.sql", $sb.ToString(), [System.Text.UTF8Encoding]::new($true))
Write-Host "[3/3] 完成：$n 条 → s7-fix-media-guids.sql（$([math]::Round((Get-Item "$PSScriptRoot\s7-fix-media-guids.sql").Length/1MB, 1)) MB）"
