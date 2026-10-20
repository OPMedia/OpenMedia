# ============================================================
# S8 修复：电影子目录 Folder 行预建（扫描安全）
# 根因（10-16 soak 事故实证：Movie 200k→20k）：电影文件在
#   movieNNN\Movie-XXXXXX\ 子目录里，但 parent_id 挂在顶层 Folder
#   （movieNNN）。扫描 Except 流在顶层比对"DB 子项（电影行）vs
#   磁盘子项（子目录）"→ 电影全部判 missing 删除；递归进子目录后
#   新建 Folder 行，但 0 字节文件过不了探测不重建 → 净损失。
#   other 库（平铺：文件直接在库目录下）零损失证明：父链深度 =
#   磁盘深度 + 公式 GUID = 扫描安全充要条件。
# 修法：为每个 per-movie 子目录预建 Folder 行（GUID 按
#   MD5(UTF16("MediaBrowser.Controller.Entities.Folder" + path))
#   公式，扫描按 GUID 命中即复用不删），电影 parent 下沉一层，
#   祖先链同步。TV 树（剧集挂在 Season 深度）同理已安全。
# 用法：.\gen-movie-subfolders.ps1  → 输出 s8-movie-subfolders.sql
# 执行时机：管线 link-tree（步骤4）之后、GUID 修正（步骤6）之前。
param(
    [int]$PgPort = 5432
)
$ErrorActionPreference = 'Stop'
# ============================================================
$ErrorActionPreference = 'Stop'
# psql 定位：$env:PSQL_PATH > PATH 解析 > Program Files 下的版本目录（取最高版本）> 历史硬编码。
# CI 的 windows runner 预装 PostgreSQL 16，硬编码 18 会直接找不到。
$psql = if ($env:PSQL_PATH) { $env:PSQL_PATH }
        else {
            $cmd = Get-Command psql.exe -ErrorAction SilentlyContinue
            if ($cmd) { $cmd.Source }
            else {
                $cand = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'PostgreSQL') -Filter 'psql.exe' -Recurse -ErrorAction SilentlyContinue |
                        Sort-Object FullName -Descending | Select-Object -First 1
                if ($cand) { $cand.FullName } else { 'C:\Program Files\PostgreSQL\18\bin\psql.exe' }
            }
        }
$env:PGPASSWORD = '123456'
$env:PGCLIENTENCODING = 'UTF8'

# [1/4] 导出顶层 Folder 路径 + 电影路径
Write-Host "[1/4] 导出路径..."
& $psql -h localhost -p $PgPort -U postgres -d openmedia_scale -A -t -F '|' -c "
SELECT id, path FROM media_items WHERE type = 1 ORDER BY id;" | Out-File -FilePath "$env:TEMP\sf-tops.txt" -Encoding utf8
$tops = [System.Collections.Generic.HashSet[string]]::new()
foreach ($l in [IO.File]::ReadAllLines("$env:TEMP\sf-tops.txt")) {
    if ($l -match '^\d+\|(.+)$') { [void]$tops.Add($Matches[1]) }
}
& $psql -h localhost -p $PgPort -U postgres -d openmedia_scale -A -t -F '|' -c "
SELECT id, path FROM media_items WHERE type = 2 AND path IS NOT NULL ORDER BY id;" | Out-File -FilePath "$env:TEMP\sf-movies.txt" -Encoding utf8
$movies = [IO.File]::ReadAllLines("$env:TEMP\sf-movies.txt") | Where-Object { $_ -match '^\d+\|' }
Write-Host "  顶层 Folder $($tops.Count) 个；电影 $($movies.Count) 条"

# [2/4] 计算子目录 Folder + 公式 GUID
$folderType = 'MediaBrowser.Controller.Entities.Folder'
$md5 = [System.Security.Cryptography.MD5]::Create()
function Get-FolderGuid([string]$path) {
    $bytes = $md5.ComputeHash([System.Text.Encoding]::Unicode.GetBytes($folderType + $path))
    return [Guid]::new($bytes).ToString('D')
}
function Get-DirName([string]$p) { return $p.Substring(0, $p.LastIndexOf('\')) }
function Get-BaseName([string]$p) { return $p.Substring($p.LastIndexOf('\') + 1) }

Write-Host "[2/4] 生成 SQL..."
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("-- S8 修复：电影子目录 Folder 行预建 + parent 下沉 + 祖先链")
[void]$sb.AppendLine("-- 生成器：tests/perf/gen-movie-subfolders.ps1（扫描安全：父链深度=磁盘深度）")
[void]$sb.AppendLine("CREATE TEMP TABLE subfolders (folder_guid uuid, name text, path text, top_path text);")
[void]$sb.AppendLine("CREATE TEMP TABLE reparent (movie_id bigint, sub_path text);")
$seen = [System.Collections.Generic.HashSet[string]]::new()
$nSub = 0; $nMov = 0
foreach ($line in $movies) {
    $parts = $line -split '\|', 2
    $movieId = [long]$parts[0]; $moviePath = $parts[1]
    $subPath = Get-DirName $moviePath
    if ($tops.Contains($subPath)) { continue }   # 平铺电影（other 库）：parent 已是顶层，安全
    $topPath = Get-DirName $subPath
    if (-not $tops.Contains($topPath)) { continue }  # 不在链接树下的异常行，不动
    if ($seen.Add($subPath)) {
        $g = Get-FolderGuid $subPath
        $name = Get-BaseName $subPath
        [void]$sb.AppendLine("INSERT INTO subfolders VALUES ('$g', '$name', '$subPath', '$topPath');")
        $nSub++
    }
    [void]$sb.AppendLine("INSERT INTO reparent VALUES ($movieId, '$subPath');")
    $nMov++
    if (($nMov % 50000) -eq 0) { Write-Host "  $nMov 处理中..." }
}
Write-Host "  子目录 Folder $nSub 行；电影重挂 $nMov 条"

# [3/4] 集合式落地（INSERT Folder 行 / UPDATE parent / 祖先链）
[void]$sb.AppendLine("INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, sort_name, parent_id, top_parent_id)")
[void]$sb.AppendLine("SELECT s.folder_guid, 1, 'MediaBrowser.Controller.Entities.Folder', s.name, s.path, true, '{}'::jsonb, s.name, f.id, replace(f.guid::text, '-', '')")
[void]$sb.AppendLine("FROM subfolders s JOIN media_items f ON f.type = 1 AND f.path = s.top_path;")
[void]$sb.AppendLine("UPDATE media_items m SET parent_id = sf.id")
[void]$sb.AppendLine("FROM reparent r, media_items sf WHERE m.id = r.movie_id AND sf.path = r.sub_path AND sf.type = 1;")
[void]$sb.AppendLine("DELETE FROM ancestor_ids a USING reparent r, subfolders s")
[void]$sb.AppendLine("WHERE a.item_id = r.movie_id AND s.path = r.sub_path AND a.ancestor_id IN (SELECT f.id FROM media_items f WHERE f.type = 1 AND f.path IN (SELECT top_path FROM subfolders));")
[void]$sb.AppendLine("INSERT INTO ancestor_ids (item_id, ancestor_id, distance)")
[void]$sb.AppendLine("SELECT r.movie_id, sf.id, 1 FROM reparent r JOIN media_items sf ON sf.path = r.sub_path AND sf.type = 1 ON CONFLICT DO NOTHING;")
[void]$sb.AppendLine("INSERT INTO ancestor_ids (item_id, ancestor_id, distance)")
[void]$sb.AppendLine("SELECT r.movie_id, f.id, 2 FROM reparent r JOIN subfolders s ON s.path = r.sub_path JOIN media_items f ON f.type = 1 AND f.path = s.top_path ON CONFLICT DO NOTHING;")
[void]$sb.AppendLine("INSERT INTO ancestor_ids (item_id, ancestor_id, distance)")
[void]$sb.AppendLine("SELECT sf.id, f.id, 1 FROM subfolders s JOIN media_items sf ON sf.path = s.path AND sf.type = 1 JOIN media_items f ON f.type = 1 AND f.path = s.top_path ON CONFLICT DO NOTHING;")
[void]$sb.AppendLine("INSERT INTO ancestor_ids (item_id, ancestor_id, distance)")
[void]$sb.AppendLine("SELECT sf.id, 1, 0 FROM subfolders s JOIN media_items sf ON sf.path = s.path AND sf.type = 1 ON CONFLICT DO NOTHING;")

# [4/4] 验收
[void]$sb.AppendLine("SELECT 'subfolder_rows' AS check, count(*) FROM media_items WHERE type = 1 AND guid IN (SELECT folder_guid FROM subfolders);")
[void]$sb.AppendLine("SELECT 'movie_reparented', count(*) FROM media_items m JOIN reparent r ON r.movie_id = m.id JOIN media_items p ON p.id = m.parent_id WHERE p.type = 1 AND p.guid IN (SELECT folder_guid FROM subfolders);")
[void]$sb.AppendLine("SELECT 'movie_depth_ok', count(*) FROM media_items m JOIN media_items p ON p.id = m.parent_id WHERE m.type = 2 AND position(p.path || '\' in m.path) = 1;")
[void]$sb.AppendLine("SELECT 'movie_total', count(*) FROM media_items WHERE type = 2;")

[IO.File]::WriteAllText("$PSScriptRoot\s8-movie-subfolders.sql", $sb.ToString(), [System.Text.UTF8Encoding]::new($true))
Write-Host "[4/4] 完成：$nSub Folder 行 + $nMov 重挂 → s8-movie-subfolders.sql（$([math]::Round((Get-Item "$PSScriptRoot\s8-movie-subfolders.sql").Length/1MB, 1)) MB）"
