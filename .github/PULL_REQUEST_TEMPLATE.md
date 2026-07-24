## Purpose

Describe the user/research outcome and link the issue, ADR and registered experiment where applicable.

## Scope

- **Included:**
- **Explicitly excluded:**
- **Compliance boundary checked:** yes/no, with explanation

## Evidence and implementation

- [ ] Acceptance criteria are explicit and satisfied.
- [ ] Behaviour changes followed RED-GREEN-REFACTOR; the initial expected failure is described below.
- [ ] Research claims use point-in-time data and a registered walk-forward evaluation.
- [ ] Data/model/architecture/security records were updated where applicable.
- [ ] No FPL scraping, credential/session access or automatic account action was introduced.

### RED evidence

Command and expected failure:

```text

```

### GREEN/full verification evidence

Commands and real results:

```text

```

## Risk and operations

- Security/privacy/data-rights risks:
- Failure and rollback/roll-forward path:
- Observability added or changed:
- Dependencies/licences introduced:

## Definition of Done

- [ ] Focused and full tests pass without new warnings or flakes.
- [ ] Format, lint, type, governance and security checks pass.
- [ ] Migration/contract compatibility is tested where applicable.
- [ ] Calibration, baselines, uncertainty and sensitivity are reported where applicable.
- [ ] Documentation and runbooks match behaviour.
- [ ] No secret, private data or generated artefact is committed.
- [ ] All required checks pass on this exact commit.

## Reviewer focus

Call out the hardest assumption or highest-risk part of the change.
