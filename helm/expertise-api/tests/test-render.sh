#!/usr/bin/env bash
# test-render.sh — Helm template render assertions for expertise-api chart
#
# Usage: bash helm/expertise-api/tests/test-render.sh
# Exit codes: 0 = all checks passed, 1 = one or more errors, 2 = missing prerequisite

set -euo pipefail

CHART="$(cd "$(dirname "$0")/.." && pwd)"
ERRORS=0
WARNINGS=0

ok()    { echo "OK    [$1] $2"; }
skip()  { echo "SKIP  [$1] $2"; }
warn()  { echo "WARN  [$1] $2"; WARNINGS=$((WARNINGS + 1)); }
err()   { echo "ERROR [$1] $2" >&2; ERRORS=$((ERRORS + 1)); }

if ! command -v helm &>/dev/null; then
    echo "ERROR [prereq] helm not found in PATH" >&2
    exit 2
fi

# 1. ServiceMonitor present when metrics.enabled=true
output=$(helm template test-release "$CHART" --set metrics.enabled=true 2>&1)
if echo "$output" | grep -q "kind: ServiceMonitor"; then
    ok "sm-enabled" "ServiceMonitor renders when metrics.enabled=true"
else
    err "sm-enabled" "ServiceMonitor missing when metrics.enabled=true"
fi

# 2. ServiceMonitor absent when metrics.enabled=false (default)
output=$(helm template test-release "$CHART" 2>&1)
if ! echo "$output" | grep -q "kind: ServiceMonitor"; then
    ok "sm-disabled" "ServiceMonitor absent when metrics.enabled=false"
else
    err "sm-disabled" "ServiceMonitor present when metrics.enabled=false"
fi

# 3. Service port has name 'http'
output=$(helm template test-release "$CHART" 2>&1)
if echo "$output" | grep -q "name: http"; then
    ok "port-name" "Service port name is 'http'"
else
    err "port-name" "Service port name 'http' not found"
fi

# 4. ServiceMonitor references port 'http'
output=$(helm template test-release "$CHART" --set metrics.enabled=true 2>&1)
if echo "$output" | grep -q "port: http"; then
    ok "sm-port-ref" "ServiceMonitor references port 'http'"
else
    err "sm-port-ref" "ServiceMonitor does not reference port 'http'"
fi

# 5. ServiceMonitor scrapes /metrics path
if echo "$output" | grep -q "path: /metrics"; then
    ok "sm-path" "ServiceMonitor scrapes /metrics path"
else
    err "sm-path" "ServiceMonitor /metrics path not found"
fi

echo "=================================="
if [ "$ERRORS" -eq 0 ]; then
    echo "PASS — 0 errors, $WARNINGS warning(s)"
    exit 0
else
    echo "FAIL — $ERRORS error(s), $WARNINGS warning(s)"
    exit 1
fi
