# ============================================================
# S5 规模化独立库搭建（openmedia_scale，1M 条目）
# 幂等：drop 若存在 → 重建 → schema（pg_dump -s 实际库）→ fixture-1m.sql
# 严禁指向共享库 openmedia（安全红线）
# 磁盘预估：data ~1.5GB + 索引 3-5GB，生成前检查 D 盘剩余空间
# ============================================================
param(
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$ScaleDb = 'openmedia_scale',
    [double]$MinFreeGB = 10
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
$pgdump = 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe'
if (-not (Test-Path $psql)) { Write-Host "psql 不存在：$psql"; exit 1 }
$env:PGPASSWORD = $PgPassword
$base = @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser, '-v', 'ON_ERROR_STOP=1')

# 前置：磁盘空间检查
$drive = Get-PSDrive -Name 'D' -ErrorAction SilentlyContinue
if ($drive) {
    $freeGB = [math]::Round($drive.Free / 1GB, 1)
    Write-Host "[0/5] D 盘剩余 ${freeGB}GB（要求 ≥ ${MinFreeGB}GB）"
    if ($freeGB -lt $MinFreeGB) { Write-Host "空间不足，中止（调 -MinFreeGB 或清理磁盘）"; exit 1 }
}

# 前置：PG 可达性
& $psql @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser, '-d', 'postgres', '-c', 'SELECT 1;') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "PG 不可达（$PgHost`:$PgPort）——确认 PG 在 $PgPort 运行（-PgPort 可覆盖）"; exit 1 }

Write-Host "[1/5] 删除旧 $ScaleDb（若存在）..."
& $psql @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser, '-d', 'postgres', '-c', "DROP DATABASE IF EXISTS $ScaleDb WITH (FORCE);") 2>$null

Write-Host "[2/5] 创建 $ScaleDb ..."
& $psql @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser, '-d', 'postgres', '-c', "CREATE DATABASE $ScaleDb;")
if ($LASTEXITCODE -ne 0) { Write-Host "建库失败"; exit 1 }

Write-Host "[3/5] 克隆实际库 schema（pg_dump -s 只读共享库结构，不碰数据）..."
& $pgdump @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser, '-s', '-d', 'openmedia', '-f', "$env:TEMP\openmedia-schema.sql")
if ($LASTEXITCODE -ne 0) { Write-Host "pg_dump 失败"; exit 1 }
& $psql @($base + @('-d', $ScaleDb, '-f', "$env:TEMP\openmedia-schema.sql")) | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "schema 应用失败"; exit 1 }

Write-Host "[4/5] 灌入 1M fixture（预计 3-10 分钟，含 timing 输出）..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $psql @($base + @('-d', $ScaleDb, '-f', "$PSScriptRoot\fixture-1m.sql"))
if ($LASTEXITCODE -ne 0) { Write-Host "fixture 失败"; exit 1 }
$sw.Stop()
Write-Host ("[5/5] 完成：{0} 就绪（生成耗时 {1} 分钟，共享库 openmedia 未被触碰）" -f $ScaleDb, [math]::Round($sw.Elapsed.TotalMinutes, 1))
