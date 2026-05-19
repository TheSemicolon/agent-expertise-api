#!/usr/bin/env bash
# .agents/skills/expertise-api/scripts/approve.sh
#
# Approve a draft entry: POST /expertise/{id}/approve.
# Requires the caller's token to carry `expertise.write.approve`.

set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib/common.sh
. "${here}/lib/common.sh"

usage() {
    echo "usage: approve.sh <id>" >&2
    exit 2
}

[ $# -eq 1 ] || usage
id="$1"
case "$id" in -h|--help) usage ;; esac

load_secrets
require_env
api_curl "/expertise/$(urlencode "$id")/approve" \
    -X POST
