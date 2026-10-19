using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace PgBatchWriteStress
{
    /// <summary>
    /// 批量写并发压测（评审 P1-5）。
    ///
    /// 场景：100 客户端 × 20 轮，混合 INSERT（新 key）与 UPDATE（已存在 key）
    /// 的批量 UNNEST UPSERT，被测 SQL 与
    /// PgUserDataRepository.SaveAllUserDataAsync 完全一致。
    ///
    /// 断言：
    ///   1. 无失败事务；
    ///   2. pg_stat_database.deadlocks 增量为 0；
    ///   3. 打印 ThreadPool.ThreadCount 峰值，证明未发生线程池饥饿。
    ///
    /// 结果落盘 tests/perf/results/pg-batch-write-&lt;ts&gt;.json。
    /// </summary>
    internal static class Program
    {
        private static readonly string ConnStr =
            Environment.GetEnvironmentVariable("OPENMEDIA_TEST_PGCONN")
            ?? "Host=localhost;Port=15432;Database=openmedia;Username=postgres;Password=123456;" +
               "Pooling=true;MaxPoolSize=100;CommandTimeout=30";

        private const string BatchSql = @"
            INSERT INTO user_datas
                (user_data_key_id, user_id, rating, played, play_count, is_favorite,
                 playback_position_ticks, last_played_date, audio_stream_index, subtitle_stream_index)
            SELECT t.k_id, t.u_id, t.r, t.p, t.pc, t.fav, t.pos, t.lpd, t.asi, t.ssi
            FROM UNNEST(
                @k_ids, @u_ids, @rs, @ps, @pcs, @favs, @poss, @lpds, @asis, @ssis
            ) AS t(k_id, u_id, r, p, pc, fav, pos, lpd, asi, ssi)
            ON CONFLICT (user_data_key_id, user_id) DO UPDATE SET
                rating = EXCLUDED.rating,
                played = EXCLUDED.played,
                play_count = EXCLUDED.play_count,
                is_favorite = EXCLUDED.is_favorite,
                playback_position_ticks = EXCLUDED.playback_position_ticks,
                last_played_date = EXCLUDED.last_played_date,
                audio_stream_index = EXCLUDED.audio_stream_index,
                subtitle_stream_index = EXCLUDED.subtitle_stream_index,
                playstate_last_modified = now()";

        private static async Task<int> Main(string[] args)
        {
            int clients = GetArg(args, "--clients", 100);
            int rounds = GetArg(args, "--rounds", 20);

            await using var ds = NpgsqlDataSource.Create(ConnStr);

            int userId;
            int[] keyIds;

            try
            {
                (userId, keyIds) = await PrepareAsync(ds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("无法连接 PostgreSQL（先 docker compose up -d postgres）：" + ex.Message);
                return 3;
            }

            Console.WriteLine($"[setup] user_id={userId} keys={keyIds.Length}");

            long deadlocksBefore = await ReadDeadlocksAsync(ds).ConfigureAwait(false);
            Console.WriteLine($"[baseline] deadlocks={deadlocksBefore}");

            var latencies = new List<double>(clients * rounds);
            var failures = new List<string>();
            var gate = new object();

            int baseThreads = ThreadPool.ThreadCount;
            int peakThreads = baseThreads;
            using var monCts = new CancellationTokenSource();

            var monitor = Task.Run(async () =>
            {
                while (!monCts.IsCancellationRequested)
                {
                    int cur = ThreadPool.ThreadCount;
                    if (cur > peakThreads)
                    {
                        peakThreads = cur;
                    }

                    await Task.Delay(5).ConfigureAwait(false);
                }
            });

            var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var workers = Enumerable.Range(0, clients).Select(cid => Task.Run(async () =>
            {
                await startGate.Task.ConfigureAwait(false);

                for (int r = 0; r < rounds; r++)
                {
                    // 每轮随机挑 5~20 个 key，并强制字典序（与仓储层确定性加锁顺序一致）
                    var batch = keyIds
                        .Where((_, idx) => (idx + cid + r) % 3 == 0)
                        .Take(5 + ((cid + r) % 16))
                        .OrderBy(x => x)
                        .ToArray();

                    if (batch.Length == 0)
                    {
                        batch = new[] { keyIds[(cid + r) % keyIds.Length] };
                    }

                    int n = batch.Length;

                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await using var conn = await ds.OpenConnectionAsync().ConfigureAwait(false);
                        await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);
                        await using (var cmd = new NpgsqlCommand(BatchSql, conn, tx))
                        {
                            cmd.Parameters.Add(new NpgsqlParameter("k_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = batch });
                            cmd.Parameters.Add(new NpgsqlParameter("u_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                            {
                                Value = Enumerable.Repeat(userId, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("rs", NpgsqlDbType.Array | NpgsqlDbType.Double)
                            {
                                Value = Enumerable.Repeat((object)8.5, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("ps", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                            {
                                Value = Enumerable.Repeat(true, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("pcs", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                            {
                                Value = Enumerable.Repeat(r, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("favs", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                            {
                                Value = Enumerable.Repeat(false, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("poss", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
                            {
                                Value = Enumerable.Repeat((long)(r * 1000), n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("lpds", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
                            {
                                Value = Enumerable.Repeat((object)DateTime.UtcNow, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("asis", NpgsqlDbType.Array | NpgsqlDbType.Smallint)
                            {
                                Value = Enumerable.Repeat((object)(short)0, n).ToArray()
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("ssis", NpgsqlDbType.Array | NpgsqlDbType.Smallint)
                            {
                                Value = Enumerable.Repeat((object)(short)0, n).ToArray()
                            });

                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        await tx.CommitAsync().ConfigureAwait(false);
                        sw.Stop();

                        lock (gate)
                        {
                            latencies.Add(sw.Elapsed.TotalMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        lock (gate)
                        {
                            if (failures.Count < 10)
                            {
                                failures.Add($"client{cid}#{r}: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                }
            })).ToList();

            var total = Stopwatch.StartNew();
            startGate.SetResult(true);
            await Task.WhenAll(workers).ConfigureAwait(false);
            total.Stop();

            monCts.Cancel();
            await monitor.ConfigureAwait(false);

            long deadlocksAfter = await ReadDeadlocksAsync(ds).ConfigureAwait(false);

            latencies.Sort();

            double Pct(double p) => latencies.Count == 0
                ? 0
                : latencies[Math.Min(latencies.Count - 1, (int)(latencies.Count * p / 100.0))];

            var result = new
            {
                timestamp = DateTime.Now.ToString("o"),
                clients,
                rounds_per_client = rounds,
                expected_transactions = clients * rounds,
                ok = latencies.Count,
                failed = failures.Count,
                failures,
                elapsed_sec = Math.Round(total.Elapsed.TotalSeconds, 3),
                throughput_tps = total.Elapsed.TotalSeconds > 0
                    ? Math.Round(latencies.Count / total.Elapsed.TotalSeconds, 1)
                    : 0,
                latency_ms = new
                {
                    p50 = Math.Round(Pct(50), 2),
                    p95 = Math.Round(Pct(95), 2),
                    p99 = Math.Round(Pct(99), 2),
                    max = latencies.Count > 0 ? Math.Round(latencies[^1], 2) : 0
                },
                threadpool = new
                {
                    baseline = baseThreads,
                    peak = peakThreads,
                    pending_work_items = ThreadPool.PendingWorkItemCount
                },
                deadlocks_before = deadlocksBefore,
                deadlocks_after = deadlocksAfter,
                deadlocks_delta = deadlocksAfter - deadlocksBefore
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);

            var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results");
            dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(dir);
            var outFile = Path.Combine(dir, "pg-batch-write-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            await File.WriteAllTextAsync(outFile, json).ConfigureAwait(false);
            Console.WriteLine("[saved] " + outFile);

            if (failures.Count != 0)
            {
                Console.Error.WriteLine("FAIL: 存在失败事务");
                return 1;
            }

            if (deadlocksAfter - deadlocksBefore != 0)
            {
                Console.Error.WriteLine("FAIL: 出现新增死锁");
                return 1;
            }

            Console.WriteLine("PASS: 0 失败、0 新增死锁");
            return 0;
        }

        private static async Task<(int UserId, int[] KeyIds)> PrepareAsync(NpgsqlDataSource ds)
        {
            await using var conn = await ds.OpenConnectionAsync().ConfigureAwait(false);

            int userId;
            await using (var cmd = new NpgsqlCommand("SELECT id FROM local_users ORDER BY id LIMIT 1", conn))
            {
                var o = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                if (o == null)
                {
                    await using var ins = new NpgsqlCommand(
                        "INSERT INTO local_users (guid, data) VALUES (@g, '{}'::jsonb) RETURNING id", conn);
                    ins.Parameters.Add(new NpgsqlParameter("g", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() });
                    userId = Convert.ToInt32(await ins.ExecuteScalarAsync().ConfigureAwait(false));
                }
                else
                {
                    userId = Convert.ToInt32(o);
                }
            }

            var keys = new List<int>();
            for (int i = 0; i < 40; i++)
            {
                await using var cmd = new NpgsqlCommand(
                    @"INSERT INTO user_data_keys (user_data_key) VALUES (@k)
                      ON CONFLICT (user_data_key) DO UPDATE SET user_data_key = EXCLUDED.user_data_key
                      RETURNING id", conn);
                cmd.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Text) { Value = $"perf-batch-{i:D3}" });
                keys.Add(Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false)));
            }

            return (userId, keys.ToArray());
        }

        private static async Task<long> ReadDeadlocksAsync(NpgsqlDataSource ds)
        {
            await using var conn = await ds.OpenConnectionAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()", conn);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
        }

        private static int GetArg(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal) &&
                    int.TryParse(args[i + 1], out var v))
                {
                    return v;
                }
            }

            return fallback;
        }
    }
}
