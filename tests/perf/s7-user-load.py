#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
S7 规模化用户压测 v2（keep-alive 会话池版）
v1 的 21s 连接超时是 urllib 每请求新建连接的 TIME_WAIT/端口耗尽伪影；
v2 每线程一条 HTTP/1.1 持久连接（keepalive.py），高并发数字可信。
前置：scale 实例（整份复制部署目录 + OPENMEDIA_PGCONN 指向 openmedia_scale）。
用法：python s7-user-load.py [port]   # 默认 8098
"""
import json, time, threading, random, os, sys
import http.client as hc
from datetime import datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from keepalive import make_client

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8098
BASE = f'http://127.0.0.1:{PORT}'
API_HOST, API_PORT = '127.0.0.1', PORT
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results')
USER_COUNT = 10000

_ka = make_client(BASE, timeout=30)

def http(method, path, headers=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    hdrs = dict(headers or {})
    if data is not None:
        hdrs['Content-Type'] = hdrs.get('Content-Type', 'application/json')
    code, ms, nbytes = _ka.request(method, path, hdrs, data)
    return code, ms, nbytes

def auth_user(name):
    """认证 scale 用户（空密码），返回 (token, userId) 或 None"""
    hdrs = {'Content-Type': 'application/json',
            'X-Emby-Authorization': f'MediaBrowser Client="S7Load", Device="S7", DeviceId="s7-{name}", Version="4.1.1.0"'}
    code, _, _ = http('POST', '/emby/Users/AuthenticateByName', hdrs, {'Username': name, 'Pw': ''})
    if code != 200:
        return None
    # 认证幂等：重发一次读 body
    conn = hc.HTTPConnection(API_HOST, API_PORT, timeout=30)
    try:
        conn.request('POST', '/emby/Users/AuthenticateByName', body=json.dumps({'Username': name, 'Pw': ''}), headers=hdrs)
        resp = conn.getresponse()
        d = json.loads(resp.read().decode())
        return d.get('AccessToken'), d.get('User', {}).get('Id')
    finally:
        conn.close()

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
    lat, codes = [], {}
    lock = threading.Lock()
    names = [f'scale-user-{i:05d}' for i in random.sample(range(1, USER_COUNT + 1), count)]
    def worker(name):
        hdrs = {'Content-Type': 'application/json',
                'X-Emby-Authorization': f'MediaBrowser Client="S7Load", Device="S7", DeviceId="s7-{name}", Version="4.1.1.0"'}
        c, ms, _ = http('POST', '/emby/Users/AuthenticateByName', hdrs, {'Username': name, 'Pw': ''})
        with lock:
            lat.append(ms); codes[c] = codes.get(c, 0) + 1
    ths = [threading.Thread(target=worker, args=(n,)) for n in names]
    t0 = time.time()
    for t in ths: t.start()
    for t in ths: t.join()
    return summary(label, lat, codes, time.time() - t0)

def run_user_queries(count, label, per_user=5):
    lat, codes = [], {}
    lock = threading.Lock()
    names = [f'scale-user-{i:05d}' for i in random.sample(range(1, USER_COUNT + 1), count)]
    def worker(name):
        auth = auth_user(name)
        if not auth:
            with lock: codes[-2] = codes.get(-2, 0) + 1
            return
        tok, uid = auth
        hdrs = {'X-Emby-Token': tok}
        for _ in range(per_user):
            c, ms, _ = http('GET', f'/emby/Users/{uid}/Items?Recursive=true&Limit=50', hdrs)
            with lock:
                lat.append(ms); codes[c] = codes.get(c, 0) + 1
    ths = [threading.Thread(target=worker, args=(n,)) for n in names]
    t0 = time.time()
    for t in ths: t.start()
    for t in ths: t.join()
    return summary(label, lat, codes, time.time() - t0)

def main():
    results = []
    print(f'== S7 用户压测 v2 keep-alive（{BASE}）==')
    for n in (100, 500, 2000):
        s = run_users(n, f'U1-认证-{n}用户')
        results.append(s); print(json.dumps(s, ensure_ascii=False))
    s = run_user_queries(500, 'U2-用户列表-500用户×5次')
    results.append(s); print(json.dumps(s, ensure_ascii=False))
    c, _, _ = http('GET', '/emby/System/Ping')
    out = {'runAt': datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
           'target': BASE, 'generator': 'v2 keep-alive（线程本地持久连接）',
           'serverAliveAtEnd': c == 200, 'scenarios': results}
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, 's7-user-load-%s.json' % datetime.now().strftime('%Y%m%d-%H%M%S'))
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print('结果已写入', path)

if __name__ == '__main__':
    main()
