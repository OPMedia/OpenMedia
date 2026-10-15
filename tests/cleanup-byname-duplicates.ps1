# T4.10 ByName 同名 divergent GUID 重复行存量清理（幂等，兼作回归工具）
# 流程（Architect phase4-design §3.4 定案）：
#   同名对检测（type+name HAVING count>1）→ 保留小写键行（承重图片 fixture 所在）→
#   divergent 行资产迁移（images/user_data_key/extradata/people 引用）→
#   零引用验证 → 删 divergent 行 → 复跑零动作（幂等证明）
# GUID 判定：小写键公式 = MD5(type.FullName + lower(path.Replace('%MetadataPath%','metadata')))
#   （与 LibraryManager.GetNewItemIdInternal 的 ProgramData 前缀剥离+小写语义一致，
#    已对库内 Genre/CollectionFolder 行逐一验证吻合）
# PowerShell 5.1 兼容；PG 已迁 15432（默认值跟随）。
param(
    [string]$PgHost = 'localhost',
    [int]$PgPort = 15432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$DbName = 'openmedia',
    [string]$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$env:PGPASSWORD = $PgPassword

# ByName 类型 → .NET FullName（PgItemRepository.TypeFullNameToCode 的逆映射）
$typeNames = @{
    11 = 'MediaBrowser.Controller.Entities.Person'
    12 = 'MediaBrowser.Controller.Entities.Genre'
    13 = 'MediaBrowser.Controller.Entities.Studio'
    18 = 'MediaBrowser.Controller.Entities.Audio.MusicArtist'
    27 = 'MediaBrowser.Controller.Entities.Audio.MusicGenre'
    28 = 'MediaBrowser.Controller.Entities.GameGenre'
}

function Invoke-Sql {
    param([string]$Sql, [switch]$TuplesOnly)
    $args = @('-h', $PgHost, '-p', $PgPort, '-U', $PgUser, '-d', $DbName)
    if ($TuplesOnly) { $args += '-t' }
    $args += '-c', $Sql
    $out = & $Psql @args 2>&1
    return ($out -join "`n")
}

function Get-ByNameGuid {
    param([int]$Type, [string]$StoredPath)
    # %MetadataPath% → 剥离后的相对段 "metadata"，整体小写 = 小写键口径
    $key = $StoredPath.Replace('%MetadataPath%', 'metadata').ToLower()
    $full = $typeNames[[int]$Type] + $key
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($full)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = $md5.ComputeHash($bytes)
    return [Guid]::new($hash).ToString()
}

# ---------- 1. 同名对检测 ----------
$pairsSql = @"
SELECT type, name FROM media_items
WHERE type IN (11,12,13,18,27,28)
GROUP BY type, name HAVING count(*) > 1 ORDER BY type, name;
"@
$pairsRaw = Invoke-Sql -Sql $pairsSql -TuplesOnly
$pairs = @()
foreach ($line in ($pairsRaw -split "`n")) {
    if ($line -match '^\s*(\d+)\s*\|\s*(.+?)\s*$') {
        $pairs += @{ Type = [int]$Matches[1]; Name = $Matches[2] }
    }
}
if ($pairs.Count -eq 0) {
    Write-Host '[PASS] 无同名重复对（幂等：库已清洁）'
    exit 0
}
Write-Host ("发现 {0} 个同名对：" -f $pairs.Count)

$deleted = 0; $migrated = 0; $skipped = 0
foreach ($p in $pairs) {
    $rowsRaw = Invoke-Sql -Sql ("SELECT id, guid, path, coalesce(images::text,'[]'), coalesce(user_data_key_id::text,'NULL') FROM media_items WHERE type=$($p.Type) AND name='$($p.Name.Replace("'","''"))' ORDER BY id;") -TuplesOnly
    $rows = @()
    foreach ($line in ($rowsRaw -split "`n")) {
        if ($line -match '^\s*(\d+)\s*\|\s*([0-9a-f-]+)\s*\|\s*(.+?)\s*\|\s*(\[.*\])\s*\|\s*(\S+)$') {
            $rows += @{ Id = [int]$Matches[1]; Guid = $Matches[2]; Path = $Matches[3]; Images = $Matches[4]; Udk = $Matches[5] }
        }
    }
    if ($rows.Count -lt 2) { continue }

    # ---------- 2. 判定小写键行（canonical）----------
    $canonical = $null; $divergent = @()
    foreach ($r in $rows) {
        $expected = Get-ByNameGuid -Type $p.Type -StoredPath $r.Path
        if ($r.Guid -eq $expected) { $canonical = $r }
        else { $divergent += $r }
    }
    if ($null -eq $canonical) {
        # 无小写键行（罕见）：保留带图/带引用的行作 canonical
        $canonical = $rows | Where-Object { $_.Images -ne '[]' } | Select-Object -First 1
        if ($null -eq $canonical) { $canonical = $rows[0] }
        $divergent = @($rows | Where-Object { $_.Id -ne $canonical.Id })
        Write-Host ("  [{0}/{1}] 无小写键行，按图片承重保留 id={2}" -f $p.Type, $p.Name, $canonical.Id)
    }

    foreach ($d in $divergent) {
        Write-Host ("  [{0}/{1}] canonical id={2} (guid={3}) <- divergent id={4} (guid={5})" -f $p.Type, $p.Name, $canonical.Id, $canonical.Guid, $d.Id, $d.Guid)

        # ---------- 3. 资产迁移 ----------
        # 3a. images：canonical 无图且 divergent 有 → 迁移
        if ($canonical.Images -eq '[]' -and $d.Images -ne '[]') {
            if (-not $DryRun) {
                Invoke-Sql -Sql ("UPDATE media_items SET images='{0}'::jsonb WHERE id={1};" -f $d.Images.Replace("'", "''"), $canonical.Id) | Out-Null
            }
            $migrated++
            Write-Host ("    images 迁移: {0}" -f $d.Images)
        }
        # 3b. user_data_key：canonical 无且 divergent 有 → 接管
        if ($canonical.Udk -eq 'NULL' -and $d.Udk -ne 'NULL') {
            if (-not $DryRun) {
                Invoke-Sql -Sql ("UPDATE media_items SET user_data_key_id={0} WHERE id={1};" -f $d.Udk, $canonical.Id) | Out-Null
            }
            $migrated++
            Write-Host ("    user_data_key 接管: {0}" -f $d.Udk)
        }
        # 3c. extradata / people 引用重指
        $refsSql = @"
UPDATE item_extradata SET item_id={0} WHERE item_id={1} AND NOT EXISTS (SELECT 1 FROM item_extradata WHERE item_id={0} AND extradata_type_id=item_extradata.extradata_type_id AND value=item_extradata.value);
DELETE FROM item_extradata WHERE item_id={1};
UPDATE item_people SET item_id={0} WHERE item_id={1} AND NOT EXISTS (SELECT 1 FROM item_people WHERE item_id={0} AND person_id=item_people.person_id);
DELETE FROM item_people WHERE item_id={1};
"@ -f $canonical.Id, $d.Id
        if (-not $DryRun) { Invoke-Sql -Sql $refsSql | Out-Null }

        # ---------- 4. 零引用验证 → 删 ----------
        $refs = Invoke-Sql -Sql ("SELECT (SELECT count(*) FROM item_extradata WHERE item_id={0})+(SELECT count(*) FROM item_people WHERE item_id={0})+(SELECT count(*) FROM media_items WHERE parent_id={0});" -f $d.Id) -TuplesOnly
        $refCount = [int]($refs.Trim())
        if ($refCount -gt 0) {
            Write-Host ("    [SKIP] divergent id={0} 仍有 {1} 处引用，跳过删除（人工复核）" -f $d.Id, $refCount)
            $skipped++
            continue
        }
        if (-not $DryRun) {
            Invoke-Sql -Sql ("DELETE FROM media_items WHERE id={0} AND type={1} AND name='{2}';" -f $d.Id, $p.Type, $p.Name.Replace("'","''")) | Out-Null
        }
        $deleted++
        Write-Host ("    divergent id={0} 已删除（零引用验证通过）" -f $d.Id)
    }
}

Write-Host ("汇总：迁移 {0} 项资产、删除 {1} 行 divergent、跳过 {2} 行（有引用）。复跑应零动作。" -f $migrated, $deleted, $skipped)
