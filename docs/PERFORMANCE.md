# Performance

**Design target:** 500 switches and 50,000+ devices. Async I/O, bounded parallel scans, indexed queries, server-side paging, batched upserts, cached DNS.

## Method
`PerformanceTests` (integration test project; **test project only**, data is generated and written straight to the database) seeds 500 switches × 48 ports (24,000 ports), 50,000 devices with their sightings, 499 LLDP uplinks in a 10-ary tree, then times real HTTP calls (best of 3 after a warm-up) against the running API.
Run it: `dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"`.

> The same test now runs against **real SQL Server 2022** too (Docker, `tools/sqlserver/run-sqlserver-tests.sh --perf`, or set `SPF_TEST_SQLSERVER` yourself): the SQL Server table below is the one to size from.
> Both were measured on a 4-core/16 GB development VM with the database on the same machine and a single user — real servers add network latency and concurrency, so treat these as a lower bound.

## Results on SQL Server 2022 (Docker, same host)

| Operation (500 switches / 24,000 ports / 50,000 devices) | Time |
|---|---|
| Device list: page 1 / by IP / by state / by switch | 4–10 ms |
| Device list sorted by hostname, page 500 | 35 ms |
| Device list text filter (`host-4999`, `LIKE '%…%'`) | 541 ms |
| Search: hostname / IP / full MAC / partial MAC / switch name / port name / no match | 156–247 ms |
| Switch list (500) / one switch's ports / dashboard | 15 / 10 / 52 ms |
| Topology (500 nodes, 499 links) | 71 ms |
| Trace in the 500-switch graph / device details | 17 / 4 ms |
| Export all 50k devices: CSV / Excel | 0.5 s / 3.0 s |
| Scan write: 2,000 MACs + 2,000 ARP + 48 ports into the full database | 2.4 s |
| Rescan of unchanged data | 1.5 s |
| Global classification after a scan (all 500 switches) | **0.33 s** per run |
| Seeding the test data (50k devices + sightings) | 26 s |

Search is the slowest interactive operation (~0.2 s) because `LIKE '%text%'` scans `Devices`; that is comfortable at 50k rows. Beyond a few hundred thousand devices add a full-text or trigram index.

## Results on SQLite (for comparison)

| Operation (500 switches / 50k devices) | Time |
|---|---|
| Device list, page 1 / sorted by hostname page 500 / by IP / filtered by state / by switch | 2–3 ms |
| Device list text filter (`host-4999`, `LIKE '%…%'`) | ~120 ms |
| Search: hostname / IP / full MAC (any format) / partial MAC / switch name / port name / no match | 34–47 ms |
| Switch list (500) / one switch's ports / dashboard | 10 / 4 / 13 ms |
| Topology (500 nodes, 499 links) | 71 ms |
| Trace in the 500-switch graph / device details | 15 / 2 ms |
| Export all 50k devices: CSV / Excel | 0.8 s / 3.9 s |
| Scan write: 2,000 MACs + 2,000 ARP + 48 ports into the full database | 1.4 s |
| Rescan of unchanged data | 1.0 s (no duplicates, unchanged rows not rewritten) |
| Global classification after a scan (all 500 switches, 24k ports, 50k sightings) | **~0.4 s** per run |

## What the load test found and fixed
Global classification originally took **8.2 s per run** (quadratic lookups and tracking all 24,000 ports); because it runs after every scan, "scan all" would have spent over an hour in it. It now reads untracked projections and touches only changed rows: **~20× faster**. Classification (and scan persistence) is **serialised** by design (it prevents duplicate-device races), so a full-fleet scan adds roughly `500 × 0.4 s ≈ 3.5 min` of serial database work on top of the SNMP time, which itself runs `MaxParallelScans` at a time.

## Tuning
* `Discovery:MaxParallelScans` (default 5): raise for faster fleet scans if the network and SQL Server cope.
* SNMP timeout / retries / max-repetitions (Settings → General): the dominant cost of a scan is switch response time (large FDB/ARP tables).
* Search uses `LIKE '%text%'`, which scans the `Devices` table. That is milliseconds at 50k rows; at several hundred thousand rows add trigram/full-text indexing.
* Keep SQL Server on fast storage; add the indexes in the migrations (they are created automatically): MAC, IP, hostname, switch/port, last-seen, composite switch+last-seen.
