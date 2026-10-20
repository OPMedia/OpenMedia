#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Debug/Release 同口径 A/B 对比负载（短版）

与 tests/perf/load-test.py 的区别：
  - 只跑 L1 分页 / L1b Ids 批量 / L3 静态流 / L4 认证 四档（不跑 L2 /Library/Refresh，
    因为它会在测试结束后继续后台扫描污染后续档位，perf-baseline.md §7.4 已记录该坑）
  - 每档 15s（A/B 两侧一致即可，绝对值不用于跨方案比较）
  - 用户名从 argv 传入（A/B 两侧必须用同一个用户）
  - 输出 results/ab-<tag>.json，供人工对比 Debug vs Release

用法：
  python tests/perf/load-ab.py --tag debug --user ab-user-001
  python tests/perf/load-ab.py --tag release --user ab-user-001
"""
import json, time, threading, uuid, sys, os, argparse
import http.client as hc
from datetime import datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from keepalive import make_client

BASE = 'http://127.0.0.1:8096/emby'
USER = None
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results')

_ka = make_client(BASE, timeout=30)


def http(method, url, headers=None, body=None, timeout=30):
    path = url[len(BASE):] if url.startswith(BASE) else url
    data = json.dumps(body).encode() if body is not None else None
    hdrs = dict(headers or {})
    if data is not None:
        hdrs['Content-Type'] = hdrs.get('Content-Type', 'application/json')
    code, ms, _ = _ka.request(method, path, hdrs, data)
    return ms, code


def authenticate(username):
    hdrs = {'Content-Type': 'application/json',
            'X-Emby-Authorization': 'MediaBrowser Client="PerfTest", Device="LoadGen", DeviceId="ab-01", Version="4.1.1.0"'}
    for pw in ('', 'admin'):
        ms, code = http('POST', BASE + '/Users/AuthenticateByName', hdrs, {'Username': username, 'Pw': pw})
        if code == 200:
            break
    if code != 200:
        print('auth failed for', username, code)
        sys.exit(1)
    conn = hc.HTTPConnection('127.0.0.1', 8096, timeout=30)
    try:
        conn.request('POST', '/emby/Users/AuthenticateByName', body=json.dumps({'Username': username, 'Pw': pw}), headers=hdrs)
        resp = conn.getresponse()
        tok = json.loads(resp.read().decode())['AccessToken']
    finally:
        conn.close()
    return tok


def pct(lat, p):
    if not lat:
        return 0.0
    lat = sorted(lat)
    return lat[min(len(lat) - 1, int(len(lat) * p / 100.0))]


class Collector:
    def __init__(self):
        self.lat = []
        self.codes = {}
        self.lock = threading.Lock()

    def add(self, ms, code):
        with self.lock:
            self.lat.append(ms)
            self.codes[code] = self.codes.get(code, 0) + 1

    def summary(self, label, dur):
        n = len(self.lat)
        errs = sum(v for k, v in self.codes.items() if k != 200)
        return {'label': label, 'requests': n, 'durationS': round(dur, 1),
                'rps': round(n / dur, 1) if dur > 0 else 0,
                'p50Ms': round(pct(self.lat, 50), 1), 'p95Ms': round(pct(self.lat, 95), 1),
                'p99Ms': round(pct(self.lat, 99), 1),
                'errorRate': round(errs * 100.0 / n, 2) if n else 100.0,
                'codes': {str(k): v for k, v in self.codes.items()}}


def run_rps(fn, rps, seconds, label):
    col = Collector()
    stop = time.time() + seconds
    interval = 1.0 / rps

    def worker():
        while time.time() < stop:
            t_send = time.time()
            ms, code = fn()
            col.add(ms, code)
            wait = interval - (time.time() - t_send)
            if wait > 0:
                time.sleep(wait)
    threads = [threading.Thread(target=worker) for _ in range(min(rps, 32))]
    t0 = time.time()
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    return col.summary(label, time.time() - t0)


def run_concurrent(fn, workers, seconds, label):
    col = Collector()
    stop = time.time() + seconds

    def worker():
        while time.time() < stop:
            ms, code = fn()
            col.add(ms, code)
    threads = [threading.Thread(target=worker) for _ in range(workers)]
    t0 = time.time()
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    return col.summary(label, time.time() - t0)


def resolve_user_id(tok):
    """Find the authenticated user's Id (A/B must use the same user)."""
    conn = hc.HTTPConnection('127.0.0.1', 8096, timeout=30)
    try:
        conn.request('GET', '/emby/Users/Public', headers={'X-Emby-Token': tok})
        payload = json.loads(conn.getresponse().read().decode())
    finally:
        conn.close()
    for u in payload:
        if u.get('Name') == USER:
            return u['Id']
    print('user not found in /Users/Public:', USER)
    sys.exit(1)


def resolve_stream_item(tok):
    """解析一个静态流目标条目。

    返回 (Id, Path, 本机文件是否存在)：
      - 优先选 Path 在本机真实存在的条目（静态流能真跑）
      - 退而求其次返回第一个条目的 Id/Path（调用方按 exists 决定跳过）
      - 库内无 Movie 条目时返回 ('', '', False)

    历史实现硬编码 openmedia 库的 Movie2 Id（c40cea18…），在 openmedia_perf /
    openmedia_compare 上永远打不存在的条目 → L3 档位 100% 500，却一直被当成
    “正常基线”对比（2026-10-20 全量回归定位）。
    """
    conn = hc.HTTPConnection('127.0.0.1', 8096, timeout=30)
    try:
        conn.request('GET', '/emby/Items?Recursive=true&Limit=20&Fields=Path&IncludeItemTypes=Movie',
                     headers={'X-Emby-Token': tok})
        payload = json.loads(conn.getresponse().read().decode())
    finally:
        conn.close()
    items = payload.get('Items') or []
    if not items:
        return '', '', False
    for it in items:
        p = it.get('Path') or ''
        if p and os.path.exists(p):
            return it.get('Id', ''), p, True
    it = items[0]
    return it.get('Id', ''), it.get('Path') or '', False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--tag', required=True, help='debug / release')
    ap.add_argument('--user', required=True, help='必须 A/B 两侧同一个用户名')
    ap.add_argument('--seconds', type=int, default=15)
    args = ap.parse_args()
    global USER
    USER = args.user

    tok = authenticate(args.user)
    hdr = {'X-Emby-Token': tok}
    uid = resolve_user_id(tok)
    print('user id:', uid)
    results = []

    print('== L1 分页 10/50 rps ==')
    for rps in (10, 50):
        fn = lambda: http('GET', BASE + f'/Users/{uid}/Items?Recursive=true&Limit=50&StartIndex=0', hdr)
        s = run_rps(fn, rps, args.seconds, f'L1-Items分页-{rps}rps')
        results.append(s)
        print(json.dumps(s, ensure_ascii=False))

    print('== L1b /Items?Ids= 50rps ==')

    def ids_fn():
        return http('GET', BASE + '/Items?Recursive=true&Limit=20&Fields=Path&IncludeItemTypes=Movie', hdr)
    s = run_rps(ids_fn, 50, args.seconds, 'L1b-Items递归-50rps')
    results.append(s)
    print(json.dumps(s, ensure_ascii=False))

    print('== L3 静态流 并发10 ==')

    # 动态解析目标条目（不再硬编码跨库 Id）；库为合成数据 / 无媒体文件时明确标记跳过，
    # 不再把“打不存在的条目”产出的 100% 500 当成基线数字。
    stream_id, stream_path, file_exists = resolve_stream_item(tok)
    if not stream_id:
        s = Collector().summary('L3-静态流-并发10', 0)
        s['skipped'] = True
        s['reason'] = '库内无 Movie 条目，无法解析静态流目标'
        results.append(s)
        print(json.dumps(s, ensure_ascii=False))
    elif not file_exists:
        s = Collector().summary('L3-静态流-并发10', 0)
        s['skipped'] = True
        s['reason'] = f'库为合成数据，媒体文件不存在（{stream_path}）——静态流档位不适用'
        results.append(s)
        print(json.dumps(s, ensure_ascii=False))
    else:
        def stream_fn():
            h = dict(hdr)
            h['Range'] = 'bytes=0-1048575'
            return http('GET', BASE + f'/Videos/{stream_id}/stream?static=true', h, timeout=60)
        s = run_concurrent(stream_fn, 10, args.seconds, 'L3-静态流-并发10')
        results.append(s)
        print(json.dumps(s, ensure_ascii=False))

    print('== L4 认证 并发20 ==')

    def auth_fn():
        return http('POST', BASE + '/Users/AuthenticateByName',
                    {'Content-Type': 'application/json',
                     'X-Emby-Authorization': 'MediaBrowser Client="PerfTest", Device="LoadGen", DeviceId="ab-' + uuid.uuid4().hex[:8] + '", Version="4.1.1.0"'},
                    {'Username': args.user, 'Pw': ''})
    s = run_concurrent(auth_fn, 20, args.seconds, 'L4-认证-并发20')
    results.append(s)
    print(json.dumps(s, ensure_ascii=False))

    ms, code = http('GET', BASE + '/System/Ping', hdr)
    out = {'runAt': datetime.now().strftime('%Y-%m-%d %H:%M:%S'), 'tag': args.tag,
           'user': args.user, 'tool': 'load-ab.py (A/B 同口径短版)',
           'serverAliveAtEnd': code == 200, 'scenarios': results}
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, 'ab-%s-%s.json' % (args.tag, datetime.now().strftime('%Y%m%d-%H%M%S')))
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    with open(os.path.join(OUT_DIR, 'ab-%s-latest.json' % args.tag), 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print('结果已写入', path)


if __name__ == '__main__':
    main()
