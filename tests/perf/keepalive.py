# -*- coding: utf-8 -*-
"""
负载发生器 v2：线程本地 keep-alive 连接池（消除 urllib 每请求新建连接的
TIME_WAIT/临时端口耗尽伪影——21s 连接超时的根因）。
用法：http_request(method, path, headers, body) -> (status, ms, body_bytes)
每线程一条 HTTP/1.1 持久连接，断线自动重连一次。
"""
import http.client, threading, time

_local = threading.local()

class KeepAliveClient:
    def __init__(self, host, port, timeout=30):
        self.host, self.port, self.timeout = host, port, timeout

    def _get(self):
        conn = getattr(_local, 'conn', None)
        if conn is None or getattr(_local, 'host', None) != (self.host, self.port):
            if conn is not None:
                try: conn.close()
                except Exception: pass
            conn = http.client.HTTPConnection(self.host, self.port, timeout=self.timeout)
            _local.conn = conn
            _local.host = (self.host, self.port)
        return conn

    def request(self, method, path, headers=None, body=None):
        """返回 (status, elapsed_ms, body_bytes)。status=-1 连接失败（含一次重连尝试）。"""
        t0 = time.perf_counter()
        for attempt in (0, 1):
            conn = self._get()
            try:
                conn.request(method, path, body=body, headers=headers or {})
                resp = conn.getresponse()
                data = resp.read()
                return resp.status, (time.perf_counter() - t0) * 1000.0, len(data)
            except Exception:
                try: conn.close()
                except Exception: pass
                _local.conn = None
                if attempt == 1:
                    return -1, (time.perf_counter() - t0) * 1000.0, 0
        return -1, (time.perf_counter() - t0) * 1000.0, 0

def make_client(base_url, timeout=30):
    """从 http://host:port/emby 形式的 base 提取 host/port，返回 KeepAliveClient"""
    from urllib.parse import urlparse
    u = urlparse(base_url)
    host = u.hostname or '127.0.0.1'
    port = u.port or 80
    return KeepAliveClient(host, port, timeout)
