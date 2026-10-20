# ============================================================
# T2.1 性能审计独立库搭建（openmedia_perf）
# 幂等：drop 若存在 → 重建 → schema → fixture（10k 平面 / tree-100k 树形）
#
# 端口：默认 5432（本机 PG 标准端口）。历史脚本硬编码 15432 已于 2026-10-20
# 全量改为 $PgPort 参数（可在命令行覆盖），本机 PG 已回 5432。
#
# 硬红线：严禁指向共享库 openmedia（R2：功能测试断言 TotalRecordCount）
#
# fixture 选择：
#   10k      —— 平面单库（历史 T2.1 形态，10k Movie 共享一个 CollectionFolder 祖先）
#              ⚠ 祖先分布不真实，BR-45 教训：该形态下 AncestorWithPresentationUniqueKey
#              EXPLAIN 结论失真（见 docs/perf-baseline.md §5 P4），只用于历史对比。
#   tree-100k —— 100 库树形结构（默认 100k 条目），祖先分布贴近真实库形态。
#              由 fixture-100k-tree.ps1 生成，禁止任何人手改。
# ============================================================
param(
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$PerfDb = 'openmedia_perf',
    [ValidateSet('10k', 'tree-100k', 'tree-20k')]
    [string]$Fixture = '10k'
)
$ErrorActionPreference = 'Continue'
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
if (-not (Test-Path $psql)) { Write-Host "psql 不存在：$psql"; exit 1 }
$env:PGPASSWORD = $PgPassword
$base = @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser)

Write-Host "[1/4] 删除旧 $PerfDb（若存在）..."
& $psql @base -d postgres -c "DROP DATABASE IF EXISTS $PerfDb WITH (FORCE);" 2>$null
Write-Host "[2/4] 创建 $PerfDb ..."
& $psql @base -d postgres -c "CREATE DATABASE $PerfDb;"
if ($LASTEXITCODE -ne 0) { Write-Host "建库失败"; exit 1 }

# 安全断言：fire-and-forget 参数不足以防御手抄，这里显式拦一次
if ($PerfDb -eq 'openmedia') { throw "拒绝在共享库 openmedia 上执行（安全红线 R2）" }

Write-Host "[3/4] 克隆实际库 schema（pg_dump -s；实际库比 schema-deploy.sql 多运行时列 type_name/clean_name 等，以实际为准）..."
$pgdump = 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe'
& $pgdump @base -s -d openmedia -f "$env:TEMP\openmedia-schema.sql"
if ($LASTEXITCODE -ne 0) { Write-Host "pg_dump 失败"; exit 1 }
& $psql @base -d $PerfDb -v ON_ERROR_STOP=1 -f "$env:TEMP\openmedia-schema.sql" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "schema 应用失败"; exit 1 }

if ($Fixture -eq '10k') {
    Write-Host "[4/4] 灌入平面 10k fixture（历史口径）..."
    & $psql @base -d $PerfDb -v ON_ERROR_STOP=1 -f "$PSScriptRoot\fixture-10k.sql"
    if ($LASTEXITCODE -ne 0) { Write-Host "fixture 失败"; exit 1 }
} else {
    Write-Host "[4/4] 生成树形 $Fixture fixture（祖先分布贴近真实库形态，BR-45 教训）..."
    $treeSql = "$env:TEMP\fixture-tree-100k.sql"
    & "$PSScriptRoot\fixture-100k-tree.ps1" -OutFile $treeSql
    if ($LASTEXITCODE -ne 0) { Write-Host "树形 fixture 生成失败"; exit 1 }
    & $psql @base -d $PerfDb -v ON_ERROR_STOP=1 -f $treeSql
    if ($LASTEXITCODE -ne 0) { Write-Host "树形 fixture 生成失败"; exit 1 }
}

Write-Host "完成：$PerfDb 就绪（共享库 openmedia 未被触碰）"
