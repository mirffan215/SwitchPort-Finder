#!/usr/bin/env python3
"""
Generates a tiny two-switch SNMP lab for net-snmp's `snmpd` (read-only test agents; NOT part of the product).

  CORE-LIVE   127.0.0.1:1161  real interface table of this machine (eth0 = ifIndex 4), scripted bridge/ARP-less/LLDP data
  ACCESS-LIVE 127.0.0.2:1161  static IF-MIB with Gi1/0/15 (PC) and Gi1/0/24 (uplink), scripted bridge + LLDP data

Topology the portal should discover:  CORE-LIVE eth0 <-> ACCESS-LIVE Gi1/0/24, and a "PC" (MAC 02:FC:00:00:00:05) on ACCESS-LIVE Gi1/0/15.
Usage:  make_lab.py OUTDIR      (then tools/lab/run-lab.sh OUTDIR)
"""
import os, shutil, sys

out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else "lab")
os.makedirs(out, exist_ok=True)
here = os.path.dirname(os.path.abspath(__file__))
shutil.copy(os.path.join(here, "pass.py"), os.path.join(out, "pass.py"))


def key(r): return [int(x) for x in r[0].split(".")]
def write(name, rows):
    rows.sort(key=key)
    open(os.path.join(out, name), "w").write("\n".join(f"{o}|{t}|{v}" for o, t, v in rows) + "\n")
def mac_oid(m): return ".".join(str(int(m[i:i + 2], 16)) for i in range(0, 12, 2))

PC = "02FC00000005"
B, L, IF, X = "1.3.6.1.2.1.17", "1.0.8802.1.1.2.1", "1.3.6.1.2.1.2.2.1", "1.3.6.1.2.1.31.1.1.1"

def bridge(portmap, fdb, vlan=50):
    r = [(f"{B}.1.4.1.2.{bp}", "integer", ifx) for bp, ifx in portmap]                       # dot1dBasePortIfIndex
    for mac, bp in fdb:
        r += [(f"{B}.7.1.2.2.1.2.{vlan}.{mac_oid(mac)}", "integer", bp),                        # dot1qTpFdbPort
              (f"{B}.7.1.2.2.1.3.{vlan}.{mac_oid(mac)}", "integer", 3)]                         # dot1qTpFdbStatus = learned
    return r + [(f"{B}.7.1.4.3.1.1.{vlan}", "string", "users")]                                 # dot1qVlanStaticName

def lldp(local_port, local_name, sysname, mgmt, remote_port):
    i = f"0.{local_port}.1"
    return [(f"{L}.3.7.1.2.{local_port}", "integer", 5), (f"{L}.3.7.1.3.{local_port}", "string", local_name), (f"{L}.3.7.1.4.{local_port}", "string", local_name),
            (f"{L}.4.1.1.4.{i}", "integer", 7), (f"{L}.4.1.1.5.{i}", "string", sysname + "-CH"), (f"{L}.4.1.1.6.{i}", "integer", 5),
            (f"{L}.4.1.1.7.{i}", "string", remote_port), (f"{L}.4.1.1.8.{i}", "string", remote_port), (f"{L}.4.1.1.9.{i}", "string", sysname),
            (f"{L}.4.1.1.10.{i}", "string", "lab"), (f"{L}.4.1.1.11.{i}", "string", "("), (f"{L}.4.1.1.12.{i}", "string", "("),     # '(' = bridge+router
            (f"{L}.4.2.1.3.{i}.1.4.{mgmt}", "integer", 2)]

write("core_bridge.txt", bridge([(1, 4)], [(PC, 1)]))
write("core_lldp.txt", lldp(4, "eth0", "ACCESS-LIVE", "127.0.0.2", "Gi1/0/24"))
acc = []
for idx, name, alias in [(15, "Gi1/0/15", "PC-LIVE wall jack"), (24, "Gi1/0/24", "uplink to core")]:
    acc += [(f"{IF}.1.{idx}", "integer", idx), (f"{IF}.2.{idx}", "string", name), (f"{IF}.3.{idx}", "integer", 6), (f"{IF}.4.{idx}", "integer", 1500),
            (f"{IF}.5.{idx}", "gauge", 1000000000), (f"{IF}.7.{idx}", "integer", 1), (f"{IF}.8.{idx}", "integer", 1),
            (f"{X}.1.{idx}", "string", name), (f"{X}.15.{idx}", "gauge", 1000), (f"{X}.18.{idx}", "string", alias)]
write("acc_if.txt", acc)
write("acc_bridge.txt", bridge([(15, 15), (24, 24)], [(PC, 15)]))
write("acc_lldp.txt", lldp(24, "Gi1/0/24", "CORE-LIVE", "127.0.0.1", "eth0"))

for name, addr, passes in [
    ("core", "127.0.0.1", [(".1.3.6.1.2.1.17", "core_bridge.txt"), (".1.0.8802.1.1.2", "core_lldp.txt")]),
    ("acc", "127.0.0.2", [(".1.3.6.1.2.1.2.2", "acc_if.txt"), (".1.3.6.1.2.1.31.1.1", "acc_if.txt"), (".1.3.6.1.2.1.17", "acc_bridge.txt"), (".1.0.8802.1.1.2", "acc_lldp.txt")]),
]:
    lines = [f"agentaddress udp:{addr}:1161", "rocommunity testcomm", f"sysName {name.upper()}-LIVE"]
    lines += [f"pass {oid} /usr/bin/python3 {out}/pass.py {f}" for oid, f in passes]
    open(os.path.join(out, f"{name}.conf"), "w").write("\n".join(lines) + "\n")
print("lab written to", out)
