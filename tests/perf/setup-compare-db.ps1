# ============================================================
# setup-compare-db.ps1 —— Debug/Release 同口径 A/B 对比专用独立库
#
# 为什么存在（docs/perf-baseline.md 口径问题）：
#   老 Debug 数字基于 147 条目的库；共享库 openmedia 现在只有 1 条目。
#   **不同规模的数据做前后对比毫无意义**（7.2 已栽过一次：64->147 条目
#   就让 P50 40.8ms->363ms，9x 伪回归）。
#   本脚本建一个独立小库（默认 openmedia_compare，~471 条目），
#   Debug 与 Release 两侧指向同一个库、同一份数据、同一负载，
#   唯一变量是构建配置。
#
# 硬红线：严禁指向共享库 openmedia（R2：功能测试断言 TotalRecordCount）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tests\perf\setup-compare-db.ps1
#   # 然后：
#   $env:OPENMEDIA_PGCONN = "Host=localhost;Port=5432;Database=openmedia_compare;Username=postgres;Password=123456"
#   dotnet MediaBrowser.Server\bin\Debug\net10.0\MediaBrowser.Server.dll      # 或 Release
#   python tests\perf\load-ab-compare.py
# ============================================================
param(
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$CompareDb = 'openmedia_compare',
    [string]$Psql = '',
    [string]$PgDump = ''
)
$ErrorActionPreference = 'Continue'  # psql 的 NOTICE 走 stderr，Stop 会把它当终止错误
$env:PGPASSWORD = $PgPassword
# psql 定位：显式 -Psql > PATH 解析 > Program Files 下的版本目录（取最高版本）> 历史硬编码。
# CI 的 windows runner 预装的是 PostgreSQL 16（不是 18），硬编码 18 会直接 exit 1。
$psql = if ($Psql) { $Psql }
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

# ---- 硬红线：绝不碰共享库 openmedia ----
if ($CompareDb -eq 'openmedia') {
    throw "拒绝在共享库 openmedia 上执行（安全红线 R2：功能测试断言 TotalRecordCount）"
}
if ($CompareDb -notmatch '^(openmedia_compare|openmedia_perf|openmedia_scale)$') {
    throw ("目标库名 '$CompareDb' 不在白名单（openmedia_compare/openmedia_perf/openmedia_scale）——防手抄误伤共享库")
}

$env:PGPASSWORD = $PgPassword
$base = @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser)

Write-Host "[1/5] 删除旧 $CompareDb（若存在）..."
& $psql @base -d postgres -c "DROP DATABASE IF EXISTS $CompareDb WITH (FORCE);" 2>$null | Out-Null
Write-Host "[2/5] 创建 $CompareDb ..."
& $psql @base -d postgres -c "CREATE DATABASE $CompareDb;"
if ($LASTEXITCODE -ne 0) { Write-Host "建库失败"; exit 1 }

Write-Host "[3/5] 克隆实际库 schema（pg_dump -s）..."
# pg_dump 定位同 psql：显式 > PATH > Program Files 下取最高版本 > 历史硬编码。
# CI 的 windows runner 预装 PG 17，硬编码 18 会直接 exit 1。
$pgdump = if ($PgDump) { $PgDump }
          else {
              $cmd = Get-Command pg_dump.exe -ErrorAction SilentlyContinue
              if ($cmd) { $cmd.Source }
              else {
                  $cand = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'PostgreSQL') -Filter 'pg_dump.exe' -Recurse -ErrorAction SilentlyContinue |
                          Sort-Object FullName -Descending | Select-Object -First 1
                  if ($cand) { $cand.FullName } else { 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe' }
              }
          }
if (-not (Test-Path $pgdump)) { Write-Host "pg_dump 不存在：$pgdump"; exit 1 }
& $pgdump @base -s -d openmedia -f "$env:TEMP\openmedia-schema.sql"
if ($LASTEXITCODE -ne 0) { Write-Host "pg_dump 失败"; exit 1 }
& $psql @base -d $CompareDb -v ON_ERROR_STOP=1 -f "$env:TEMP\openmedia-schema.sql" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "schema 应用失败"; exit 1 }

Write-Host "[4/5] 灌入 A/B 对比 fixture（~471 条目树形）..."
& "$PSScriptRoot\fixture-ab-compare.ps1" -OutFile "$env:TEMP\fixture-ab-compare.sql"
if ($LASTEXITCODE -ne 0) { Write-Host "fixture 生成失败"; exit 1 }
& $psql @base -d $CompareDb -v ON_ERROR_STOP=1 -f "$env:TEMP\fixture-ab-compare.sql" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "fixture 灌入失败"; exit 1 }

Write-Host "[5/5] 验收（共享库 openmedia 未被触碰）..."
& $psql @base -d $CompareDb -c "SELECT 'media_items', count(*) FROM media_items;"
& $psql @base -d postgres -c "SELECT 'shared_openmedia_must_stay_1' AS check, count(*) FROM openmedia.media_items;"

Write-Host ""
Write-Host "完成：$CompareDb 就绪——Debug/Release 两侧用同一连接串："
Write-Host "  `$env:OPENMEDIA_PGCONN = `"Host=$PgHost;Port=$PgPort;Database=$CompareDb;Username=$PgUser;Password=$PgPassword`""
