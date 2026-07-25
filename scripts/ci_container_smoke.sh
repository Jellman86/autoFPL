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
selection_valid_request="$(mktemp)"
selection_malformed_request="$(mktemp)"
selection_invalid_request="$(mktemp)"
selection_valid_response="$(mktemp)"
selection_malformed_response="$(mktemp)"
selection_invalid_response="$(mktemp)"
trap 'rm -f "$headers_file" "$invalid_file" "$unknown_file" "$duplicate_file" "$oversized_file" "$oversized_body" "$selection_valid_request" "$selection_malformed_request" "$selection_invalid_request" "$selection_valid_response" "$selection_malformed_response" "$selection_invalid_response"; cleanup' EXIT
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

python3 - "$selection_valid_request" "$selection_malformed_request" "$selection_invalid_request" <<'PY'
import json, pathlib, sys

players = [
    {"playerId": 1, "clubId": 1, "position": "goalkeeper", "priceTenths": 45},
    {"playerId": 2, "clubId": 2, "position": "goalkeeper", "priceTenths": 45},
    {"playerId": 3, "clubId": 1, "position": "defender", "priceTenths": 45},
    {"playerId": 4, "clubId": 2, "position": "defender", "priceTenths": 45},
    {"playerId": 5, "clubId": 3, "position": "defender", "priceTenths": 45},
    {"playerId": 6, "clubId": 4, "position": "defender", "priceTenths": 45},
    {"playerId": 7, "clubId": 5, "position": "defender", "priceTenths": 45},
    {"playerId": 8, "clubId": 1, "position": "midfielder", "priceTenths": 50},
    {"playerId": 9, "clubId": 2, "position": "midfielder", "priceTenths": 50},
    {"playerId": 10, "clubId": 3, "position": "midfielder", "priceTenths": 50},
    {"playerId": 11, "clubId": 4, "position": "midfielder", "priceTenths": 50},
    {"playerId": 12, "clubId": 5, "position": "midfielder", "priceTenths": 50},
    {"playerId": 13, "clubId": 3, "position": "forward", "priceTenths": 60},
    {"playerId": 14, "clubId": 4, "position": "forward", "priceTenths": 60},
    {"playerId": 15, "clubId": 5, "position": "forward", "priceTenths": 60},
]
valid = {
    "budgetTenths": 1000,
    "players": players,
    "startingPlayerIds": [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
    "captainPlayerId": 8,
    "viceCaptainPlayerId": 13,
    "replacementGoalkeeperPlayerId": 2,
    "outfieldSubstitutePlayerIds": [6, 7, 15],
}
malformed = dict(valid, replacementGoalkeeperPlayerId="2")
invalid = dict(valid, replacementGoalkeeperPlayerId=6, outfieldSubstitutePlayerIds=[2, 7, 15])
for path, payload in zip(sys.argv[1:], [valid, malformed, invalid], strict=True):
    pathlib.Path(path).write_text(json.dumps(payload))
PY
selection_valid_status="$(curl --silent --show-error --output "$selection_valid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$selection_valid_request" \
  "$base_url/api/v1/gameweek-selections/validation")"
selection_malformed_status="$(curl --silent --show-error --output "$selection_malformed_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$selection_malformed_request" \
  "$base_url/api/v1/gameweek-selections/validation")"
selection_invalid_status="$(curl --silent --show-error --output "$selection_invalid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$selection_invalid_request" \
  "$base_url/api/v1/gameweek-selections/validation")"

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

python3 - "$selection_valid_response" "$selection_valid_status" "$selection_malformed_status" "$selection_invalid_response" "$selection_invalid_status" <<'PY'
import json, pathlib, sys

valid = json.loads(pathlib.Path(sys.argv[1]).read_text())
valid_status, malformed_status = sys.argv[2:4]
invalid = json.loads(pathlib.Path(sys.argv[4]).read_text())
invalid_status = sys.argv[5]
assert valid_status == "200", valid_status
assert valid["formation"] == "3-5-2", valid
assert valid["replacementGoalkeeperPlayerId"] == 2, valid
assert valid["outfieldSubstitutePlayerIds"] == [6, 7, 15], valid
assert malformed_status == "400", malformed_status
assert invalid_status == "422", invalid_status
assert invalid["code"] == "selection.replacement_goalkeeper.invalid_position", invalid
assert invalid["field"] == "replacementGoalkeeperPlayerId", invalid
PY

printf 'container smoke test passed for %s\n' "$image"
