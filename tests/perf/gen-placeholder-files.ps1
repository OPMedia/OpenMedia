# S8 前置：1M 占位文件生成器（幂等，0 字节，路径来自 scale 库的媒体 path 列）
# 用法：路径清单由 psql 导出（SELECT path FROM media_items WHERE type IN (2,5,6,20,8,9)）
#       .\gen-placeholder-files.ps1 -PathList <清单文件>
# 880k 文件 ≈ 8-15 分钟（含目录创建）；幂等：已存在跳过。
param(
    [string]$PathList = "D:\Dev\OpenMedia\tests\perf\placeholder-paths.txt",
    [int]$ReportEvery = 100000
)
$ErrorActionPreference = 'Continue'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$n = 0; $created = 0; $skipped = 0
$dirCache = New-Object 'System.Collections.Generic.HashSet[string]'
$fs = [IO.File]::OpenText($PathList)
try {
    while ($null -ne ($line = $fs.ReadLine())) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $n++
        $dir = [IO.Path]::GetDirectoryName($line)
        if (-not $dirCache.Contains($dir)) {
            [IO.Directory]::CreateDirectory($dir) | Out-Null
            [void]$dirCache.Add($dir)
        }
        if (-not [IO.File]::Exists($line)) {
            [IO.File]::WriteAllText($line, "")
            $created++
        } else { $skipped++ }
        if (($n % $ReportEvery) -eq 0) {
            Write-Host ("  {0}k 行：新建 {1} / 跳过 {2}（{3:F1} 分钟）" -f [int]($n/1000), $created, $skipped, $sw.Elapsed.TotalMinutes)
        }
    }
} finally { $fs.Close() }
$sw.Stop()
Write-Host ("完成：{0} 行 / 新建 {1} / 跳过 {2} / 目录 {3} / 耗时 {4:F1} 分钟" -f $n, $created, $skipped, $dirCache.Count, $sw.Elapsed.TotalMinutes)
