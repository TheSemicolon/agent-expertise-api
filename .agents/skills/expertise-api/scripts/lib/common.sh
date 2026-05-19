#!/usr/bin/env bash
# .agents/skills/expertise-api/scripts/lib/common.sh
#
# Shared helpers for the expertise-api skill scripts. Source from each script:
#
#   # shellcheck source=lib/common.sh
#   . "$(dirname "$0")/lib/common.sh"
#
# Provides:
#   - load_secrets       Source ~/.config/expertise-api/secrets.env if present.
#   - require_env        Fail loudly if EXPERTISE_API_BASE_URL/_TOKEN unset.
#   - api_curl ARGS...   Wrap curl with -sS, Bearer auth, and HTTP-status check.
#                        Writes response body to stdout. On non-2xx, writes the
#                        body to stderr along with the status line and exits 1.
#   - urlencode STR      RFC 3986 percent-encoding for query-string values.
#   - require_cmd CMD    Fail loudly if a required CLI is missing.

set -euo pipefail

# Track every temp file created by api_curl across the lifetime of the calling
# process so the EXIT trap cleans them all up. Bash replaces (not appends) the
# EXIT trap on each `trap ... EXIT` call, so installing the trap per-invocation
# would clobber prior entries and leak temp files when a script calls api_curl
# more than once (e.g. skill-smoke-test.sh, which calls it ~6 times).
_API_CURL_TMP_FILES=()
_api_curl_cleanup() {
    if [ "${#_API_CURL_TMP_FILES[@]}" -gt 0 ]; then
        rm -f "${_API_CURL_TMP_FILES[@]}" 2>/dev/null || true
    fi
}
trap _api_curl_cleanup EXIT

load_secrets() {
    local secrets_file="${EXPERTISE_API_SECRETS_FILE:-${HOME}/.config/expertise-api/secrets.env}"
    if [ -f "$secrets_file" ]; then
        # shellcheck disable=SC1090
        . "$secrets_file"
    fi
}

require_env() {
    local missing=0
    if [ -z "${EXPERTISE_API_BASE_URL:-}" ]; then
        echo "error: EXPERTISE_API_BASE_URL is not set" >&2
        missing=1
    fi
    if [ -z "${EXPERTISE_API_TOKEN:-}" ]; then
        echo "error: EXPERTISE_API_TOKEN is not set" >&2
        missing=1
    fi
    if [ "$missing" -ne 0 ]; then
        echo "hint: export the variables or write them to ~/.config/expertise-api/secrets.env" >&2
        exit 2
    fi
    # Strip any trailing slash from the base URL so callers can append paths cleanly.
    EXPERTISE_API_BASE_URL="${EXPERTISE_API_BASE_URL%/}"
    export EXPERTISE_API_BASE_URL
}

require_cmd() {
    local cmd="$1"
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "error: required command '$cmd' not found on PATH" >&2
        exit 2
    fi
}

urlencode() {
    # Pure-bash RFC 3986 percent-encoding. Reserves [A-Za-z0-9._~-].
    local s="${1-}" out="" c
    local i
    for ((i = 0; i < ${#s}; i++)); do
        c="${s:i:1}"
        case "$c" in
            [a-zA-Z0-9._~-]) out+="$c" ;;
            *) printf -v c '%%%02X' "'$c"; out+="$c" ;;
        esac
    done
    printf '%s' "$out"
}

# api_curl PATH [curl-args...]
# - PATH starts with '/' (e.g. /expertise/search?q=foo)
# - Bearer token + Accept: application/json injected.
# - Captures body to a temp file and status code separately so we can
#   surface non-2xx responses with the body verbatim.
api_curl() {
    require_cmd curl
    local path="$1"; shift
    local url="${EXPERTISE_API_BASE_URL}${path}"
    local body_file status
    body_file="$(mktemp -t expertise-api.XXXXXX)"
    _API_CURL_TMP_FILES+=("$body_file")

    status="$(curl -sS \
        -o "$body_file" \
        -w '%{http_code}' \
        -H "Authorization: Bearer ${EXPERTISE_API_TOKEN}" \
        -H 'Accept: application/json' \
        "$@" \
        "$url")"

    case "$status" in
        2??)
            cat "$body_file"
            return 0
            ;;
        *)
            echo "error: HTTP ${status} from ${url}" >&2
            cat "$body_file" >&2
            echo >&2
            return 1
            ;;
    esac
}

# api_curl_status PATH [curl-args...]
# Same as api_curl but writes the HTTP status code to stdout and the response
# body to stderr (used by smoke tests that need to assert on specific status
# codes without treating non-2xx as a hard failure). Returns 0 regardless of
# the HTTP status, so callers must inspect the captured status themselves.
api_curl_status() {
    require_cmd curl
    local path="$1"; shift
    local url="${EXPERTISE_API_BASE_URL}${path}"
    local body_file status
    body_file="$(mktemp -t expertise-api.XXXXXX)"
    _API_CURL_TMP_FILES+=("$body_file")

    status="$(curl -sS \
        -o "$body_file" \
        -w '%{http_code}' \
        -H "Authorization: Bearer ${EXPERTISE_API_TOKEN}" \
        -H 'Accept: application/json' \
        "$@" \
        "$url")"

    printf '%s' "$status"
    cat "$body_file" >&2
}
