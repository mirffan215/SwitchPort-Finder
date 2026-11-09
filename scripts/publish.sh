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
mkdir -p "$OUT/docs"; cp "$ROOT"/scripts/{install,uninstall,backup}.ps1 "$ROOT/scripts/SpfInstall.Common.ps1" "$OUT/"; cp "$ROOT"/docs/{schema.sql,INSTALLATION.md,CONFIGURATION.md,TROUBLESHOOTING.md} "$OUT/docs/"
echo "==> Done. Run:  cd $OUT && dotnet SwitchPortFinder.Api.dll   (Windows: copy the folder and run .\install.ps1 as Administrator)"
