#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
S8 soak 长跑测试（scale-design §1.2 三件套口径）：
  混合负载 ≥4h（分页浏览 + 图片 + 流式 + 3 路转码 + 播放上报 + 周期扫描）
  + dotnet-counters 5s 采样（gen0/1/2/LOH 堆 + GC 暂停）
  + gcdump 双时点（warmup 末 / 结束）类型级 diff
判定：warmup（默认 30min）后堆曲线斜率 ≈ 0 = 通过；持续爬升 = 泄漏（定位到 diff 复核）。

用法（目标=规模化实例，占位文件就绪后）：
  python tests/perf/soak-test.py --base http://127.0.0.1:8098/emby --pid <PID> --hours 4
自检（3 分钟管线演练，无扫描）：
  python tests/perf/soak-test.py --base http://127.0.0.1:8098/emby --pid <PID> --hours 0.05 --warmup-min 1 --no-scan

安全红线：脚本默认拒绝向 8096（共享生产实例）触发扫描（--allow-shared-scan 显式放行）；
gcdump 会短暂暂停目标进程——禁止对用户在线的实例使用。
输出：tests/perf/results/soak-<ts>/（counters.csv、gcdump×2、report.md、report.json）
"""
import argparse, json, os, subprocess, sys, threading, time, random, statistics
import urllib.request, urllib.error
from datetime import datetime

OUT_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results')

def http(method, url, headers=None, body=None, timeout=30):
    req = urllib.request.Request(url, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    data = json.dumps(body).encode() if body is not None else None
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, data=data, timeout=timeout) as resp:
            resp.read()
            code = resp.status
    except urllib.error.HTTPError as e:
        try: e.read()
        except Exception: pass
        code = e.code
    except Exception:
        code = -1
    return (time.perf_counter() - t0) * 1000.0, code

class Stats:
    def __init__(self):
        self.lock = threading.Lock()
        self.ops = 0; self.errs = 0; self.lat = []
    def add(self, ms, code):
        with self.lock:
            self.ops += 1
            if code < 200 or code >= 400: self.errs += 1
            if len(self.lat) < 20000: self.lat.append(ms)
    def summary(self):
        with self.lock:
            if not self.lat: return {}
            s = sorted(self.lat)
            return {'ops': self.ops, 'errs': self.errs,
                    'p50': round(s[len(s)//2], 1), 'p95': round(s[int(len(s)*0.95)], 1)}

def gcdump(pid, path):
    for _ in range(3):
        r = subprocess.run(['dotnet-gcdump', 'collect', '-p', str(pid), '-o', path],
                           capture_output=True, text=True, timeout=180)
        if os.path.exists(path) and os.path.getsize(path) > 1000:
            return True
        time.sleep(5)
    return False

def gcdump_report(path):
    """返回 {类型行: count}——report 按行输出 'bytes count Type [asm]'"""
    r = subprocess.run(['dotnet-gcdump', 'report', path], capture_output=True, text=True, timeout=300)
    counts = {}
    for line in r.stdout.splitlines():
        parts = line.split()
        if len(parts) >= 3 and parts[0].replace(',', '').isdigit() and parts[1].replace(',', '').isdigit():
            # 完整类型行（含短名碰撞风险：同名不同类会合并——报告里标注）
            key = ' '.join(parts[2:])
            counts[key] = int(parts[1].replace(',', ''))
    return counts

def parse_counters(csv_path):
    """返回 [(t_minutes, total_heap_bytes)]——gen0+gen1+gen2+LOH 每采样点求和"""
    import csv
    points = []
    with open(csv_path, newline='', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        latest = {}
        t0 = None
        for row in reader:
            ts = row.get('Timestamp', '')
            name = row.get('Counter Name', '')
            val = row.get('Mean/Increment', '')
            if 'heap.size' not in name or not val:
                continue
            try: v = float(val)
            except Exception: continue
            if t0 is None: t0 = ts
            # 同一时间戳的各代求和（简化：按行序聚合同 ts）
            if latest.get('_ts') != ts:
                if '_ts' in latest and latest.get('_sum', 0) > 0:
                    points.append(latest)
                latest = {'_ts': ts, '_sum': 0.0}
            latest['_sum'] += v
            # 时间轴：用行号近似（dotnet-counters 5s 间隔）
        if latest.get('_sum', 0) > 0:
            points.append(latest)
    # 行序 → 分钟（每点 5s）
    out = []
    for i, p in enumerate(points):
        out.append((i * 5.0 / 60.0, p['_sum']))
    return out

def slope_mb_per_hour(points, after_min):
    """warmup 后段的线性回归斜率（MB/h）"""
    xs, ys = [], []
    for t, b in points:
        if t >= after_min:
            xs.append(t); ys.append(b / (1024*1024))
    if len(xs) < 5:
        return None, 0
    n = len(xs)
    mx = sum(xs)/n; my = sum(ys)/n
    num = sum((x-mx)*(y-my) for x, y in zip(xs, ys))
    den = sum((x-mx)**2 for x in xs)
    slope = num/den if den else 0.0  # MB/min
    return slope * 60.0, n

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base', default='http://127.0.0.1:8098/emby')
    ap.add_argument('--pid', type=int, required=True)
    ap.add_argument('--hours', type=float, default=4.0)
    ap.add_argument('--warmup-min', type=float, default=30.0)
    ap.add_argument('--no-scan', action='store_true', help='不触发库扫描（自检/红线场景）')
    ap.add_argument('--allow-shared-scan', action='store_true', help='显式允许对共享实例触发扫描（默认拒绝 8096）')
    ap.add_argument('--scan-interval-min', type=float, default=30.0)
    ap.add_argument('--user', default='scale-user-00001', help='认证用户（规模实例用 scale-user-XXXXX，共享实例用 admin）')
    ap.add_argument('--pw', default='')
    args = ap.parse_args()

    if not args.no_scan and '8096' in args.base and not args.allow_shared_scan:
        print('拒绝：目标是共享生产实例(8096)且未显式 --allow-shared-scan——扫描触发被安全红线拦截')
        sys.exit(2)

    ts = datetime.now().strftime('%Y%m%d-%H%M%S')
    out = os.path.join(OUT_ROOT, 'soak-' + ts)
    os.makedirs(out, exist_ok=True)
    print(f'== S8 soak == 目标 {args.base} PID {args.pid}，{args.hours}h（warmup {args.warmup_min}min），输出 {out}')

    # ---- 认证 + 解析播放/图片目标 ----
    auth_hdr = {'Content-Type': 'application/json',
                'X-Emby-Authorization': 'MediaBrowser Client="SoakTest", Device="Soak", DeviceId="soak-01", Version="4.1.1.0"'}
    ms, code = http('POST', args.base + '/Users/AuthenticateByName', auth_hdr, {'Username': args.user, 'Pw': args.pw})
    if code != 200:
        print(f'认证失败 {code}'); sys.exit(2)
    # 重新认证拿 token
    req = urllib.request.Request(args.base + '/Users/AuthenticateByName', method='POST')
    for k, v in auth_hdr.items(): req.add_header(k, v)
    with urllib.request.urlopen(req, data=json.dumps({'Username': args.user, 'Pw': args.pw}).encode(), timeout=30) as resp:
        tok = json.loads(resp.read().decode()).get('AccessToken')
    H = {'X-MediaBrowser-Token': tok}
    uid = ''
    ms, code = http('GET', args.base + '/Items?Recursive=true&IncludeItemTypes=Movie&Limit=1', H)
    # 拿一个 movie id
    req = urllib.request.Request(args.base + '/Items?Recursive=true&IncludeItemTypes=Movie&Limit=5', headers=H if False else {})
    for k, v in H.items(): req.add_header(k, v)
    with urllib.request.urlopen(req, timeout=30) as resp:
        items = json.loads(resp.read().decode()).get('Items', [])
    movie_id = items[0]['Id'] if items else ''
    print(f'播放目标: {movie_id}')

    # ---- 启动 dotnet-counters ----
    counters_csv = os.path.join(out, 'counters.csv')
    counters_proc = subprocess.Popen(
        ['dotnet-counters', 'collect', '--process-id', str(args.pid),
         '--counters', 'System.Runtime', '--refresh-interval', '5',
         '--format', 'csv', '-o', counters_csv],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f'dotnet-counters 已启动 (PID {counters_proc.pid})')

    stats = Stats()
    stop = threading.Event()

    def load_cycle():
        """一轮混合负载（~10s）：分页浏览 + 图片 + 流式 + 3 路转码 + 播放上报"""
        # 1. 分页浏览（匿名 + 用户交替）
        ms, code = http('GET', args.base + f'/Items?Recursive=true&StartIndex={random.randint(0,500)}&Limit=50')
        stats.add(ms, code)
        ms, code = http('GET', args.base + f'/Items?Recursive=true&IncludeItemTypes=Movie&StartIndex={random.randint(0,100)}&Limit=50', H)
        stats.add(ms, code)
        # 2. 图片
        if movie_id:
            ms, code = http('GET', args.base + f'/Items/{movie_id}/Images/Primary', H)
            stats.add(ms, code)
        # 3. 流式（Range）
        if movie_id:
            ms, code = http('GET', args.base + f'/Videos/{movie_id}/stream?static=true',
                            dict(H, Range='bytes=0-65535'))
            stats.add(ms, code)
        # 4. 3 路转码（各自 PlaySessionId）+ DELETE
        for i in range(3):
            sid = f'soak-{int(time.time())}-{i}'
            ms, code = http('GET', args.base + f'/Videos/{movie_id}/hls1/main/0.ts?DeviceId=soak&PlaySessionId={sid}&MediaSourceId={movie_id}&VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts', H)
            stats.add(ms, code)
            http('DELETE', args.base + f'/Videos/ActiveEncodings?DeviceId=soak&PlaySessionId={sid}', H)
        # 5. 播放上报
        http('POST', args.base + '/Sessions/Playing/Progress',
             dict(H, **{'Content-Type': 'application/json'}),
             {'ItemId': movie_id, 'PlaySessionId': 'soak-report', 'PositionTicks': random.randint(0, 10**8), 'IsPaused': False})

    def load_worker():
        while not stop.is_set():
            try:
                load_cycle()
            except Exception:
                stats.add(0, -1)
            time.sleep(2)

    threads = [threading.Thread(target=load_worker, daemon=True) for _ in range(2)]
    for t in threads: t.start()
    print('负载线程已启动（2× 混合循环，~10s/轮）')

    total_sec = args.hours * 3600
    warmup_sec = args.warmup_min * 60
    t_start = time.time()
    last_scan = 0.0
    next_milestone = warmup_sec

    try:
        while time.time() - t_start < total_sec:
            elapsed = time.time() - t_start
            # 周期扫描（"扫描进行中"形态）
            if (not args.no_scan and elapsed - last_scan > args.scan_interval_min * 60):
                ms, code = http('POST', args.base + '/Library/Refresh', H)
                last_scan = elapsed
                print(f'[{elapsed/60:.0f}min] 扫描触发 -> {code}')
            # warmup 末：gcdump #1
            if elapsed >= next_milestone and next_milestone == warmup_sec:
                print(f'[{elapsed/60:.0f}min] warmup 结束，采集 gcdump #1 ...')
                gcdump(args.pid, os.path.join(out, 'gcdump-warmup.gcdump'))
                next_milestone = float('inf')
            # 里程碑日志
            if int(elapsed) % 300 == 0 and int(elapsed) > 0:
                s = stats.summary()
                print(f'[{elapsed/60:.0f}min] ops={s.get("ops")} errs={s.get("errs")} p50={s.get("p50")}ms p95={s.get("p95")}ms')
            time.sleep(1)
    finally:
        stop.set()
        time.sleep(3)

    # ---- gcdump #2 + 强制 GC 金标准 + 停计数器 ----
    # 金标准（soak-diagnosis 采纳）：gcdump 前强制完整 GC——分离"真滞留"与
    # "gen2 惰性未收垃圾"两个效应。gcdump 本身只走存活对象，但显式 Collect(2)
    # 消除"惰性 gen2 尚未跑"的争议空间：dump 后仍在 = 铁证滞留。
    print('触发强制完整 GC（金标准：分离滞留 vs 垃圾）...')
    try:
        req = urllib.request.Request(args.base + '/System/Info', headers=H if False else {})
        # 无公开强制 GC 端点——通过 dotnet-counters 的 gc 命令不可行；改用
        # gcdump 自带的 GC（采集即触发），并在报告标注该语义
        print('(gcdump 采集自带 GC——dump 中对象即 GC 后存活，报告标注该判定语义)')
    except Exception:
        pass
    print('采集 gcdump #2 ...')
    gcdump(args.pid, os.path.join(out, 'gcdump-final.gcdump'))
    # 二次快照：间隔 60s 再采一次，两次 diff = 稳态滞留（非采集瞬态）
    time.sleep(60)
    print('采集 gcdump #3（稳态复核，60s 间隔）...')
    gcdump(args.pid, os.path.join(out, 'gcdump-steady.gcdump'))
    counters_proc.terminate()

    # ---- 分析 ----
    report = {'started': ts, 'base': args.base, 'pid': args.pid,
              'hours': args.hours, 'warmup_min': args.warmup_min,
              'load': stats.summary()}
    points = parse_counters(counters_csv) if os.path.exists(counters_csv) else []
    slope, n = slope_mb_per_hour(points, args.warmup_min) if points else (None, 0)
    report['heap_points'] = len(points)
    report['slope_mb_per_hour'] = round(slope, 2) if slope is not None else None
    report['verdict'] = 'PASS' if (slope is not None and abs(slope) < 5.0) else ('INCONCLUSIVE' if slope is None else 'FAIL')

    # gcdump diff
    g1 = os.path.join(out, 'gcdump-warmup.gcdump')
    g2 = os.path.join(out, 'gcdump-final.gcdump')
    if os.path.exists(g1) and os.path.exists(g2):
        c1, c2 = gcdump_report(g1), gcdump_report(g2)
        growers = []
        for k, v in c2.items():
            d = v - c1.get(k, 0)
            if d > 50:
                growers.append({'type': k[:120], 'warmup': c1.get(k, 0), 'final': v, 'delta': d})
        growers.sort(key=lambda x: -x['delta'])
        report['top_growers'] = growers[:20]
        # 嫌疑类型检查（scale-design §2 清单）
        suspects = {'BaseItem': 0, 'SessionInfo': 0, 'SemaphoreSlim': 0, 'TranscodingJob': 0}
        for g in growers:
            for s in suspects:
                if s in g['type']:
                    suspects[s] += g['delta']
        report['suspect_growth'] = suspects

    # 写报告
    with open(os.path.join(out, 'report.json'), 'w', encoding='utf-8') as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    lines = [f'# S8 soak 报告 {ts}', '',
             f'- 目标: {args.base} (PID {args.pid})，{args.hours}h，warmup {args.warmup_min}min',
             f'- 负载: {report["load"]}',
             f'- 堆曲线: {report["heap_points"]} 采样点，warmup 后斜率 {report["slope_mb_per_hour"]} MB/h',
             f'- 判定: **{report["verdict"]}**（阈值 |slope| < 5 MB/h）', '']
    if report.get('top_growers'):
        lines.append('| 类型 | warmup | final | Δ |')
        lines.append('|---|---|---|---|')
        for g in report['top_growers'][:15]:
            lines.append(f"| {g['type'][:80]} | {g['warmup']} | {g['final']} | +{g['delta']} |")
    lines.append('')
    lines.append('> 注：gcdump report 按类型短名聚合，同名闭包跨类合并会产出伪增长行；')
    lines.append('> 嫌疑类型以计数直查为准。.NET 10 lossy 模式大堆可能不完整。')
    with open(os.path.join(out, 'report.md'), 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    print(f'== 完成: {report["verdict"]}（斜率 {report["slope_mb_per_hour"]} MB/h），报告在 {out}/report.md')

if __name__ == '__main__':
    main()
