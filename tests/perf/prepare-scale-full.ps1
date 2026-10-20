# ============================================================
# S7/S8 完整准备管线（幂等）：fixture → 迁移 → 路径 → GUID → 链接 → data 修正
# 产出：openmedia_scale 完全对齐运行时形态（无需扫描、无删除风险）
# 顺序严格；总耗时 ~25 分钟（fixture 13.5 + GUID 应用 6 + 其余 ~5）
# ============================================================
param(
    [string]$PgPort = 5432,
    [switch]$SkipFixture
)
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
$base = @('-h', 'localhost', '-p', "$PgPort", '-U', 'postgres', '-v', 'ON_ERROR_STOP=1', '-d', 'openmedia_scale')
$perf = "D:\Dev\OpenMedia\tests\perf"

function Step($n, $name, $script) {
    Write-Host "[$n] $name ..."
    & $psql @base -c $script 2>&1 | Select-Object -Last 1
}

# [1] 重建 fixture（drop + schema + 1M 灌入，13.5 分钟）
if (-not $SkipFixture) {
    Write-Host "[1/7] 重建 fixture（13.5 分钟）..."
    & "D:\Dev\OpenMedia\tests\perf\setup-scale-db.ps1" 2>&1 | Select-Object -Last 1
} else {
    Write-Host "[1/7] 跳过 fixture（-SkipFixture，复用现有库）"
}

# [2] 库行迁移 + 用户 Policy
Write-Host "[2/7] 库行迁移..."
& $psql @base -f "$perf\s7-migrate-libs.sql" 2>&1 | Select-Object -Last 1
# 注意：JSON 字面量必须走 jsonb_build_object（纯单引号）——PS 5.1 传原生参数时
# 内嵌双引号会被改写成 \"，PG 收到非法 JSON（10-16 管线断点实证，勿改回字面量）
& $psql @base -c "UPDATE local_users SET data = jsonb_set(data, '{Policy}', jsonb_build_object('IsAdministrator', false, 'EnableAllFolders', true, 'Enabled', true));" 2>&1 | Select-Object -Last 1

# [3] 路径修正（season/episode/audio）
Step "3/7" "season 路径" @"
UPDATE media_items sn SET path = s.path || '\Season ' || lpad(sn.index_number::text, 2, '0')
FROM media_items s WHERE sn.parent_id = s.id AND sn.type = 4 AND position('tv-lib' in sn.path) > 0;
"@
Step "3/7" "episode 路径" @"
UPDATE media_items e SET path = sn.path || '\' || (string_to_array(e.path, '\'))[array_length(string_to_array(e.path, '\'), 1)]
FROM media_items sn WHERE e.parent_id = sn.id AND e.type = 5 AND position('tv-lib' in e.path) > 0;
"@
Step "3/7" "audio 路径" @"
UPDATE media_items a SET path = al.path || '\' || (string_to_array(a.path, '\'))[array_length(string_to_array(a.path, '\'), 1)]
FROM media_items al WHERE a.parent_id = al.id AND a.type = 6 AND position('music-lib' in a.path) > 0;
"@

# [4] 链接树（Folder 行 + 重挂 + top_parent + 祖先）
Write-Host "[4/7] 链接树..."
& $psql @base -f "$perf\s7-link-tree.sql" 2>&1 | Select-Object -Last 1

# [5] other 路径修正（Folder 行已在位）
Step "5/7" "other 路径" @"
UPDATE media_items m SET path = 'D:\scale\' || f.name || '\' || (string_to_array(m.path, '\'))[array_length(string_to_array(m.path, '\'), 1)]
FROM media_items f WHERE m.parent_id = f.id AND f.type = 1 AND m.type IN (20, 8, 9) AND position('scale' || chr(92) || 'other' || chr(92) in m.path) > 0 AND position('other' in f.name) = 1;
"@

# [5.5] 电影子目录 Folder 行预建（扫描安全：父链深度=磁盘深度）
# 根因：电影文件在 movieNNN\Movie-XXXXXX\ 子目录，parent 却挂顶层 Folder →
#       扫描 Except 流判 missing 删 18 万（10-16 soak 事故）。预建公式 GUID 的
#       子目录 Folder 行 + parent 下沉 → 扫描按 GUID 命中复用，零删除。
Write-Host "[5.5/9] 电影子目录 Folder 行（扫描安全）..."
& "D:\Dev\OpenMedia\tests\perf\gen-movie-subfolders.ps1" 2>&1 | Select-Object -Last 2
& $psql @base -f "$perf\s8-movie-subfolders.sql" 2>&1 | Select-Object -Last 5

# [6] 媒体 GUID 修正（路径派生；GUID 文件可复用——路径确定性强）
Write-Host "[6/7] 媒体 GUID 修正（6 分钟）..."
& $psql @base -f "$perf\s7-fix-media-guids.sql" 2>&1 | Select-Object -Last 1

# [7] CollectionFolder data 修正（CollectionType + PhysicalFolderIds 持久化——catch-22 的根治）
Step "7/7" "CollectionFolder data" @"
UPDATE media_items l SET data = jsonb_build_object(
  'IsRoot', false, 'IsShortcut', false,
  'CollectionType', CASE
    WHEN l.name LIKE 'movie-lib-%' THEN 'movie'
    WHEN l.name LIKE 'tv-lib-%' THEN 'tvshows'
    WHEN l.name LIKE 'music-lib-%' THEN 'music'
    ELSE NULL END,
  'LinkedChildren', '[]'::jsonb, 'RemoteTrailers', '[]'::jsonb,
  'PhysicalFolderIds', to_jsonb(ARRAY[replace(f.guid::text, '-', '')]),
  'PhysicalLocationsList', to_jsonb(ARRAY[l.path, 'D:' || chr(92) || 'scale' || chr(92) || f.name])
)
FROM media_items f
WHERE f.type = 1 AND f.name = replace(l.name, '-lib-', '') AND l.type = 0;
"@

# 终验
Write-Host "=== 终验 ==="
& $psql @base -A -t -c "SELECT 'total: ' || count(*) FROM media_items;"
& $psql @base -A -t -c "SELECT 'libs with data: ' || count(*) FROM media_items WHERE type = 0 AND data ? 'PhysicalFolderIds';"
& $psql @base -A -t -c "SELECT 'episodes: ' || count(*) FROM media_items WHERE type = 5;"
Write-Host "完成：openmedia_scale 就绪（无需扫描，实例直接起）"

# [8] CollectionFolder 挂到 UserRootFolder（视图生成的父子前提）
Step "8/9" "lib parent" @"
UPDATE media_items SET parent_id = (SELECT id FROM media_items WHERE type = 25) WHERE type = 0 AND parent_id IS NULL;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT l.id, p.id, 1 FROM media_items l JOIN media_items p ON p.id = l.parent_id WHERE l.type = 0 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT l.id, r.id, 2 FROM media_items l JOIN media_items ur ON ur.id = l.parent_id JOIN media_items r ON r.id = ur.parent_id WHERE l.type = 0 ON CONFLICT DO NOTHING;
"@

# [9] top_parent_id 统一为 N 格式（无连字符）的链接 Folder guid（经祖先链）
Step "9/9" "top_parent N-format" @"
UPDATE media_items m SET top_parent_id = replace(f.guid::text, '-', '')
FROM media_items f
WHERE f.type = 1 AND m.id IN (SELECT item_id FROM ancestor_ids WHERE ancestor_id = f.id);
"@
