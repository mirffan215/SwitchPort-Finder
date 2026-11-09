# V1 completion checklist (spec §15C)

**Legend.** ✅ implemented and verified by automated tests, a live run over real SNMP against net-snmp test agents, **and** the whole integration suite on **real SQL Server 2022**.
◐ implemented and verified with a stated limit. **Still not possible to verify here:** physical switches of any vendor (so vendor CLI snippets, a real Cisco's per-VLAN/CDP behaviour and real LLDP encodings remain checked only against scripted replays of the documented MIB structures).

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | Add a switch (with manufacturer choice) | ✅ | UI form; `ApiTests`; vendor list + auto-detect |
| 2 | Bulk import from CSV/Excel (template, preview, commit, credential profiles) | ✅ | `ImportParsingTests`, `ApiTests.Import_*`; zip-bomb and injection tests |
| 3 | Test SNMP (v2c and v3) | ✅ | Live v2c and v3 (SHA+AES) against net-snmp; wrong community/password/user, unreachable |
| 4 | Discover the switch | ✅ | Background queue/worker; live scans; SQL Server run |
| 5 | Display ports | ✅ | Switch page; natural ordering; supporting evidence shown |
| 6 | Display MAC addresses | ✅ | Q-BRIDGE → BRIDGE-MIB → Cisco per-VLAN; bridge-port→ifIndex; live Q-BRIDGE over the wire |
| 7 | Correlate IP addresses (global ARP join) | ✅ | Acceptance topology; live ARP join |
| 8 | Resolve hostnames ("Unknown Device" otherwise) | ✅ | Reverse DNS + timeout/cache; DNS-failure tests |
| 9 | Detect LLDP neighbors | ✅ | `LldpParserTests`; live LLDP over the wire |
| 10 | Detect Cisco CDP neighbors | ◐ | `CdpParserTests`, scripted CDP; OIDs machine-verified against `CISCO-CDP-MIB`. Not run against a real Cisco |
| 11 | Identify switch uplinks (Confirmed / Likely) | ✅ | `UplinkClassifierTests` (hub guard mutation-checked); acceptance topology LLDP on/off; supporting evidence (STP root, tagged VLANs, description) never sufficient alone |
| 12 | Open a neighboring switch from an uplink | ✅ | Remote-port highlight; live UI click-through |
| 13 | Search by MAC, IP, hostname, switch, port | ✅ | `SearchApiTests`; acceptance searches |
| 14 | Open a switch directly from search | ✅ | Cards/dropdown navigate and highlight the port |
| 15 | Trace a device | ✅ | `TraceBuilderTests`; acceptance trace; partial paths |
| 16 | Display the simple topology | ✅ | `TopologyBuilderTests`; React Flow; 500-node load test |
| 17 | Store discovery history | ✅ | Jobs/steps/errors/durations; Discovery page |
| 18 | Export CSV/Excel | ✅ | Filter-aware device export; switches without credentials; formula-injection tests |
| 19 | Non-Cisco manufacturers via dropdown + generic fallback | ✅ | A provider per manufacturer + Generic; `sysObjectID` mappings **verified against real-device fingerprints** (corrected `4413`); vendor model/serial OIDs resolved from vendor MIBs; unknown/unsupported fall back to Generic |
| 20 | Run without login | ✅ | Default mode; *optional* local accounts available |
| 21 | Run without SSL | ✅ | Plain HTTP on :5000; HTTPS by configuration |
| 22 | Run entirely through a web browser | ✅ | React app served by Kestrel; no external URLs; browser contacts only the portal |

## Other requirements
* Acceptance topology passes automatically (LLDP on/off, both scan orders). ✅
* Mock provider for tests only: scripted `ISnmpClient` in tests; wiring test proves production uses the real client and has no Mock/Fake types. ✅
* **Database:** migrations applied from scratch and as an upgrade on real SQL Server 2022; idempotent `schema.sql` run twice; `char(12)` MAC columns; cascade rules checked; full integration suite passes on SQL Server. ✅
* **Performance 500 switches / 50k devices:** measured on real SQL Server (see [PERFORMANCE.md](PERFORMANCE.md)). ✅
* **Security:** see [SECURITY.md](SECURITY.md) and [PENTEST.md](PENTEST.md); API fuzzing clean; dependency audits clean. ✅ (no external human pentest)

## Remaining limits
1. Real vendor hardware (see legend); vendor CLI snippets in `SNMP-SETUP.md` are typical, not device-tested.
2. Vendors without a MIB-verified model/serial OID use ENTITY-MIB only.
3. STP is read for the default instance only (no Cisco per-VLAN STP).
4. No Windows/AD integrated login, SSO or MFA; no external human penetration test.
