#!/usr/bin/env bash
# Runs the whole integration suite (and optionally the 500-switch / 50k-device load test) against a REAL SQL Server in Docker,
# through the real EF migrations. Each test fixture gets and drops its own throwaway database.
# Usage: tools/sqlserver/run-sqlserver-tests.sh [--perf]
set -euo pipefail
PW='Spf-Test#2026pw'; NAME=spf-sql-tests; PORT=14333
docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$PW" -e MSSQL_PID=Developer -p $PORT:1433 mcr.microsoft.com/mssql/server:2022-latest >/dev/null
trap 'docker rm -f "$NAME" >/dev/null 2>&1 || true' EXIT
echo -n "Waiting for SQL Server"; for _ in $(seq 1 60); do docker logs "$NAME" 2>&1 | grep -q "ready for client connections" && break; echo -n .; sleep 2; done; echo
export SPF_TEST_SQLSERVER="Server=localhost,$PORT;User Id=sa;Password=$PW;TrustServerCertificate=True"
cd "$(dirname "$0")/../.."
dotnet test tests/SwitchPortFinder.IntegrationTests --filter "Category!=Performance"
if [ "${1:-}" = "--perf" ]; then dotnet test tests/SwitchPortFinder.IntegrationTests --filter "Category=Performance" --logger "console;verbosity=detailed"; fi
