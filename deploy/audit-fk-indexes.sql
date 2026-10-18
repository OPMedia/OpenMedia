-- ============================================================
-- 外键索引完整性审计（工业级）
--
-- 目的：PostgreSQL 不会为外键列自动建索引。父表 UPDATE/DELETE 触发
-- ON DELETE/UPDATE 检查或加 KEY SHARE 锁时，缺失索引会让子表退化为
-- 全表扫描 + 全表 ShareLock，是并发写入下最隐蔽的争用源。
--
-- 用法：
--   psql -d openmedia -f deploy/audit-fk-indexes.sql
--
-- 判据：对每个 (conrelid, conkey[0]) 外键首列，存在 indrelid 相同且
--       indkey[0] 为该列的索引（含分区子表各自的本地索引）。
-- 输出：missing_index 非空行即为需要补齐的外键列。
-- ============================================================

\echo '=== 1. 缺失索引的外键列（期望 0 行） ==='

SELECT
    c.conname                                   AS fk_constraint,
    c.conrelid::regclass::text                  AS child_table,
    a.attname                                   AS fk_column,
    pg_get_constraintdef(c.oid)                 AS constraint_def
FROM pg_constraint c
JOIN pg_attribute a
     ON a.attrelid = c.conrelid
    AND a.attnum = ANY (c.conkey)
WHERE c.contype = 'f'
  AND NOT EXISTS (
      SELECT 1
      FROM pg_index i
      WHERE i.indrelid = c.conrelid
        AND i.indkey[0] = a.attnum
  )
ORDER BY child_table, fk_column;

\echo ''
\echo '=== 2. 全部外键列 → 可用索引映射（人工复核用） ==='

SELECT
    c.conname                                   AS fk_constraint,
    c.conrelid::regclass::text                  AS child_table,
    a.attname                                   AS fk_column,
    i.indexrelid::regclass::text                AS usable_index
FROM pg_constraint c
JOIN pg_attribute a
     ON a.attrelid = c.conrelid
    AND a.attnum = ANY (c.conkey)
LEFT JOIN pg_index i
     ON i.indrelid = c.conrelid
    AND i.indkey[0] = a.attnum
WHERE c.contype = 'f'
ORDER BY child_table, fk_column;

\echo ''
\echo '=== 3. 汇总：缺失索引的外键列总数（期望 0） ==='

SELECT count(*) AS missing_fk_index_count
FROM pg_constraint c
JOIN pg_attribute a
     ON a.attrelid = c.conrelid
    AND a.attnum = ANY (c.conkey)
WHERE c.contype = 'f'
  AND NOT EXISTS (
      SELECT 1
      FROM pg_index i
      WHERE i.indrelid = c.conrelid
        AND i.indkey[0] = a.attnum
  );
