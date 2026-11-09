# Importing switches

**Settings → Admin → Import Switches** adds or updates many switches from a **CSV** or **Excel (.xlsx)** file. Nothing is saved until you press **Commit**.

## Steps
1. **Download the template** (CSV or Excel). It has the header row and one example row (using the documentation address `192.0.2.10`).
2. Fill it in. Delete or replace the example row.
3. Upload the file, choose *Skip existing* or *Update existing*, press **Preview**.
4. Review every row: **New / Update / Skipped / Error**, with messages and the **row number from your file**. Fix the file and preview again if needed.
5. **Commit.** Then scan the new switches (Switches → *Scan all switches*).

An import is logged (file name, counts, "system" as the user) **without any secrets**. The uploaded file is processed in memory and is **not stored**.

> ⚠ Files containing community strings or passwords are sensitive. Prefer **credential profiles** so the file contains no secrets, and delete your copy of the file after importing.

## Columns

| Column | Required | Values / notes |
|---|---|---|
| `Name` | **yes** | Up to 100 characters. |
| `ManagementIP` | **yes** | IPv4 or IPv6. Rows are matched to existing switches by this. Loopback, `0.0.0.0`, broadcast and multicast are rejected. |
| `Manufacturer` | no | Auto-detect (blank), Cisco, HPE / Aruba, Dell, Juniper, Extreme Networks, Huawei, MikroTik, Ubiquiti, Fortinet, Netgear, TP-Link, Allied Telesis, Other / Generic SNMP. Common spellings are accepted (`HPE`, `aruba`, `TP-Link`, …). An unknown name becomes **Auto-detect with a warning**. |
| `Model` | no | Free text. |
| `SnmpVersion` | with credentials | `v2c` or `v3` (`2c`, `3` also work). |
| `Community` | v2c | The read-only community. |
| `V3Username` | v3 | |
| `V3AuthProtocol` | no | `none`, `MD5`, `SHA`, `SHA256`, `SHA384`, `SHA512`. |
| `V3AuthPassword` | if auth ≠ none | Minimum 8 characters. |
| `V3PrivProtocol` | no | `none`, `DES`, `AES` / `AES128`, `AES192`, `AES256`. Requires an auth protocol. |
| `V3PrivPassword` | if priv ≠ none | Minimum 8 characters. |
| `V3Context` | no | SNMPv3 context name. |
| `CredentialProfile` | no | Name of an existing profile (case-insensitive). **Replaces** the inline credential columns (a warning is shown if both are present). |
| `UseAsArpSource` | no | `true`/`false` (`yes`/`no`/`1`/`0`). Blank leaves it unchanged. |
| `Location`, `Notes` | no | Free text. |

## Examples

```csv
Name,ManagementIP,Manufacturer,Model,SnmpVersion,Community,V3Username,V3AuthProtocol,V3AuthPassword,V3PrivProtocol,V3PrivPassword,V3Context,CredentialProfile,UseAsArpSource,Location,Notes
CORE-SW-01,192.0.2.10,Cisco,Catalyst 9300,,,,,,,,,Core-v3,true,Server room,
ACCESS-SW-01,192.0.2.11,Cisco,,v2c,CHANGE-ME,,,,,,,,false,Floor 1,"uses a comma, quoted"
EDGE-SW-07,192.0.2.17,HPE / Aruba,,v3,,spf,SHA,AUTH-PASS-8+,AES128,PRIV-PASS-8+,,,false,,
LAB-SW,2001:db8::5,,,,,,,,,,,Access-v2c-RO,false,Lab,
```
CSV files may use `,`, `;` or tab as the delimiter (detected from the header) and UTF-8 with or without a BOM.

## Matching: update vs skip

Rows are matched to existing switches by **ManagementIP**.
* **Skip existing** — existing switches are left untouched (status *Skipped*).
* **Update existing** — the switch is updated (status *Update*) instead of duplicated. Blank `Model`, `Location`, `Notes` and `UseAsArpSource` leave the current value alone; **a blank `Manufacturer` is applied as Auto-detect**. **If the row has no credentials and no profile, the existing credentials are kept**; if it has credentials of the same SNMP version, blank secret fields keep their stored value.
* A **new** switch must have credentials or a profile.

## Credential profiles

Settings → Admin → **Credential Profiles** stores named SNMP credential sets (for example `Core-v3`, `Access-v2c-RO`). Secrets are encrypted like switch credentials and are **never shown again** — editing a profile shows "unchanged" placeholders and only replaces what you type. A profile that is still used by switches cannot be deleted.

## Validation

Every problem is reported against the row number in your file: invalid or duplicate IP (duplicates *within the file* are flagged on the second occurrence), unknown profile, `v2c` without a community, `v3` without a username, passwords shorter than 8 characters, privacy without authentication, bad true/false values, unknown manufacturer (warning). Files up to **10 MB** and **10,000 rows** are accepted (the test suite parses and validates a 1,500-row Excel file); an `.xlsx` that expands to more than 200 MB is rejected.

## Export, edit, re-import

**Settings → General** (and Admin) offers *Export switches* as CSV/Excel **without credentials**. Edit it in Excel and re-import with *Update existing*: because credentials are blank, they are kept. (Exports neutralise spreadsheet formula injection by prefixing risky text with `'`.)
