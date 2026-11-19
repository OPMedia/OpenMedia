namespace MediaBrowser.Model.Configuration;

/// <summary>
/// 全量备份的持久配置。缺省值保证「装好就能用」，同时给磁盘保护留一个明确的边界。
/// </summary>
public class BackupOptions
{
    /// <summary>备份根目录。为空时使用 ProgramData/backups。</summary>
    public string Directory { get; set; }

    /// <summary>pg_dump / pg_restore 所在目录。为空时依次探测 PATH 与常见安装位置。</summary>
    public string PostgresBinDirectory { get; set; }

    /// <summary>最多保留的备份份数（含恢复前快照）。0 表示不按份数清理。</summary>
    public int RetentionCount { get; set; }

    /// <summary>备份目录总大小上限（字节）。0 表示不限制。</summary>
    public long MaxTotalBytes { get; set; }

    /// <summary>周期备份间隔（小时）。0 表示禁用周期备份。</summary>
    public int IntervalHours { get; set; }

    public BackupOptions()
    {
        Directory = string.Empty;
        PostgresBinDirectory = string.Empty;
        RetentionCount = 5;
        MaxTotalBytes = 20L * 1024 * 1024 * 1024;
        IntervalHours = 24;
    }
}
