# ============================================================
# pg-stat-statements-top20.ps1 —— 欠账 4 天的 Top-20 慢查询取证（三口径）
#
# 背景（docs/perf-baseline.md §1 / §6 长期"待补"）：
#   pg_stat_statements 一直没启用（shared_preload_libraries 为空），
#   §1 与 §6 承诺的"慢查询 Top-20"从未补做。
#   2026-10-20 已启用：postgresql.conf 第 814 行改为
#   shared_preload_libraries = 'pg_stat_statements' + 重启 postgresql-x64-18
#   + CREATE EXTENSION（postgres 库与 openmedia_perf 库各建一次）。
#
# 用法：
#   # 1. 清空累计统计（建议每轮负载前做一次，否则历史数据会糊住排名）
#   & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h localhost -U postgres -d openmedia_perf -c "SELECT pg_stat_statements_reset();"
#   # 2. 跑负载（例如 run-ab-load.ps1 -Tag release -Db openmedia_perf）
#   # 3. 取 Top-20
#   powershell -ExecutionPolicy Bypass -File tests\perf\pg-stat-statements-top20.ps1
# ============================================================
param(
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$PgUser = 'postgres',
    [string]$PgPassword = '123456',
    [string]$Db = 'openmedia_perf',
    [int]$TopN = 20
)
$ErrorActionPreference = 'Continue'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$env:PGPASSWORD = $PgPassword
$base = @('-h', $PgHost, '-p', "$PgPort", '-U', $PgUser, '-d', $Db, '-P', 'pager=off')
$outdir = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Force -Path $outdir | Out-Null

# 0. 前置断言：扩展必须在库里装好，否则后面全是空表
$ext = & $psql @base -t -A -c "SELECT count(*) FROM pg_extension WHERE extname='pg_stat_statements';"
if ($ext.Trim() -ne '1') {
    throw ("pg_stat_statements 未在 $Db 安装。先：CREATE EXTENSION pg_stat_statements;（并确认 postgresql.conf 的 shared_preload_libraries 已含它且已重启服务）")
}
Write-Host "[0] pg_stat_statements 已就绪（$Db）"

function Run-Sql([string]$title, [string]$sql, [string]$outfile) {
    Write-Host ""
    Write-Host "===== $title ====="
    $tmp = Join-Path $env:TEMP ($outfile + '.sql')
    [IO.File]::WriteAllText($tmp, $sql)
    & $psql @base -f $tmp | Tee-Object -FilePath (Join-Path $outdir $outfile) | Out-String
}

# 1. 总量口径
Run-Sql "总量（tracked 语句数 / 总调用 / 总耗时）" @"
SELECT count(*) AS tracked, sum(calls) AS total_calls,
       round(sum(total_exec_time)::numeric,1) AS total_ms
FROM pg_stat_statements;
"@ 'pgss-total.out'

# 2. Top-N by total_exec_time（最常用的"谁最烧"口径）
Run-Sql "Top-$TopN by total_exec_time" @"
SELECT rank, left(regexp_replace(query, '\s+', ' ', 'g'), 150) AS query_150,
       calls, round(mean_exec_time::numeric,2) AS mean_ms,
       round(total_exec_time::numeric,1) AS total_ms,
       round(max_exec_time::numeric,1) AS max_ms, rows AS rows_returned
FROM (
  SELECT row_number() OVER (ORDER BY total_exec_time DESC) AS rank, query, calls, mean_exec_time, total_exec_time, max_exec_time, rows
  FROM pg_stat_statements
) t
WHERE rank <= $TopN
ORDER BY rank;
"@ 'pgss-top20-total.out'

# 3. Top-N by mean_exec_time（单次最慢口径，抓偶发重查询）
Run-Sql "Top-$TopN by mean_exec_time" @"
SELECT rank, left(regexp_replace(query, '\s+', ' ', 'g'), 110) AS query_110,
       calls, round(mean_exec_time::numeric,2) AS mean_ms
FROM (
  SELECT row_number() OVER (ORDER BY mean_exec_time DESC) AS rank, query, calls, mean_exec_time
  FROM pg_stat_statements
) t
WHERE rank <= $TopN
ORDER BY rank;
"@ 'pgss-top20-mean.out'

# 4. Top-N by calls（调用最频口径，抓 N+1 与连接层开销）
Run-Sql "Top-$TopN by calls" @"
SELECT rank, left(regexp_replace(query, '\s+', ' ', 'g'), 110) AS query_110,
       calls, round(total_exec_time::numeric,1) AS total_ms
FROM (
  SELECT row_number() OVER (ORDER BY calls DESC) AS rank, query, calls, total_exec_time
  FROM pg_stat_statements
) t
WHERE rank <= $TopN
ORDER BY rank;
"@ 'pgss-top20-calls.out'

# 5. 头号慢查询的执行计划（磁盘排序/分区全扫一眼可见）
$top1 = & $psql @base -t -A -c "SELECT query FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 1;"
if ($top1) {
    $explainSql = "EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF)`n" + $top1 + ";`n"
    Run-Sql "头号慢查询 EXPLAIN (ANALYZE, BUFFERS)" $explainSql 'pgss-top1-explain.out'
}

Write-Host ""
Write-Host ("原始输出已写入 {0}" -f $outdir)
