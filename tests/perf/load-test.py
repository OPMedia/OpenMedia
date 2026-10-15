#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
T2.3 负载测试（k6 替代：Python stdlib 实现，无第三方依赖）
场景（docs/phase2-design.md §2.4，时长按在线实例缩减为 30s/档，偏差记录在报告）：
  L1 只读热点：/Items 分页 RPS 阶梯 10→50→100，每档 30s
  L2 扫描并发：POST /Library/Refresh 进行中跑 L1 短脉冲（30s）
  L3 静态流式：stream?static=true + Range，并发 10 × 30s
  L4 认证：AuthenticateByName 并发 20 × 30s
指标：P50/P95/P99、错误率、吞吐。输出 tests/perf/results/load-<ts>.json
"""
import json, time, threading, statistics, uuid, sys, os
import http.client as hc
from datetime import datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from keepalive import make_client

BASE = 'http://127.0.0.1:8096/emby'
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results')

_ka = make_client(BASE, timeout=30)

def http(method, url, headers=None, body=None, timeout=30):
    """v2 keep-alive：每线程一条持久连接（消除连接层伪影）"""
    path = url[len(BASE):] if url.startswith(BASE) else url
    data = json.dumps(body).encode() if body is not None else None
    hdrs = dict(headers or {})
    if data is not None:
        hdrs['Content-Type'] = hdrs.get('Content-Type', 'application/json')
    code, ms, _ = _ka.request(method, path, hdrs, data)
    return ms, code

def authenticate():
    auth_hdrs = {'Content-Type': 'application/json',
                 'X-Emby-Authorization': 'MediaBrowser Client="PerfTest", Device="LoadGen", DeviceId="perf-01", Version="4.1.1.0"'}
    ms, code = http('POST', BASE + '/Users/AuthenticateByName', auth_hdrs, {'Username': 'admin', 'Pw': ''})
    if code != 200:
        ms, code = http('POST', BASE + '/Users/AuthenticateByName', auth_hdrs, {'Username': 'admin', 'Pw': 'admin'})
    if code != 200:
        print('auth failed', code); sys.exit(1)
    # token: 再发一次读 body（认证幂等）
    conn = hc.HTTPConnection('127.0.0.1', 8096, timeout=30)
    try:
        conn.request('POST', '/emby/Users/AuthenticateByName', body=json.dumps({'Username': 'admin', 'Pw': ''}), headers=auth_hdrs)
        resp = conn.getresponse()
        tok = json.loads(resp.read().decode())['AccessToken']
    finally:
        conn.close()
    return tok

def pct(lat, p):
    if not lat: return 0.0
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
        return {
            'label': label, 'requests': n, 'durationS': round(dur, 1),
            'rps': round(n / dur, 1) if dur > 0 else 0,
            'p50Ms': round(pct(self.lat, 50), 1), 'p95Ms': round(pct(self.lat, 95), 1),
            'p99Ms': round(pct(self.lat, 99), 1),
            'errorRate': round(errs * 100.0 / n, 2) if n else 100.0,
            'codes': {str(k): v for k, v in self.codes.items()},
        }

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
            if wait > 0: time.sleep(wait)
    threads = [threading.Thread(target=worker) for _ in range(min(rps, 32))]
    t0 = time.time()
    for t in threads: t.start()
    for t in threads: t.join()
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
    for t in threads: t.start()
    for t in threads: t.join()
    return col.summary(label, time.time() - t0)

def main():
    tok = authenticate()
    hdr = {'X-Emby-Token': tok}
    uid = 'ac49142474fd4c49b85427f29fc4ba9b'
    results = []
    print('== L1 只读热点（阶梯 10→50→100 RPS，每档 30s）==')
    for rps in (10, 50, 100):
        fn = lambda: http('GET', BASE + f'/Users/{uid}/Items?Recursive=true&Limit=50&StartIndex=0', hdr)
        s = run_rps(fn, rps, 30, f'L1-Items分页-{rps}rps')
        results.append(s); print(json.dumps(s, ensure_ascii=False))

    print('== L1b Ids 批量 + 图片端点（50 RPS × 30s）==')
    def mixed():
        import random
        if random.random() < 0.5:
            return http('GET', BASE + '/Items?Recursive=true&Limit=20&Fields=Path', hdr)
        return http('GET', BASE + '/Items?Ids=c40cea18fc9c413b93aca7bad1a80d92', hdr)
    s = run_rps(mixed, 50, 30, 'L1b-Ids批量-50rps')
    results.append(s); print(json.dumps(s, ensure_ascii=False))

    print('== L2 扫描并发（Refresh 进行中 L1 脉冲 50 RPS × 30s）==')
    ms, code = http('POST', BASE + '/Library/Refresh?api_key=' + tok, hdr)
    print('Library/Refresh =>', code)
    fn = lambda: http('GET', BASE + f'/Users/{uid}/Items?Recursive=true&Limit=50', hdr)
    s = run_rps(fn, 50, 30, 'L2-扫描并发-50rps')
    results.append(s); print(json.dumps(s, ensure_ascii=False))

    print('== L3 静态流式（stream?static=true + Range，并发 10 × 30s）==')
    vid = 'c40cea18fc9c413b93aca7bad1a80d92'
    def stream_fn():
        h = dict(hdr); h['Range'] = 'bytes=0-1048575'
        return http('GET', BASE + f'/Videos/{vid}/stream?static=true', h, timeout=60)
    s = run_concurrent(stream_fn, 10, 30, 'L3-静态流-并发10')
    results.append(s); print(json.dumps(s, ensure_ascii=False))

    print('== L4 认证（AuthenticateByName 并发 20 × 30s）==')
    def auth_fn():
        return http('POST', BASE + '/Users/AuthenticateByName',
                    {'Content-Type': 'application/json',
                     'X-Emby-Authorization': 'MediaBrowser Client="PerfTest", Device="LoadGen", DeviceId="perf-' + uuid.uuid4().hex[:8] + '", Version="4.1.1.0"'},
                    {'Username': 'admin', 'Pw': ''})
    s = run_concurrent(auth_fn, 20, 30, 'L4-认证-并发20')
    results.append(s); print(json.dumps(s, ensure_ascii=False))

    # 服务器存活
    ms, code = http('GET', BASE + '/System/Ping', hdr)
    out = {
        'runAt': datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
        'tool': 'python-stdlib-loadgen (k6 未安装的替代，等价口径)',
        'note': '每档时长 30s（设计值 60s，在线实例缩减，偏差已记录）',
        'serverAliveAtEnd': code == 200,
        'scenarios': results,
    }
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, 'load-%s.json' % datetime.now().strftime('%Y%m%d-%H%M%S'))
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    with open(os.path.join(OUT_DIR, 'load-latest.json'), 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print('结果已写入', path)

if __name__ == '__main__':
    main()
