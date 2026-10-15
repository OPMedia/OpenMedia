#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
S7 规模化用户压测（10k 用户 × 1M 条目，scale-design §S7）
前置：openmedia_scale 就绪（setup-scale-db.ps1，1M 条目 + 10k local_users + 1M user_datas）；
      一个指向 scale 库的服务器实例（整份复制部署目录 + 环境变量：
      OPENMEDIA_PGCONN=Host=localhost;Port=15432;Database=openmedia_scale;Username=postgres;Password=123456
      端口用 -p 传入，默认 8098）
场景：
  U1 多用户认证阶梯：100/500/2000 并发用户各 AuthenticateByName（空密码，scale 用户无密码）
  U2 每用户列表查询：认证后的用户打 /Users/{uid}/Items?Recursive=true&Limit=50（用户隔离查询）
  U3 每用户 Ids 批量：/Items?Ids=<随机条目>（跨用户共享缓存路径）
指标：P50/P95/P99、错误率、吞吐、服务器存活。
输出：tests/perf/results/s7-user-load-<ts>.json
注意：本脚本只打 HTTP，不动共享库；跑在 scale 实例上，与 8096 共享实例无关。
"""
import json, time, threading, random, urllib.request, urllib.error, os
import sys
from datetime import datetime

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8098
BASE = f'http://127.0.0.1:{PORT}/emby'
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results')
USER_COUNT = 10000  # scale 库用户总数

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
        e.read(); code = e.code
    except Exception:
        code = -1
    return code, (time.perf_counter() - t0) * 1000.0

def auth_user(name):
    """认证一个 scale 用户，返回 (token, userId) 或 None"""
    code, ms = http('POST', BASE + '/Users/AuthenticateByName',
                    {'Content-Type': 'application/json',
                     'X-Emby-Authorization': 'MediaBrowser Client="S7Load", Device="S7", DeviceId="s7-' + name + '", Version="4.1.1.0"'},
                    {'Username': name, 'Pw': ''})
    if code != 200:
        return None
    req = urllib.request.Request(BASE + '/Users/AuthenticateByName', method='POST')
    req.add_header('Content-Type', 'application/json')
    req.add_header('X-Emby-Authorization', 'MediaBrowser Client="S7Load", Device="S7", DeviceId="s7-' + name + '", Version="4.1.1.0"')
    with urllib.request.urlopen(req, json.dumps({'Username': name, 'Pw': ''}).encode(), timeout=30) as r:
        d = json.loads(r.read().decode())
    return d.get('AccessToken'), d.get('User', {}).get('Id')

def pct(lat, p):
    if not lat: return 0.0
    lat = sorted(lat)
    return lat[min(len(lat) - 1, int(len(lat) * p / 100.0))]

def summary(label, lat, codes, dur):
    n = len(lat)
    errs = sum(v for k, v in codes.items() if k not in (200, 204, 206))
    return {'label': label, 'requests': n, 'durationS': round(dur, 1),
            'rps': round(n / dur, 1) if dur > 0 else 0,
            'p50Ms': round(pct(lat, 50), 1), 'p95Ms': round(pct(lat, 95), 1), 'p99Ms': round(pct(lat, 99), 1),
            'errorRate': round(errs * 100.0 / n, 2) if n else 100.0,
            'codes': {str(k): v for k, v in codes.items()}}

def run_users(count, label):
    """U1: count 个不同用户并发认证"""
    lat, codes = [], {}
    lock = threading.Lock()
    names = [f'scale-user-{i:05d}' for i in random.sample(range(1, USER_COUNT + 1), count)]
    results = {}
    def worker(name):
        c, ms = http('POST', BASE + '/Users/AuthenticateByName',
                     {'Content-Type': 'application/json',
                      'X-Emby-Authorization': 'MediaBrowser Client="S7Load", Device="S7", DeviceId="s7-' + name + '", Version="4.1.1.0"'},
                     {'Username': name, 'Pw': ''})
        with lock:
            lat.append(ms); codes[c] = codes.get(c, 0) + 1
    ths = [threading.Thread(target=worker, args=(n,)) for n in names]
    t0 = time.time()
    for t in ths: t.start()
    for t in ths: t.join()
    return summary(label, lat, codes, time.time() - t0)

def run_user_queries(count, label, per_user=5):
    """U2: count 个用户各自查询自己的列表（用户隔离）"""
    lat, codes = [], {}
    lock = threading.Lock()
    names = [f'scale-user-{i:05d}' for i in random.sample(range(1, USER_COUNT + 1), count)]
    def worker(name):
        auth = auth_user(name)
        if not auth:
            with lock: codes[-2] = codes.get(-2, 0) + 1
            return
        tok, uid = auth
        for _ in range(per_user):
            c, ms = http('GET', BASE + f'/Users/{uid}/Items?Recursive=true&Limit=50',
                         {'X-Emby-Token': tok})
            with lock:
                lat.append(ms); codes[c] = codes.get(c, 0) + 1
    ths = [threading.Thread(target=worker, args=(n,)) for n in names]
    t0 = time.time()
    for t in ths: t.start()
    for t in ths: t.join()
    return summary(label, lat, codes, time.time() - t0)

def main():
    results = []
    print(f'== S7 用户压测（scale 实例 {BASE}）==')
    for n in (100, 500, 2000):
        s = run_users(n, f'U1-认证-{n}用户')
        results.append(s); print(json.dumps(s, ensure_ascii=False))
    s = run_user_queries(500, 'U2-用户列表-500用户×5次')
    results.append(s); print(json.dumps(s, ensure_ascii=False))
    c, _ = http('GET', BASE + '/System/Ping')
    out = {'runAt': datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
           'target': BASE, 'note': 'S7 规模化用户压测（scale-design §S7）；前置=scale 实例 + openmedia_scale 库',
           'serverAliveAtEnd': c == 200, 'scenarios': results}
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, 's7-user-load-%s.json' % datetime.now().strftime('%Y%m%d-%H%M%S'))
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print('结果已写入', path)

if __name__ == '__main__':
    main()
