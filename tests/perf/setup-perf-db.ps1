# ============================================================
# T2.1 性能审计独立库搭建（openmedia_perf）
# 幂等：drop 若存在 → 重建 → schema → fixture-10k.sql
# 严禁指向共享库 openmedia（R2）；连接串与 BasePgRepository.DefaultConnectionString 一致
# ============================================================
param(
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$PerfDb = 'openmedia_perf'
)
$ErrorActionPreference = 'Continue'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
if (-not (Test-Path $psql)) { Write-Host "psql 不存在：$psql"; exit 1 }
$env:PGPASSWORD = $PgPassword
$base = @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser)

Write-Host "[1/4] 删除旧 $PerfDb（若存在）..."
& $psql @base -d postgres -c "DROP DATABASE IF EXISTS $PerfDb WITH (FORCE);" 2>$null
Write-Host "[2/4] 创建 $PerfDb ..."
& $psql @base -d postgres -c "CREATE DATABASE $PerfDb;"
if ($LASTEXITCODE -ne 0) { Write-Host "建库失败"; exit 1 }

Write-Host "[3/4] 克隆实际库 schema（pg_dump -s；实际库比 schema-deploy.sql 多运行时列 type_name/clean_name 等，以实际为准）..."
$pgdump = 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe'
& $pgdump @base -s -d openmedia -f "$env:TEMP\openmedia-schema.sql"
if ($LASTEXITCODE -ne 0) { Write-Host "pg_dump 失败"; exit 1 }
& $psql @base -d $PerfDb -v ON_ERROR_STOP=1 -f "$env:TEMP\openmedia-schema.sql" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "schema 应用失败"; exit 1 }

Write-Host "[4/4] 灌入 10k fixture ..."
& $psql @base -d $PerfDb -v ON_ERROR_STOP=1 -f "$PSScriptRoot\fixture-10k.sql"
if ($LASTEXITCODE -ne 0) { Write-Host "fixture 失败"; exit 1 }

Write-Host "完成：$PerfDb 就绪（共享库 openmedia 未被触碰）"
