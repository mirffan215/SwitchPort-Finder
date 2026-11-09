# V1 completion checklist (spec §15C)

**Legend.** ✅ implemented and verified by automated tests **and** a live run against net-snmp test agents over real SNMP. ◐ implemented and verified, with a limit noted.
**Not verified anywhere:** physical switches of any vendor; SQL Server (the SQL Server migrations were generated and statically checked, never applied to a live server); a real Cisco (per-VLAN and CDP were exercised against scripted replays of the documented MIB structures).

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | Add a switch (with manufacturer choice) | ✅ | UI Add/Edit form; `ApiTests.Create_get_update_delete…`; vendor list + auto-detect (`VendorDetectorTests`) |
| 2 | Bulk import from CSV/Excel (template, preview, commit, credential profiles) | ✅ | `ImportParsingTests`, `ApiTests.Import_*`; live preview/commit; zip-bomb and injection tests |
| 3 | Test SNMP (v2c and v3) | ✅ | Live v2c and v3 (SHA+AES) against net-snmp; wrong-community, wrong-password, unknown-user, unreachable cases |
| 4 | Discover the switch | ✅ | Background queue/worker; `DiscoveryApiTests`, live scans |
| 5 | Display ports | ✅ | Switch page; ports endpoint; natural port ordering |
| 6 | Display MAC addresses | ✅ | Q-BRIDGE → BRIDGE-MIB → Cisco per-VLAN (`ScannerTests`), bridge-port→ifIndex mapping; live Q-BRIDGE over the wire |
| 7 | Correlate IP addresses (global ARP join) | ✅ | `Full_scan_shows…joins_ip_and_hostname_globally`; live ARP join |
| 8 | Resolve hostnames ("Unknown Device" otherwise) | ✅ | Reverse DNS with timeout + cache; DNS failure test; never invented |
| 9 | Detect LLDP neighbors | ✅ | `LldpParserTests`; live LLDP over the wire |
| 10 | Detect Cisco CDP neighbors | ◐ | `CdpParserTests`, scripted CDP tables; missing-CDP warning test. **Not run against a real Cisco** |
| 11 | Identify switch uplinks (Confirmed / Likely) | ✅ | `UplinkClassifierTests` (incl. hub guard, mutation-checked); acceptance topology with LLDP on **and** off |
| 12 | Open a neighboring switch from an uplink | ✅ | `ConnectedSwitchId` + remote-port highlight; live UI click-through |
| 13 | Search by MAC, IP, hostname, switch, port | ✅ | `SearchApiTests`, acceptance searches for `AA:BB:CC:DD:EE:01`, `aabb.ccdd.ee01`, `10.10.50.101`, `PC-001` |
| 14 | Open a switch directly from search | ✅ | Result cards and dropdown navigate to the switch page and highlight the port |
| 15 | Trace a device | ✅ | `TraceBuilderTests`; acceptance trace CORE —Gi1/0/24→ ACCESS —Gi1/0/15→ PC-001; partial paths |
| 16 | Display the simple topology | ✅ | `TopologyBuilderTests`, bundled React Flow, live render; 500-node build in the load test |
| 17 | Store discovery history | ✅ | Jobs, steps, errors, durations, counts; Discovery page with filters |
| 18 | Export CSV/Excel | ✅ | Device export honoring filters; switch export without credentials; formula-injection tests |
| 19 | Non-Cisco manufacturers via dropdown + generic fallback | ◐ | Generic provider used for all non-Cisco; unknown/unsupported manufacturer tests. Vendor-specific quirks (model/serial OIDs) are **not** implemented beyond Cisco's per-VLAN/CDP |
| 20 | Run without login | ✅ | By design (`IUserAuthorizationService` allows all) |
| 21 | Run without SSL | ✅ | Plain HTTP on :5000; HTTPS can be enabled by configuration |
| 22 | Run entirely through a web browser | ✅ | React app served by Kestrel; guard tests: no external URLs; live browser run contacts only the portal host |

## Other spec requirements
* Acceptance topology passes automatically (LLDP on/off, both scan orders). ✅
* `MockDiscoveryProvider` for tests only, never registered in production: the test suite uses a scripted `ISnmpClient`; a wiring test asserts production resolves the real client and that no production assembly contains Mock/Fake types. ✅
* Performance target 500 switches / 50k devices: measured on SQLite (see [PERFORMANCE.md](PERFORMANCE.md)); **SQL Server timings still to be measured**. ◐
* Security review and credential-leak tests: see [SECURITY.md](SECURITY.md). ✅

## Known gaps to plan for
1. Run the migrations and the load test against your SQL Server.
2. Verify `sysObjectID` → vendor mappings and the vendor SNMP snippets on real devices.
3. Spanning-tree role / trunk mode / port description are not collected as supporting evidence for uplinks (the spec says they may never be sufficient alone).
4. Vendor-specific model/serial OIDs beyond ENTITY-MIB; vendor providers other than Generic and Cisco carry no extra quirks yet.
5. No authentication (V1), no external penetration test.
