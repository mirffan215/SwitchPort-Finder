#!/usr/bin/env python3
"""Small authenticated load test (stdlib). python tools/security/loadtest.py --base https://host:8450 --admin admin --password '...' [--threads 20 --each 50]
Reports status codes and latency percentiles per endpoint. Use a test instance: it hammers read-only endpoints only."""
import argparse, http.client, json, ssl, statistics, threading, time, urllib.parse
ap = argparse.ArgumentParser(); ap.add_argument("--base", default="http://127.0.0.1:5000"); ap.add_argument("--admin", default="admin"); ap.add_argument("--password", required=True)
ap.add_argument("--threads", type=int, default=20); ap.add_argument("--each", type=int, default=50); A = ap.parse_args(); U = urllib.parse.urlparse(A.base)
def conn(): return http.client.HTTPSConnection(U.hostname, U.port or 443, timeout=30, context=ssl._create_unverified_context()) if U.scheme == "https" else http.client.HTTPConnection(U.hostname, U.port or 80, timeout=30)
def call(method, path, cookie=None, body=None):
    c = conn(); h = {"Origin": f"{U.scheme}://{U.netloc}", "Content-Type": "application/json"}
    if cookie: h["Cookie"] = cookie
    t = time.perf_counter(); c.request(method, path, body=json.dumps(body) if body else None, headers=h); r = c.getresponse(); r.read(); dt = (time.perf_counter() - t) * 1000
    ck = [v.split(";")[0] for k, v in r.getheaders() if k.lower() == "set-cookie"]; c.close(); return r.status, dt, ck
s, _, ck = call("POST", "/api/auth/login", body={"username": A.admin, "password": A.password}); assert s == 200, f"login failed {s}"; cookie = ck[0]
paths = ["/api/dashboard", "/api/switches", "/api/devices?pageSize=50", "/api/search?q=sw", "/api/topology", "/api/metrics/top-ports?metric=utilization", "/api/metrics/top-ports?metric=drops", "/", "/api/health"]
res = {p: [] for p in paths}; lock = threading.Lock()
def worker():
    for i in range(A.each):
        p = paths[i % len(paths)]; s, dt, _ = call("GET", p, cookie)
        with lock: res[p].append((s, dt))
t0 = time.time(); ts = [threading.Thread(target=worker) for _ in range(A.threads)]; [t.start() for t in ts]; [t.join() for t in ts]; el = time.time() - t0
tot = sum(len(v) for v in res.values()); bad = 0
print(f"{tot} requests, {A.threads} concurrent clients, {el:.1f}s = {tot / el:.0f} req/s")
for p, v in res.items():
    d = sorted(x[1] for x in v); codes = {}
    for s, _ in v: codes[s] = codes.get(s, 0) + 1
    if any(s >= 500 for s, _ in v): bad += 1
    print(f"  {p:48} n={len(v):4} p50={statistics.median(d):6.0f}ms p95={d[int(len(d) * .95) - 1]:6.0f}ms max={d[-1]:6.0f}ms  {codes}")
print("RESULT:", "FAIL (5xx seen)" if bad else "OK (no 5xx)"); raise SystemExit(1 if bad else 0)
