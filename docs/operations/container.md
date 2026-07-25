# autoFPL API container

## Purpose and current boundary

The container is a private development API for exercising deterministic metadata, squad, lineup, gameweek-selection, effective-captain and automatic-substitution rules. It has no database, FPL data collection, credentials, autonomous actions or user-facing write operations.

Routes:

| Method | Path | Behavior |
|---|---|---|
| `GET` | `/healthz` | Liveness response: `{"status":"healthy"}` |
| `GET` | `/readyz` | Readiness response: `{"status":"ready"}` |
| `POST` | `/api/v1/decision-snapshot-metadata/validation` | Returns canonical metadata or a stable 400/422 problem response |
| `POST` | `/api/v1/squads/validation` | Validates a manually supplied 15-player squad and returns its exact integer-tenths budget summary |
| `POST` | `/api/v1/lineups/validation` | Validates a manually supplied starting XI, formation, captain and vice-captain against a valid squad |
| `POST` | `/api/v1/gameweek-selections/validation` | Validates the starting XI plus replacement goalkeeper and three ordered outfield substitutes against a valid squad |
| `POST` | `/api/v1/gameweek-outcomes/captaincy-resolution` | Resolves the effective captain from a valid complete selection and manually supplied player IDs with minutes |
| `POST` | `/api/v1/gameweek-outcomes/substitution-resolution` | Resolves automatic substitutions from a valid complete selection and manually supplied player IDs that played |

Decision-snapshot metadata request fields are exact and case-sensitive. Missing or `null` required fields, duplicate or undeclared fields, non-string field values and malformed payloads fail with 400. Present string values that are unsupported fail with the stable domain error code and 422. The request body is bounded to 16 KiB by Kestrel.

Squad requests reject undeclared fields and malformed JSON. They accept no external data or account credentials. Positions are `goalkeeper`, `defender`, `midfielder` and `forward`; money is represented as integer tenths rather than floating point.

Lineup requests use the same manual squad boundary, require 11 unique squad members, exactly one goalkeeper, at least three defenders and at least one forward, and require distinct captain and vice-captain IDs from the starting XI. Structural failures return 400; rule-invalid lineups return a stable 422 problem response.

Gameweek-selection requests preserve the starting-XI and captaincy rules, then require the non-starting squad goalkeeper as the replacement goalkeeper and exactly three distinct non-goalkeeper substitutes in priority order. The starting XI and bench must partition the 15-player squad exactly once. Structural failures return 400; rule-invalid selections return a stable 422 problem response. This endpoint validates the submitted bench order only; it does not ingest player appearances, execute automatic substitutions or calculate points.

Captaincy-resolution requests compose the same valid squad and complete gameweek selection with a distinct list of squad-player IDs that played at least one minute. Captaincy remains with the selected captain when present, transfers to the vice-captain when only the vice-captain played minutes, and resolves to `null` when neither played. Structural failures return 400; duplicate or non-squad minute evidence returns a stable 422 problem response. Minute evidence is manually supplied: the endpoint does not retrieve appearances, points or account data.

Substitution-resolution requests compose the same valid squad and complete selection with distinct, manually supplied squad-player IDs representing players who played in the Gameweek. A non-playing starting goalkeeper is replaced only by the replacement goalkeeper when that goalkeeper played. Played outfield substitutes are considered in bench-priority order and activated only when the existing formation rules remain valid; unresolved non-playing starters are reported explicitly. Structural failures return 400, and duplicate or non-squad play evidence returns a stable 422 problem response. The endpoint does not retrieve appearances or cards, calculate scores, persist outcomes or perform account actions.

## Image construction

`Dockerfile` uses digest-pinned Microsoft .NET images:

- SDK: `10.0.302-noble`
- runtime: `10.0.10-noble-chiseled-extra`

The final image:

- runs as the base image's non-root UID `1654`;
- contains no shell or package manager;
- has an application-native Docker health check;
- carries OCI source, revision and AGPL licence labels;
- contains no test packages or JSON Schema validator;
- supports a read-only root filesystem with a small `/tmp` tmpfs;
- is scanned for HIGH and CRITICAL OS and .NET vulnerabilities before publication.

Build and smoke-test locally from the repository root:

```bash
docker build \
  --build-arg "SOURCE_REVISION=$(git rev-parse HEAD)" \
  --tag autofpl:local .

bash scripts/ci_container_smoke.sh autofpl:local "$(git rev-parse HEAD)"
```

The smoke test launches the image with a read-only filesystem, all Linux capabilities dropped, `no-new-privileges`, and no fixed host port.

## CI and publication

`.github/workflows/container.yml` runs on pull requests to `dev`/`main` and pushes to `dev`.

1. Build a local candidate from locked dependencies and digest-pinned bases.
2. Save the image to an archive.
3. Scan the archive with digest-pinned Trivy without exposing the Docker socket to the scanner.
4. Run the hardened container smoke test.
5. On a protected `dev` push only, grant `packages: write`, repeat the gates, authenticate to GHCR, and publish:
   - `ghcr.io/jellman86/autofpl:dev`
   - `ghcr.io/jellman86/autofpl:sha-<full-commit-sha>`
6. Tag and push the exact scanned candidate, immutable SHA tag first and mutable `dev` tag second, then verify the SHA tag is readable.

Pull requests never receive registry write permission and never publish images.

## Private Dockhand deployment

The Git-backed Dockhand definition lives in `autofpl/` in the separate `docker-configs` repository and deploys to Quark/Fedora.

- It pins `ghcr.io/jellman86/autofpl@sha256:<published-manifest-digest>`; `:dev` is never deployed directly.
- It joins only the external trusted `general_brg` network, where internal consumers can use `http://autofpl-api:8080`.
- Do not publish a host port in the steady-state stack.
- Use `read_only: true`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`, and a bounded `/tmp` tmpfs.
- No Nginx Proxy Manager host or public DNS route is configured. Any public/user-authenticated API is a later threat-model and authentication decision.
- Store future secrets only in Dockhand; this first image requires none.

All stack lifecycle changes must follow the `docker-configs` repository's Git-backed Dockhand procedure. Do not run direct `docker compose pull` or `up` commands on the host.

## Rollback

No prior releasable digest exists yet, so an image rollback has not been exercised. When a prior verified digest exists:

1. Select the previously verified image digest from Git/GHCR history.
2. Revert the `docker-configs` digest change and push it through review.
3. Redeploy the Git-backed stack through Dockhand.
4. Verify container health and representative 200/400/422 behavior.

This image has no persistent state, so rollback requires no data migration or volume restoration.
