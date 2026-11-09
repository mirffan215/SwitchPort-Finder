#!/usr/bin/env bash
# Builds the React frontend into the API's wwwroot and publishes one self-contained-folder deployment.
# Usage: scripts/publish.sh [output-dir] [runtime-id]      e.g.  scripts/publish.sh ./publish win-x64
set -euo pipefail
OUT="${1:-./publish}"; RID="${2:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
echo "==> Building frontend"
( cd "$ROOT/src/SwitchPortFinder.Web" && npm ci && npm run build )
echo "==> Publishing API (frontend is served from wwwroot by Kestrel)"
ARGS=(-c Release -o "$OUT" "$ROOT/src/SwitchPortFinder.Api")
[ -n "$RID" ] && ARGS+=(-r "$RID" --self-contained false)
dotnet publish "${ARGS[@]}"
echo "==> Done. Run:  cd $OUT && dotnet SwitchPortFinder.Api.dll"
