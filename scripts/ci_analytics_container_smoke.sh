#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: ci_analytics_container_smoke.sh IMAGE [EXPECTED_REVISION]}"
expected_revision="${2:-}"

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

docker run --rm \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,nodev,size=32m,uid=1654,gid=1654 \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  "$image" \
  --help >/dev/null

versions="$(docker run --rm \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,nodev,size=32m,uid=1654,gid=1654 \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --entrypoint python \
  "$image" \
  -c 'import numpy, sklearn; print(f"{numpy.__version__} {sklearn.__version__}")')"
[[ "$versions" == "2.5.1 1.9.0" ]] || {
  printf 'unexpected analytics dependency versions: %s\n' "$versions" >&2
  exit 1
}
