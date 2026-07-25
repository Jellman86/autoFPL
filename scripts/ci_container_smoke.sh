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
  --tmpfs /data:rw,noexec,nosuid,nodev,size=32m,uid=1654,gid=1654 \
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
official_capture_file="$(mktemp)"
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
captaincy_valid_request="$(mktemp)"
captaincy_malformed_request="$(mktemp)"
captaincy_invalid_request="$(mktemp)"
captaincy_duplicate_request="$(mktemp)"
captaincy_valid_response="$(mktemp)"
captaincy_malformed_response="$(mktemp)"
captaincy_invalid_response="$(mktemp)"
captaincy_duplicate_response="$(mktemp)"
substitution_valid_request="$(mktemp)"
substitution_malformed_request="$(mktemp)"
substitution_invalid_request="$(mktemp)"
substitution_valid_response="$(mktemp)"
substitution_malformed_response="$(mktemp)"
substitution_invalid_response="$(mktemp)"
outcome_valid_response="$(mktemp)"
outcome_malformed_response="$(mktemp)"
outcome_invalid_response="$(mktemp)"
score_valid_request="$(mktemp)"
score_malformed_request="$(mktemp)"
score_invalid_request="$(mktemp)"
score_valid_response="$(mktemp)"
score_malformed_response="$(mktemp)"
score_invalid_response="$(mktemp)"
trap 'rm -f "$headers_file" "$invalid_file" "$official_capture_file" "$unknown_file" "$duplicate_file" "$oversized_file" "$oversized_body" "$selection_valid_request" "$selection_malformed_request" "$selection_invalid_request" "$selection_valid_response" "$selection_malformed_response" "$selection_invalid_response" "$captaincy_valid_request" "$captaincy_malformed_request" "$captaincy_invalid_request" "$captaincy_duplicate_request" "$captaincy_valid_response" "$captaincy_malformed_response" "$captaincy_invalid_response" "$captaincy_duplicate_response" "$substitution_valid_request" "$substitution_malformed_request" "$substitution_invalid_request" "$substitution_valid_response" "$substitution_malformed_response" "$substitution_invalid_response" "$outcome_valid_response" "$outcome_malformed_response" "$outcome_invalid_response" "$score_valid_request" "$score_malformed_request" "$score_invalid_request" "$score_valid_response" "$score_malformed_response" "$score_invalid_response"; cleanup' EXIT
invalid_status="$(curl --silent --show-error --output "$invalid_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"schemaVersion":"2.0","sourceType":"manual"}' \
  "$base_url/api/v1/decision-snapshot-metadata/validation")"
official_capture_status="$(curl --silent --show-error --output "$official_capture_file" --write-out '%{http_code}' \
  "$base_url/api/v1/data/official-fpl/latest")"
[[ "$official_capture_status" == "404" ]] || {
  printf 'expected no implicit official FPL capture, got HTTP %s\n' "$official_capture_status" >&2
  cat "$official_capture_file" >&2
  exit 1
}
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

python3 - "$selection_valid_request" "$selection_malformed_request" "$selection_invalid_request" "$captaincy_valid_request" "$captaincy_malformed_request" "$captaincy_invalid_request" "$captaincy_duplicate_request" "$substitution_valid_request" "$substitution_malformed_request" "$substitution_invalid_request" "$score_valid_request" "$score_malformed_request" "$score_invalid_request" <<'PY'
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
selection_malformed = dict(valid, replacementGoalkeeperPlayerId="2")
selection_invalid = dict(valid, replacementGoalkeeperPlayerId=6, outfieldSubstitutePlayerIds=[2, 7, 15])
captaincy_valid = dict(valid, playerIdsWithMinutes=[13])
captaincy_malformed = dict(valid, playerIdsWithMinutes=["13"])
captaincy_invalid = dict(valid, playerIdsWithMinutes=[13, 99])
captaincy_duplicate = dict(valid, playerIdsWithMinutes=[8, 8])
substitution_valid = dict(
    valid,
    outfieldSubstitutePlayerIds=[15, 6, 7],
    playerIdsWhoPlayed=[1, 4, 5, 6, 9, 10, 11, 12, 13, 14, 15],
)
substitution_malformed = dict(valid, playerIdsWhoPlayed=["1"])
substitution_invalid = dict(valid, playerIdsWhoPlayed=[1, 99])
score_points = [
    {"playerId": player_id, "points": points}
    for player_id, points in enumerate([2, 0, 0, 6, 1, 8, 0, 0, 3, -1, 5, 2, 7, 4, 10], start=1)
]
score_valid = dict(substitution_valid, playerPoints=score_points)
score_malformed = dict(
    substitution_valid,
    playerPoints=[dict(entry) for entry in score_points],
)
score_malformed["playerPoints"][0]["points"] = "2"
score_invalid_points = [dict(entry) for entry in score_points]
score_invalid_points[-1]["playerId"] = 1
score_invalid = dict(substitution_valid, playerPoints=score_invalid_points)
payloads = [
    valid,
    selection_malformed,
    selection_invalid,
    captaincy_valid,
    captaincy_malformed,
    captaincy_invalid,
    captaincy_duplicate,
    substitution_valid,
    substitution_malformed,
    substitution_invalid,
    score_valid,
    score_malformed,
    score_invalid,
]
for path, payload in zip(sys.argv[1:], payloads, strict=True):
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
captaincy_valid_status="$(curl --silent --show-error --output "$captaincy_valid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$captaincy_valid_request" \
  "$base_url/api/v1/gameweek-outcomes/captaincy-resolution")"
captaincy_malformed_status="$(curl --silent --show-error --output "$captaincy_malformed_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$captaincy_malformed_request" \
  "$base_url/api/v1/gameweek-outcomes/captaincy-resolution")"
captaincy_invalid_status="$(curl --silent --show-error --output "$captaincy_invalid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$captaincy_invalid_request" \
  "$base_url/api/v1/gameweek-outcomes/captaincy-resolution")"
captaincy_duplicate_status="$(curl --silent --show-error --output "$captaincy_duplicate_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$captaincy_duplicate_request" \
  "$base_url/api/v1/gameweek-outcomes/captaincy-resolution")"
substitution_valid_status="$(curl --silent --show-error --output "$substitution_valid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$substitution_valid_request" \
  "$base_url/api/v1/gameweek-outcomes/substitution-resolution")"
substitution_malformed_status="$(curl --silent --show-error --output "$substitution_malformed_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$substitution_malformed_request" \
  "$base_url/api/v1/gameweek-outcomes/substitution-resolution")"
substitution_invalid_status="$(curl --silent --show-error --output "$substitution_invalid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$substitution_invalid_request" \
  "$base_url/api/v1/gameweek-outcomes/substitution-resolution")"
outcome_valid_status="$(curl --silent --show-error --output "$outcome_valid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$substitution_valid_request" \
  "$base_url/api/v1/gameweek-outcomes/effective-resolution")"
outcome_malformed_status="$(curl --silent --show-error --output "$outcome_malformed_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$substitution_malformed_request" \
  "$base_url/api/v1/gameweek-outcomes/effective-resolution")"
outcome_invalid_status="$(curl --silent --show-error --output "$outcome_invalid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$substitution_invalid_request" \
  "$base_url/api/v1/gameweek-outcomes/effective-resolution")"
score_valid_status="$(curl --silent --show-error --output "$score_valid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$score_valid_request" \
  "$base_url/api/v1/gameweek-outcomes/effective-score")"
score_malformed_status="$(curl --silent --show-error --output "$score_malformed_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$score_malformed_request" \
  "$base_url/api/v1/gameweek-outcomes/effective-score")"
score_invalid_status="$(curl --silent --show-error --output "$score_invalid_response" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary "@$score_invalid_request" \
  "$base_url/api/v1/gameweek-outcomes/effective-score")"

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

python3 - "$captaincy_valid_response" "$captaincy_valid_status" "$captaincy_malformed_status" "$captaincy_invalid_response" "$captaincy_invalid_status" "$captaincy_duplicate_response" "$captaincy_duplicate_status" <<'PY'
import json, pathlib, sys

valid = json.loads(pathlib.Path(sys.argv[1]).read_text())
valid_status, malformed_status = sys.argv[2:4]
invalid = json.loads(pathlib.Path(sys.argv[4]).read_text())
invalid_status = sys.argv[5]
duplicate = json.loads(pathlib.Path(sys.argv[6]).read_text())
duplicate_status = sys.argv[7]
assert valid_status == "200", valid_status
assert valid["originalCaptainPlayerId"] == 8, valid
assert valid["viceCaptainPlayerId"] == 13, valid
assert valid["effectiveCaptainPlayerId"] == 13, valid
assert valid["captaincyTransferred"] is True, valid
assert malformed_status == "400", malformed_status
assert invalid_status == "422", invalid_status
assert invalid["code"] == "outcome.minutes_player.not_in_squad", invalid
assert invalid["field"] == "playerIdsWithMinutes", invalid
assert duplicate_status == "422", duplicate_status
assert duplicate["code"] == "outcome.minutes_player.duplicate", duplicate
assert duplicate["field"] == "playerIdsWithMinutes", duplicate
PY

python3 - "$substitution_valid_response" "$substitution_valid_status" "$substitution_malformed_status" "$substitution_invalid_response" "$substitution_invalid_status" <<'PY'
import json, pathlib, sys

valid = json.loads(pathlib.Path(sys.argv[1]).read_text())
valid_status, malformed_status = sys.argv[2:4]
invalid = json.loads(pathlib.Path(sys.argv[4]).read_text())
invalid_status = sys.argv[5]
assert valid_status == "200", valid_status
assert valid["originalStartingPlayerIds"] == [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14], valid
assert valid["effectivePlayerIds"] == [1, 6, 4, 5, 15, 9, 10, 11, 12, 13, 14], valid
assert valid["activatedSubstitutePlayerIds"] == [15, 6], valid
assert valid["unreplacedStartingPlayerIds"] == [], valid
assert malformed_status == "400", malformed_status
assert invalid_status == "422", invalid_status
assert invalid["code"] == "outcome.played_player.not_in_squad", invalid
assert invalid["field"] == "playerIdsWhoPlayed", invalid
PY

python3 - "$outcome_valid_response" "$outcome_valid_status" "$outcome_malformed_status" "$outcome_invalid_response" "$outcome_invalid_status" <<'PY'
import json, pathlib, sys

valid = json.loads(pathlib.Path(sys.argv[1]).read_text())
valid_status, malformed_status = sys.argv[2:4]
invalid = json.loads(pathlib.Path(sys.argv[4]).read_text())
invalid_status = sys.argv[5]
assert valid_status == "200", valid_status
assert valid["effectivePlayerIds"] == [1, 6, 4, 5, 15, 9, 10, 11, 12, 13, 14], valid
assert valid["activatedSubstitutePlayerIds"] == [15, 6], valid
assert valid["effectiveCaptainPlayerId"] == 13, valid
assert valid["captaincyTransferred"] is True, valid
assert malformed_status == "400", malformed_status
assert invalid_status == "422", invalid_status
assert invalid["code"] == "outcome.played_player.not_in_squad", invalid
assert invalid["field"] == "playerIdsWhoPlayed", invalid
PY

python3 - "$score_valid_response" "$score_valid_status" "$score_malformed_status" "$score_invalid_response" "$score_invalid_status" <<'PY'
import json, pathlib, sys

valid = json.loads(pathlib.Path(sys.argv[1]).read_text())
valid_status, malformed_status = sys.argv[2:4]
invalid = json.loads(pathlib.Path(sys.argv[4]).read_text())
invalid_status = sys.argv[5]
assert valid_status == "200", valid_status
assert valid["outcome"]["effectivePlayerIds"] == [1, 6, 4, 5, 15, 9, 10, 11, 12, 13, 14], valid
assert valid["outcome"]["effectiveCaptainPlayerId"] == 13, valid
assert valid["basePoints"] == 47, valid
assert valid["captainBonusPoints"] == 7, valid
assert valid["totalPoints"] == 54, valid
assert malformed_status == "400", malformed_status
assert invalid_status == "422", invalid_status
assert invalid["code"] == "outcome.player_points.duplicate", invalid
assert invalid["field"] == "playerPoints", invalid
PY

printf 'container smoke test passed for %s\n' "$image"
