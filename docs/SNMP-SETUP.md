# SNMP setup for SwitchPort Finder

The portal is **strictly read-only**: it only sends SNMP GET / GETNEXT / GETBULK. Give it a **read-only** view and restrict who may query.

> **Honesty note.** The CLI examples below are typical read-only configurations for each vendor. They could not be tested against real devices while this software was built (the discovery logic was verified with net-snmp test agents and scripted replays, and the vendor OIDs and `sysObjectID` values against vendor MIBs and a real-device fingerprint database — see below). **Always check the exact syntax against your OS version's documentation**, apply them in a maintenance window, and test with *Test Connection*.

## What the portal needs

| Data | MIB | Required? |
|---|---|---|
| System, interfaces (names, status, speed, VLAN) | SNMPv2-MIB, IF-MIB (ifXTable), Q-BRIDGE-MIB (`dot1qPvid`) | **yes** (interfaces) |
| MAC table (FDB) | Q-BRIDGE-MIB `dot1qTpFdbTable`, else BRIDGE-MIB `dot1dTpFdbTable` (+ `dot1dBasePortIfIndex`) | **yes** |
| IP addresses | IP-MIB `ipNetToPhysicalTable` / `ipNetToMediaTable` on the **core / Layer 3** device | for IPs |
| Neighbors | LLDP-MIB (all vendors); Cisco CDP-MIB (Cisco only, optional) | recommended |

Use a view that includes at least `iso.3.6.1.2.1` (MIB-2), `iso.0.8802.1.1.2` (LLDP) and, on Cisco, `iso.3.6.1.4.1.9.9.23` (CDP), `iso.3.6.1.4.1.9.9.46` (VTP) and `iso.3.6.1.4.1.9.9.68` (VLAN membership). A simple `iso` view is fine for read-only access.

## Restrict SNMP to the portal server (do this everywhere)

Use the vendor's ACL / client-list feature so only the portal's IP can query (examples below use **192.0.2.5** as the portal server, a documentation address — replace it). Prefer **SNMPv3 with auth + privacy** (`SHA` + `AES128` or stronger); SNMPv2c community strings travel in clear text.

## The Cisco per-VLAN requirement

On Cisco IOS/IOS-XE the BRIDGE-MIB forwarding table is **per VLAN**. Without per-VLAN access the MAC table looks empty or incomplete. The portal handles this automatically (after trying the Q-BRIDGE table):

* It reads the active VLAN list (`vtpVlanState`, or `dot1qVlanStaticName`) and skips VLANs 1002–1005.
* **SNMPv2c:** it queries each VLAN with the community **`<community>@<vlan>`** (community string indexing). Nothing extra to configure — the same community works for every `community@vlan`.
* **SNMPv3:** it uses the SNMP **context `vlan-<id>`**. The SNMPv3 group must be allowed to use those contexts (below).
* Per-VLAN queries use short timeouts and a small parallelism (Settings → General) so scans stay fast.

## Cisco IOS / IOS-XE (example)

```text
ip access-list standard SPF-SNMP
 permit host 192.0.2.5
!
! --- SNMPv2c, read-only, restricted ---
snmp-server community CHANGE-ME-LONG-RANDOM RO SPF-SNMP
!
! --- SNMPv3 (preferred): authPriv, read-only ---
snmp-server view SPF-VIEW iso included
snmp-server group SPF-RO v3 priv read SPF-VIEW access SPF-SNMP
snmp-server user spf SPF-RO v3 auth sha AUTH-PASSPHRASE-8+ priv aes 128 PRIV-PASSPHRASE-8+
! per-VLAN contexts for the MAC table over SNMPv3 (verify exact syntax for your release):
snmp-server group SPF-RO v3 priv context vlan- match prefix read SPF-VIEW
!
! --- neighbors ---
lldp run
! CDP is on by default; if disabled:  cdp run
```
Enter the portal credentials as **SNMPv3**: username `spf`, auth `SHA` / passphrase, privacy `AES-128` / passphrase.

## HPE / Aruba

* **ArubaOS-Switch (ProCurve):**
  ```text
  snmp-server community "CHANGE-ME" operator
  snmp-server host 192.0.2.5 community "CHANGE-ME"      ! optional; also restrict with 'restricted' communities / ACLs per your model
  snmpv3 enable
  snmpv3 user spf auth sha AUTH-PASSPHRASE-8+ priv aes PRIV-PASSPHRASE-8+
  snmpv3 group ManagerPriv user spf sec-model ver3       ! group name/privilege differs by firmware; use a READ-ONLY group
  lldp run                                                ! LLDP is normally on by default
  ```
* **AOS-CX:** `snmp-server vrf <vrf>`, `snmp-server community CHANGE-ME`, `snmpv3 user spf auth sha auth-pass ... priv aes priv-pass ...`; LLDP is enabled by default.

## Dell

* **Dell EMC OS10:** `snmp-server community CHANGE-ME ro`, `snmp-server user spf <group> 3 auth sha ... priv aes ...`, `lldp enable` (global) — add an ACL to restrict the source.
* **Dell Networking N-Series (OS6/OS9 style):** `snmp-server community CHANGE-ME ro ipaddress 192.0.2.5`, `lldp transmit` / `lldp receive` on interfaces.

## Juniper (Junos)

```text
set snmp view ALL oid .1 include
set snmp community CHANGE-ME authorization read-only
set snmp community CHANGE-ME view ALL
set snmp community CHANGE-ME clients 192.0.2.5/32
set snmp community CHANGE-ME clients 0.0.0.0/0 restrict
! --- SNMPv3 ---
set snmp v3 usm local-engine user spf authentication-sha authentication-password AUTH-PASSPHRASE-8+
set snmp v3 usm local-engine user spf privacy-aes128 privacy-password PRIV-PASSPHRASE-8+
set snmp v3 vacm security-to-group security-model usm security-name spf group SPF-RO
set snmp v3 vacm access group SPF-RO default-context-prefix security-model usm security-level privacy read-view ALL
! --- neighbors ---
set protocols lldp interface all
```
On EX/QFX the forwarding table is exposed through `dot1qTpFdbTable`/`dot1dTpFdbTable` and LLDP through LLDP-MIB; verify `ipNetToMediaTable` on the L3 device you flag as ARP source.

## Extreme Networks

* **EXOS:**
  ```text
  configure snmp add community readonly CHANGE-ME
  configure snmpv3 add user spf authentication sha AUTH-PASSPHRASE-8+ privacy aes PRIV-PASSPHRASE-8+
  configure snmpv3 add group ReadGrp user spf sec-model usm
  configure snmpv3 add access ReadGrp sec-model usm sec-level priv read-view defaultAdminView
  enable snmp access
  enable lldp ports all
  ```
  Use `configure snmp access-profile` to limit the source to the portal server.
* **VOSS / ERS:** `snmp-server community ... ro`, `snmp-server user ...`, `lldp status txAndRx` per port.

## Other vendors

Huawei, MikroTik, Ubiquiti, Fortinet (FortiSwitch), Netgear, TP-Link, Allied Telesis and anything else work through the **Generic SNMP** path (IF-MIB, Q-BRIDGE/BRIDGE-MIB, IP-MIB, LLDP-MIB). Enable SNMP read-only (v2c community or v3 user), restrict it to the portal address and enable LLDP. If the MAC table is empty, see TROUBLESHOOTING.md.

## Which switch is the "ARP source"?

Access switches normally have **no ARP table for user VLANs**. IP addresses come from the Layer 3 device that routes those VLANs (core/distribution switch or router). Tick **Use as ARP source** on it (it is also auto-detected when `ipForwarding = 1` and the ARP table is non-empty). IPs are joined *globally*: the IP of a PC seen on an access switch is taken from the core's ARP table.

## Vendor auto-detection (verified)

The portal maps `sysObjectID` to a manufacturer. **Every mapping below was checked against sysObjectID values from real devices** as recorded in the community-maintained
LibreNMS device-fingerprint database (`resources/definitions/os_detection/*.yaml`), plus the Huawei root against its vendor MIB (`HUAWEI-MIB: enterprises 2011`).
A manually selected manufacturer always overrides detection, and a mismatch shows a warning. Unknown numbers fall back to Generic SNMP.

| Manufacturer | sysObjectID (1.3.6.1.4.1.…) | Seen on |
|---|---|---|
| Cisco | `9` | IOS, IOS-XE, NX-OS |
| HPE / Aruba | `11` (ProCurve), `25506` (Comware/H3C), `47196` (AOS-CX), `14823` (ArubaOS) | |
| Dell | `674` (PowerConnect, OS10), `6027` (Force10 / DNOS) | |
| Juniper | `2636` | Junos |
| Extreme Networks | `1916` (EXOS/VOSS), `2272` (VOSS), `347.2.5730` (EXOS) | |
| Huawei | `2011` | VRP |
| MikroTik | `14988` | RouterOS |
| Ubiquiti | `41112`, `10002`; and `4413` **only when sysDescr starts with** `EdgeSwitch`, `EdgePoint` or `USW` | UniFi, AirOS, EdgeSwitch |
| Fortinet | `12356` | FortiSwitch |
| Netgear | `4526` | |
| TP-Link | `11863`, `16972` | JetStream |
| Allied Telesis | `207` | AlliedWare Plus |

> **Correction to the original specification.** The spec listed `4413` under Extreme Networks. In real-device fingerprints `4413` is a shared Broadcom/FASTPATH number
> (it identifies Ubiquiti EdgeSwitch only together with a `sysDescr` check), so it is **not** treated as Extreme; alone it stays Generic. Extreme is identified by `1916`, `2272` and `347.2.5730`.

## Vendor-specific discovery (verified OIDs only)

All standards-based discovery lives in the generic path, so every manufacturer — and an unknown one — returns ports, MACs, ARP and LLDP. Model and serial come from ENTITY-MIB first; if that has
nothing, the vendor provider tries these OIDs, each **resolved from the vendor's own MIB file with `snmptranslate`** (never typed from memory):

| Manufacturer | Model | Serial |
|---|---|---|
| Cisco | `CISCO-STACK-MIB::chassisModel` (9.5.1.2.16.0) | `chassisSerialNumberString` (9.5.1.2.19.0) |
| Juniper | `JUNIPER-MIB::jnxBoxDescr` (2636.3.1.2.0) | `jnxBoxSerialNo` (2636.3.1.3.0) |
| MikroTik | `MIKROTIK-MIB::mtxrBoardName` (14988.1.1.7.9.0) | `mtxrSerialNumber` (14988.1.1.7.3.0) |
| Fortinet | — | `FORTINET-CORE-MIB::fnSysSerial` (12356.100.1.1.1.0) |
| Dell | — | `F10-CHASSIS-MIB::chSerialNumber` (6027.3.1.1.1.2), `DELL-NETWORKING-CHASSIS-MIB::dellNetChassisSerialNumber` |
| HPE/Aruba, Extreme, Huawei, Ubiquiti, Netgear, TP-Link, Allied Telesis | ENTITY-MIB only | ENTITY-MIB only |

For the last row no vendor OID could be verified, so none is used (no guessing). Cisco additionally uses per-VLAN FDB queries and CDP, as described above.

## Supporting evidence for uplinks

Besides LLDP/CDP and the MAC-table heuristic, the portal collects **spanning-tree root port** (`dot1dStpRootPort`), **tagged VLAN membership** (Q-BRIDGE static egress/untagged port lists)
and **port descriptions** such as "uplink"/"trunk". These are shown on the port as *Supporting evidence* and may lower the MAC-table heuristic's threshold by one MAC, but they **never classify a port by themselves**.
(STP is read for the default instance only; Cisco per-VLAN STP instances are not queried.)

## Verify from the portal server

```bash
snmpwalk -v2c -c CHANGE-ME 192.0.2.10 1.3.6.1.2.1.1               # system group
snmpwalk -v2c -c CHANGE-ME@10 192.0.2.10 1.3.6.1.2.1.17.4.3.1.2   # Cisco VLAN 10 forwarding table
snmpwalk -v3 -l authPriv -u spf -a SHA -A AUTH-PASS -x AES -X PRIV-PASS 192.0.2.10 1.0.8802.1.1.2.1.4.1.1.9   # LLDP neighbor names
```
