#!/usr/bin/env bash
# Starts the two test agents (needs net-snmp: `apt-get install snmpd snmp`). Read-only; for demos and manual checks of the portal.
set -euo pipefail
mkdir -p "${1:-lab}"; DIR="$(cd "${1:-lab}" && pwd)"
python3 "$(dirname "$0")/make_lab.py" "$DIR" >/dev/null
snmpd -C -c "$DIR/core.conf" -Lf "$DIR/core.log" -p "$DIR/core.pid"
snmpd -C -c "$DIR/acc.conf"  -Lf "$DIR/acc.log"  -p "$DIR/acc.pid" -I -ifTable,-interfaces
echo "Agents up: CORE-LIVE 127.0.0.1:1161 and ACCESS-LIVE 127.0.0.2:1161, community 'testcomm'."
echo "Run the portal with:  Snmp__Port=1161 Security__AllowLoopbackTargets=true  (loopback targets are blocked unless allowed)"
echo "Stop with:  kill \$(cat $DIR/core.pid $DIR/acc.pid)"
