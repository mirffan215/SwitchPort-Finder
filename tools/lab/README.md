# Two-switch SNMP test lab

Read-only net-snmp agents used to check the portal end to end over **real SNMP** without hardware (this is what the live browser checks in the
project history used). Not part of the product; no code here is referenced by it.

```bash
sudo apt-get install -y snmpd snmp
tools/lab/run-lab.sh /tmp/spf-lab
# then run the portal against it:
Snmp__Port=1161 Security__AllowLoopbackTargets=true dotnet run --project src/SwitchPortFinder.Api
```
Add two switches — `127.0.0.1` (tick *Use as ARP source* optional) and `127.0.0.2`, SNMPv2c community `testcomm` — and **Scan all switches**.
Expected: CORE-LIVE `eth0` is a Confirmed (LLDP) uplink to ACCESS-LIVE, ACCESS-LIVE `Gi1/0/24` is an uplink back, and the device
`02:FC:00:00:00:05` sits on ACCESS-LIVE `Gi1/0/15` (VLAN 50); the trace reads CORE-LIVE → eth0 → ACCESS-LIVE → Gi1/0/15.

How it works: `snmpd` serves static BRIDGE-MIB/Q-BRIDGE-MIB, LLDP-MIB and (for ACCESS-LIVE) IF-MIB tables through its `pass` extension
(`pass.py` answers GET/GETNEXT from the generated `*.txt` files). Limits: the `pass` mechanism spawns a process per request, so the first scan after a
cold start can be slow or return truncated tables (the portal then keeps previous port status and shows a warning).

Note: CORE-LIVE reports **this machine's real network interfaces** (only its bridge/LLDP tables are scripted), so the number of ports shown for it depends on your host (e.g. Docker adds `docker0`/veth interfaces).
