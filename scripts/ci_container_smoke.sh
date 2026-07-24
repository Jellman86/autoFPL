#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: ci_container_smoke.sh IMAGE [EXPECTED_REVISION]}"
expected_revision="${2:-}"
container=""

cleanup() {
  if [[ -n "$container" ]]; then
    docker rm --force "$container" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

configured_user="$(docker image inspect "$image" --format '{{.Config.User}}')"
[[ "$configured_user" == "1654" ]] || {
  printf 'expected image user 1654, got %s\n' "$configured_user" >&2
  exit 1
}

source_label="$(docker image inspect "$image" --format '{{index .Config.Labels "org.opencontainers.image.source"}}')"
[[ "$source_label" == "https://github.com/Jellman86/autoFPL" ]] || {
  printf 'unexpected source label: %s\n' "$source_label" >&2
  exit 1
}

if [[ -n "$expected_revision" ]]; then
  actual_revision="$(docker image inspect "$image" --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')"
  [[ "$actual_revision" == "$expected_revision" ]] || {
    printf 'expected revision %s, got %s\n' "$expected_revision" "$actual_revision" >&2
    exit 1
  }
fi

container="$(docker run --detach \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,nodev,size=16m \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --publish 127.0.0.1::8080 \
  "$image")"

host_port="$(docker inspect "$container" --format '{{(index (index .NetworkSettings.Ports "8080/tcp") 0).HostPort}}')"
base_url="http://127.0.0.1:${host_port}"

for _ in $(seq 1 60); do
  state="$(docker inspect "$container" --format '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{end}}')"
  if [[ "$state" == "running healthy" ]]; then
    break
  fi
  if [[ "$state" == exited* || "$state" == dead* ]]; then
    docker logs "$container" >&2
    exit 1
  fi
  sleep 1
done

[[ "$(docker inspect "$container" --format '{{.State.Health.Status}}')" == "healthy" ]] || {
  docker inspect "$container" --format '{{json .State.Health}}' >&2
  docker logs "$container" >&2
  exit 1
}

headers_file="$(mktemp)"
health_body="$(curl --fail --silent --show-error --dump-header "$headers_file" "$base_url/healthz")"
ready_body="$(curl --fail --silent --show-error "$base_url/readyz")"
valid_body="$(curl --fail --silent --show-error \
  --header 'Content-Type: application/json' \
  --data '{"schemaVersion":"1.0","sourceType":"manual"}' \
  "$base_url/api/v1/decision-snapshot-metadata/validation")"

invalid_file="$(mktemp)"
unknown_file="$(mktemp)"
duplicate_file="$(mktemp)"
oversized_file="$(mktemp)"
oversized_body="$(mktemp)"
trap 'rm -f "$headers_file" "$invalid_file" "$unknown_file" "$duplicate_file" "$oversized_file" "$oversized_body"; cleanup' EXIT
invalid_status="$(curl --silent --show-error --output "$invalid_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"schemaVersion":"2.0","sourceType":"manual"}' \
  "$base_url/api/v1/decision-snapshot-metadata/validation")"
unknown_status="$(curl --silent --show-error --output "$unknown_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"schemaVersion":"1.0","sourceType":"manual","unexpected":true}' \
  "$base_url/api/v1/decision-snapshot-metadata/validation")"
duplicate_status="$(curl --silent --show-error --output "$duplicate_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"schemaVersion":"1.0","schemaVersion":"2.0","sourceType":"manual"}' \
  "$base_url/api/v1/decision-snapshot-metadata/validation")"
python3 - "$oversized_body" <<'PY'
import json, pathlib, sys
pathlib.Path(sys.argv[1]).write_text(json.dumps({"schemaVersion": "1.0", "sourceType": "manual", "padding": "x" * 17000}))
PY
oversized_status="$(curl --silent --show-error --output "$oversized_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$oversized_body" \
  "$base_url/api/v1/decision-snapshot-metadata/validation")"

python3 - "$health_body" "$ready_body" "$valid_body" "$invalid_file" "$invalid_status" "$unknown_status" "$duplicate_status" "$oversized_status" "$headers_file" <<'PY'
import json, pathlib, sys
health, ready, valid = map(json.loads, sys.argv[1:4])
invalid = json.loads(pathlib.Path(sys.argv[4]).read_text())
invalid_status, unknown_status, duplicate_status, oversized_status = sys.argv[5:9]
headers = pathlib.Path(sys.argv[9]).read_text().lower().splitlines()
assert health == {"status": "healthy"}, health
assert ready == {"status": "ready"}, ready
assert valid == {"schemaVersion": "1.0", "sourceType": "manual"}, valid
assert invalid_status == "422", invalid_status
assert invalid["status"] == 422, invalid
assert invalid["code"] == "snapshot.schema_version.unsupported", invalid
assert invalid["field"] == "schemaVersion", invalid
assert "value" not in invalid, invalid
assert unknown_status == "400", unknown_status
assert duplicate_status == "400", duplicate_status
assert oversized_status == "413", oversized_status
assert not any(line.startswith("server:") for line in headers), headers
PY

printf 'container smoke test passed for %s\n' "$image"
