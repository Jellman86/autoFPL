const positionOrder = ["forward", "midfielder", "defender", "goalkeeper"];
const mobileDossierQuery = window.matchMedia("(max-width: 980px)");

let selectedPlayerId = null;
let advice = null;
let selectionRevision = null;
let displayedPlayers = [];
let selectionEditDraft = null;
let lastSelectedCard = null;
let dossierRequest = 0;

function formatDeadline(value) {
  return new Intl.DateTimeFormat(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
    timeZoneName: "short",
  }).format(new Date(value));
}

function formatCompactInstant(value) {
  return new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function formatKickoff(value) {
  if (!value) return "TBC";
  return new Intl.DateTimeFormat(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function formatLeadTime(totalSeconds) {
  const hours = Math.floor(totalSeconds / 3600);
  const days = Math.floor(hours / 24);
  const remainingHours = hours % 24;
  if (days > 0) return `${days}d ${remainingHours}h before deadline`;
  if (hours > 0) return `${hours}h before deadline`;
  return `${Math.max(0, Math.floor(totalSeconds / 60))}m before deadline`;
}

function initials(name) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
}

function createPortrait(name, photoUrl, className) {
  const portrait = document.createElement("span");
  portrait.className = className;
  const fallback = document.createElement("span");
  fallback.className = "portrait-fallback";
  fallback.textContent = initials(name);
  portrait.append(fallback);

  if (photoUrl) {
    const image = document.createElement("img");
    image.src = photoUrl;
    image.alt = "";
    image.loading = "lazy";
    image.decoding = "async";
    image.addEventListener("load", () => portrait.classList.add("has-photo"));
    image.addEventListener("error", () => image.remove());
    portrait.append(image);
  }

  return portrait;
}

function createPlayerCard(player) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "player-card";
  button.dataset.playerId = String(player.playerId);
  button.setAttribute("aria-pressed", String(player.playerId === selectedPlayerId));
  button.setAttribute(
    "aria-label",
    `${player.name}, ${player.expectedPoints} ${advice?.isSynthetic ? "synthetic " : ""}expected points, ${player.expectedMinutes} expected minutes. Open player dossier.`,
  );

  const portrait = createPortrait(player.name, player.photoUrl, "card-portrait");
  const badgeRail = document.createElement("span");
  badgeRail.className = "badge-rail";
  if (player.captaincy) {
    const captain = document.createElement("span");
    captain.className = "captain";
    captain.textContent = player.captaincy === "captain" ? "C" : "VC";
    badgeRail.append(captain);
  }
  if (player.benchOrder) {
    const order = document.createElement("span");
    order.className = "bench-order";
    order.textContent = String(player.benchOrder);
    badgeRail.append(order);
  }

  const body = document.createElement("span");
  body.className = "card-body";
  const identity = document.createElement("span");
  identity.className = "card-identity";
  const club = document.createElement("span");
  club.className = "club";
  club.textContent = `${player.clubShortName} · ${player.position.slice(0, 3).toUpperCase()}`;
  const name = document.createElement("span");
  name.className = "name";
  name.textContent = player.name;
  identity.append(club, name);

  const projection = document.createElement("span");
  projection.className = "projection";
  const points = document.createElement("strong");
  points.textContent = player.expectedPoints.toFixed(1);
  const pointsLabel = document.createElement("span");
  pointsLabel.textContent = "xPts";
  projection.append(points, pointsLabel);

  const range = document.createElement("span");
  range.className = "range-track";
  const rangeFill = document.createElement("span");
  rangeFill.style.width = `${Math.min(100, Math.max(12, (player.upper80 / 15) * 100))}%`;
  range.append(rangeFill);

  const fixture = document.createElement("span");
  fixture.className = "fixture";
  fixture.textContent =
    `${player.isHome ? "vs" : "at"} ${player.opponent} · ${player.expectedMinutes}′`;

  body.append(identity, projection, range, fixture);
  button.append(portrait, badgeRail, body);
  button.addEventListener("click", () => {
    lastSelectedCard = button;
    selectPlayer(player.playerId, { updateHistory: true, focusDossier: true });
  });
  return button;
}

function setDossierPortrait(name, photoUrl) {
  const portrait = document.querySelector("#dossier-portrait");
  portrait.replaceChildren();
  const rendered = createPortrait(name, photoUrl, "dossier-portrait-content");
  portrait.append(...rendered.childNodes);
  portrait.classList.toggle("has-photo", Boolean(photoUrl));
  const image = portrait.querySelector("img");
  if (image) {
    image.addEventListener("error", () => portrait.classList.remove("has-photo"));
  }
}

function openDossier(focusDossier) {
  const dossier = document.querySelector("#player-dossier");
  if (mobileDossierQuery.matches && !focusDossier) {
    dossier.classList.remove("is-open");
    dossier.setAttribute("aria-hidden", "true");
    document.body.classList.remove("dossier-open");
    return;
  }

  dossier.classList.add("is-open");
  dossier.setAttribute("aria-hidden", "false");
  document.body.classList.add("dossier-open");
  if (focusDossier && mobileDossierQuery.matches) {
    dossier.focus({ preventScroll: true });
  }
}

function closeDossier() {
  if (!mobileDossierQuery.matches) return;
  const dossier = document.querySelector("#player-dossier");
  dossier.classList.remove("is-open");
  dossier.setAttribute("aria-hidden", "true");
  document.body.classList.remove("dossier-open");
  lastSelectedCard?.focus({ preventScroll: true });
}

function updatePlayerUrl(playerId) {
  const url = new URL(window.location.href);
  url.searchParams.set("player", String(playerId));
  window.history.replaceState({ playerId }, "", url);
}

function selectPlayer(playerId, options = {}) {
  selectedPlayerId = playerId;
  const player = displayedPlayers.find(
    (candidate) => candidate.playerId === playerId,
  );
  if (!player) return;

  document.querySelectorAll(".player-card").forEach((card) => {
    card.setAttribute("aria-pressed", String(Number(card.dataset.playerId) === playerId));
  });

  document.querySelector("#player-name").textContent = player.name;
  document.querySelector("#player-context").textContent =
    `${player.clubShortName} · ${player.position} · ${player.isHome ? "home to" : "away at"} ${player.opponent}`;
  document.querySelector("#player-points").textContent = player.expectedPoints.toFixed(1);
  document.querySelector("#player-minutes").textContent = `${player.expectedMinutes}′`;
  document.querySelector("#player-range").textContent =
    `${player.lower80.toFixed(0)}–${player.upper80.toFixed(0)}`;
  document.querySelector("#published-xpts").hidden = true;
  document.querySelector("#preseason-challenger").hidden = true;
  setDossierPortrait(player.name, player.photoUrl);

  const badge = document.querySelector("#player-badge");
  badge.textContent = player.captaincy
    ? player.captaincy
    : player.lineupPlace === "bench"
      ? `bench ${player.benchOrder}`
      : "starter";
  badge.hidden = false;

  renderList("#player-reasons", player.reasons);
  renderList("#player-risks", player.risks);
  openDossier(Boolean(options.focusDossier));
  if (options.updateHistory !== false) updatePlayerUrl(playerId);
  loadPlayerDossier(player);
}

function renderList(selector, values) {
  const list = document.querySelector(selector);
  list.replaceChildren();
  values.forEach((value) => {
    const item = document.createElement("li");
    item.textContent = value;
    list.append(item);
  });
}

function setDossierState(state, message) {
  const element = document.querySelector("#dossier-state");
  element.dataset.state = state;
  element.textContent = message;
}

function renderEmpty(container, message) {
  const empty = document.createElement("p");
  empty.className = "empty-state";
  empty.textContent = message;
  container.replaceChildren(empty);
}

function stat(label, value) {
  const item = document.createElement("span");
  const term = document.createElement("small");
  term.textContent = label;
  const amount = document.createElement("strong");
  amount.textContent = String(value);
  item.append(term, amount);
  return item;
}

function fixedStat(label, value, digits = 2) {
  return stat(label, Number(value).toFixed(digits));
}

function renderRecentForm(outcomes) {
  const container = document.querySelector("#recent-form");
  if (!outcomes.length) {
    renderEmpty(
      container,
      "No completed outcome is available before this deadline yet. Missing history stays missing.",
    );
    return;
  }

  const rows = outcomes.map((outcome) => {
    const row = document.createElement("article");
    row.className = "form-row";
    const headline = document.createElement("div");
    headline.className = "form-headline";
    const gameweek = document.createElement("span");
    gameweek.className = "gameweek-chip";
    gameweek.textContent = `GW${outcome.gameweek}`;
    const opponent = document.createElement("strong");
    opponent.textContent = outcome.fixtures.length
      ? outcome.fixtures
          .map((fixture) => `${fixture.isHome ? "vs" : "at"} ${fixture.opponentShortName}`)
          .join(" · ")
      : "Fixture unavailable";
    const points = document.createElement("span");
    points.className = "outcome-points";
    points.textContent = `${outcome.totalPoints} pts`;
    headline.append(gameweek, opponent, points);
    if (outcome.isGameweekAggregate) {
      const aggregate = document.createElement("span");
      aggregate.className = "aggregate-chip";
      aggregate.textContent = "GW aggregate";
      headline.append(aggregate);
    }

    const stats = document.createElement("div");
    stats.className = "form-stats";
    stats.append(
      stat("MIN", outcome.minutes),
      stat("START", outcome.starts),
      stat("G", outcome.goalsScored),
      stat("A", outcome.assists),
      stat("CS", outcome.cleanSheets),
      stat("SAV", outcome.saves),
      stat("BON", outcome.bonus),
      stat("YC", outcome.yellowCards),
      stat("RC", outcome.redCards),
    );
    row.append(headline, stats);
    if (outcome.expectedGoals !== null) {
      const underlying = document.createElement("div");
      underlying.className = "underlying-stats";
      underlying.setAttribute(
        "aria-label",
        "Official underlying performance statistics",
      );
      underlying.append(
        fixedStat("xG", outcome.expectedGoals),
        fixedStat("xA", outcome.expectedAssists),
        fixedStat("xGC", outcome.expectedGoalsConceded),
        fixedStat("ICT", outcome.ictIndex, 1),
        stat("BPS", outcome.bps),
        stat("DEF", outcome.defensiveContribution),
      );
      row.append(underlying);
    }
    return row;
  });
  container.replaceChildren(...rows);
}

function renderUpcomingFixtures(fixtures) {
  const container = document.querySelector("#upcoming-fixtures");
  if (!fixtures.length) {
    renderEmpty(container, "No upcoming fixture is present in the selected capture.");
    return;
  }

  const cards = fixtures.map((fixture) => {
    const card = document.createElement("article");
    card.className = "fixture-card";
    const gameweek = document.createElement("span");
    gameweek.textContent = `GW ${fixture.gameweek}`;
    const opponent = document.createElement("strong");
    opponent.textContent = fixture.opponentShortName;
    const venue = document.createElement("small");
    venue.textContent = fixture.isHome ? "HOME" : "AWAY";
    const kickoff = document.createElement("time");
    kickoff.dateTime = fixture.kickoffUtc ?? "";
    kickoff.textContent = formatKickoff(fixture.kickoffUtc);
    card.append(gameweek, opponent, venue, kickoff);
    return card;
  });
  container.replaceChildren(...cards);
}

function sourceDisplayName(sourceKey) {
  const knownSources = {
    ffs: "Fantasy Football Scout",
    ffscout: "Fantasy Football Scout",
    "fantasy-football-scout": "Fantasy Football Scout",
    "ffscout-predicted-lineups": "Fantasy Football Scout",
    "ffscout-editorial-lineup": "Fantasy Football Scout",
    strAIghtred: "strAIghtred consensus",
    "straightred-lineup-consensus": "strAIghtred consensus",
  };
  return knownSources[sourceKey] ?? sourceKey;
}

function researchClaimHeadline(claim) {
  if (claim.claimType === "availability") {
    const labels = {
      available: "Reported available",
      doubtful: "Reported doubtful",
      unavailable: "Reported unavailable",
      "expected-return": "Return expected",
    };
    return labels[claim.availabilityStatus] ?? "Availability report";
  }
  if (claim.claimType === "start") {
    const labels = {
      starts: "Predicted to start",
      "does-not-start": "Predicted not to start",
      uncertain: "Start uncertain",
    };
    return labels[claim.startStatus] ?? "Starting-status prediction";
  }
  if (claim.claimType === "minutes") {
    return `${claim.expectedMinutes} expected minutes`;
  }
  if (claim.claimType === "role") {
    return `Role: ${claim.role}`;
  }
  return "Research claim";
}

function researchClaimMetric(claim) {
  if (claim.forecastProbability === null) return null;
  const percentage = `${Math.round(Number(claim.forecastProbability) * 100)}%`;
  return ["strAIghtred", "straightred-lineup-consensus"].includes(claim.sourceKey)
    ? `Source agreement ${percentage}`
    : `Reported probability ${percentage}`;
}

function safeExternalUrl(value) {
  try {
    const url = new URL(value);
    return ["http:", "https:"].includes(url.protocol) ? url.href : null;
  } catch {
    return null;
  }
}

function setResearchSummary(tally, signal, state = "neutral") {
  document.querySelector("#research-tally").textContent = tally;
  const signalElement = document.querySelector("#research-signal");
  signalElement.textContent = signal;
  signalElement.dataset.state = state;
}

function renderResearchEvidence(evidence) {
  const container = document.querySelector("#research-evidence");
  if (!evidence || !evidence.claims.length) {
    setResearchSummary(
      "0 admitted claims",
      "No named source claim was available for this player before the deadline. "
        + "This is missing coverage, not a prediction that the player will be benched.",
    );
    renderEmpty(
      container,
      "No admitted research claim was available. Predicted-lineup sources can be "
        + "incomplete; absence is kept as unknown unless a complete XI supports a "
        + "does-not-start inference.",
    );
    return;
  }

  const claimLabel = evidence.claimCount === 1 ? "claim" : "claims";
  const sourceLabel = evidence.sourceCount === 1 ? "source" : "sources";
  let signal = evidence.sourceCount > 1
    ? "Multi-source view. Treat agreement as corroboration, not independence."
    : "Single-source view. Corroboration is not available yet.";
  let signalState = "neutral";
  if (evidence.hasContradictions) {
    signal = "Conflicting categorical claims are present. Uncertainty remains unresolved.";
    signalState = "warning";
  } else if (evidence.dependentClusterCount > 0) {
    signal =
      `${evidence.dependentClusterCount} linked evidence ` +
      `${evidence.dependentClusterCount === 1 ? "cluster" : "clusters"} detected; ` +
      "linked claims are not independent votes.";
    signalState = "linked";
  }
  setResearchSummary(
    `${evidence.claimCount} ${claimLabel} · ${evidence.sourceCount} ${sourceLabel}`,
    signal,
    signalState,
  );

  const cards = evidence.claims.map((claim) => {
    const card = document.createElement("article");
    card.className = "research-claim";
    if (claim.isDependent) card.dataset.dependent = "true";

    const sourceLine = document.createElement("div");
    sourceLine.className = "research-source-line";
    const source = document.createElement("strong");
    source.textContent = sourceDisplayName(claim.sourceKey);
    const type = document.createElement("span");
    type.textContent = claim.claimType;
    sourceLine.append(source, type);
    if (claim.isDependent) {
      const dependent = document.createElement("span");
      dependent.className = "dependent-chip";
      dependent.textContent = "Linked evidence";
      sourceLine.append(dependent);
    }

    const headline = document.createElement("h4");
    headline.textContent = researchClaimHeadline(claim);
    const metricValue = researchClaimMetric(claim);
    if (metricValue) {
      const metric = document.createElement("span");
      metric.className = "research-metric";
      metric.textContent = metricValue;
      headline.append(metric);
    }

    const sourceSpan = document.createElement("blockquote");
    sourceSpan.textContent = claim.sourceSpan;

    const metadata = document.createElement("div");
    metadata.className = "research-meta";
    const available = document.createElement("time");
    available.dateTime = claim.availableAtUtc;
    available.textContent =
      `${formatCompactInstant(claim.availableAtUtc)} · ${formatLeadTime(claim.leadTimeSeconds)}`;
    const directness = document.createElement("span");
    directness.textContent = claim.directness.replaceAll("-", " ");
    metadata.append(available, directness);
    if (claim.author) {
      const author = document.createElement("span");
      author.textContent = `by ${claim.author}`;
      metadata.append(author);
    }

    const sourceUrl = safeExternalUrl(claim.canonicalUrl);
    if (sourceUrl) {
      const link = document.createElement("a");
      link.href = sourceUrl;
      link.target = "_blank";
      link.rel = "noopener noreferrer";
      link.textContent = "Open source";
      metadata.append(link);
    }

    card.append(sourceLine, headline, sourceSpan, metadata);
    return card;
  });
  container.replaceChildren(...cards);
}

async function loadPlayerDossier(player) {
  const request = ++dossierRequest;
  renderEmpty(document.querySelector("#recent-form"), "Loading previous Gameweeks…");
  renderEmpty(document.querySelector("#upcoming-fixtures"), "Loading upcoming fixtures…");
  setResearchSummary("Loading claims…", "Checking the same pre-deadline evidence boundary.");
  renderEmpty(document.querySelector("#research-evidence"), "Loading admitted research…");

  if (!player.dossierPath) {
    setDossierState(
      "preview",
      "Official dossier unavailable for this synthetic identity; forecast evidence remains visible.",
    );
    renderEmpty(
      document.querySelector("#recent-form"),
      "This fallback preview has no official match history.",
    );
    renderEmpty(
      document.querySelector("#upcoming-fixtures"),
      "This fallback preview has no official fixture join.",
    );
    setResearchSummary(
      "Unavailable for preview",
      "Synthetic identities cannot be joined safely to admitted source claims.",
    );
    renderEmpty(
      document.querySelector("#research-evidence"),
      "Research evidence requires a stable official player identity.",
    );
    return;
  }

  setDossierState(
    "loading",
    "Loading cutoff-correct official identity, form, fixtures and research…",
  );
  try {
    const response = await fetch(player.dossierPath, {
      headers: { Accept: "application/json" },
    });
    if (request !== dossierRequest) return;
    if (response.status === 404) {
      setDossierState(
        "empty",
        "No qualifying dossier exists for this player at the selected deadline.",
      );
      renderEmpty(document.querySelector("#recent-form"), "No prior outcome is available.");
      renderEmpty(document.querySelector("#upcoming-fixtures"), "No fixture data is available.");
      setResearchSummary("No dossier", "Research could not be joined to this player.");
      renderEmpty(
        document.querySelector("#research-evidence"),
        "No cutoff-correct research dossier is available.",
      );
      return;
    }
    if (!response.ok) throw new Error(`Dossier request failed with ${response.status}`);

    const dossier = await response.json();
    if (request !== dossierRequest) return;
    document.querySelector("#player-name").textContent = dossier.player.fullName;
    document.querySelector("#player-context").textContent =
      `${dossier.player.teamShortName} · ${dossier.player.position} · £${(dossier.player.priceTenths / 10).toFixed(1)}m`;
    setDossierPortrait(dossier.player.fullName, dossier.player.photoUrl);
    setDossierState(
      "ready",
      `Official capture #${dossier.selectedCaptureId} · evidence available ${formatCompactInstant(dossier.captureAvailableAtUtc)} · cutoff ${formatCompactInstant(dossier.decisionCutoffUtc)}`,
    );
    const published = document.querySelector("#published-xpts");
    if (dossier.publishedExpectedPoints) {
      document.querySelector("#published-xpts-value").textContent =
        `${Number(dossier.publishedExpectedPoints.expectedPoints).toFixed(1)} pts`;
      published.hidden = false;
    } else {
      published.hidden = true;
    }
    const challenger = document.querySelector("#preseason-challenger");
    if (dossier.preseasonChallenger) {
      const forecast = dossier.preseasonChallenger;
      const difference = Number(forecast.differenceFromBaselineV0);
      const direction = difference > 0 ? "+" : "";
      document.querySelector("#preseason-challenger-value").textContent =
        `${Number(forecast.expectedPoints).toFixed(1)} pts`;
      document.querySelector("#preseason-challenger-delta").textContent =
        `${direction}${difference.toFixed(1)} vs Baseline v0`;
      const warnings = [];
      if (forecast.availabilityStatus === "authoritative-current-official-not-modelled") {
        warnings.push("official availability shown, not modelled");
      } else {
        warnings.push(`availability: ${forecast.availabilityStatus}`);
      }
      if (forecast.priorSeasonIdentityStatus !== "stable-code-match") {
        warnings.push("no matched prior-season identity");
      }
      document.querySelector("#preseason-challenger-note").textContent =
        `Comparison only · ${(Number(forecast.lockedHoldoutMaeImprovementFraction) * 100).toFixed(1)}% lower locked-holdout MAE · no calibrated distribution`
        + (warnings.length ? ` · ${warnings.join(" · ")}` : "");
      challenger.hidden = false;
    } else {
      challenger.hidden = true;
    }
    renderRecentForm(dossier.recentOutcomes);
    renderUpcomingFixtures(dossier.upcomingFixtures);
    renderResearchEvidence(dossier.researchEvidence);
  } catch (error) {
    if (request !== dossierRequest) return;
    setDossierState(
      "error",
      "Official form and fixtures could not be loaded. Forecast preview remains available.",
    );
    renderEmpty(document.querySelector("#recent-form"), "Player history is temporarily unavailable.");
    renderEmpty(document.querySelector("#upcoming-fixtures"), "Fixtures are temporarily unavailable.");
    setResearchSummary(
      "Research unavailable",
      "Forecast and official data remain available.",
      "warning",
    );
    renderEmpty(
      document.querySelector("#research-evidence"),
      "Research evidence could not be loaded. Forecast and official data remain available.",
    );
    console.error(error);
  }
}

function playersForSelection(selection) {
  const starting = new Set(selection.startingPlayerIds);
  const benchOrder = new Map([
    [selection.replacementGoalkeeperPlayerId, 1],
    ...selection.outfieldSubstitutePlayerIds.map((playerId, index) => [
      playerId,
      index + 2,
    ]),
  ]);
  return advice.selection.players.map((player) => ({
    ...player,
    lineupPlace: starting.has(player.playerId) ? "starting" : "bench",
    benchOrder: benchOrder.get(player.playerId) ?? null,
    captaincy:
      player.playerId === selection.captainPlayerId
        ? "captain"
        : player.playerId === selection.viceCaptainPlayerId
          ? "vice-captain"
          : null,
  }));
}

function renderSquadPlayers(players, isOwnerSelection = false) {
  displayedPlayers = players;
  document.querySelector("#formation-kicker").textContent = isOwnerSelection
    ? "Your current revision"
    : "Recommended selection";
  const starters = players.filter((player) => player.lineupPlace === "starting");
  const formation = document.querySelector("#formation");
  formation.replaceChildren();
  positionOrder.forEach((position) => {
    const row = document.createElement("div");
    row.className = "formation-row";
    row.dataset.position = position;
    starters
      .filter((player) => player.position === position)
      .forEach((player) => row.append(createPlayerCard(player)));
    formation.append(row);
  });

  const bench = document.querySelector("#bench");
  bench.replaceChildren();
  players
    .filter((player) => player.lineupPlace === "bench")
    .sort((left, right) => left.benchOrder - right.benchOrder)
    .forEach((player) => bench.append(createPlayerCard(player)));
  if (selectedPlayerId && players.some((player) => player.playerId === selectedPlayerId)) {
    selectPlayer(selectedPlayerId, { updateHistory: false, focusDossier: false });
  }
}

function renderAdvice(adviceDocument) {
  advice = adviceDocument;
  displayedPlayers = adviceDocument.selection.players;
  document.querySelector("#evidence-status").textContent =
    adviceDocument.evidenceStatus.replaceAll("-", " ");
  document.querySelector("#gameweek-label").textContent =
    `Gameweek ${adviceDocument.gameweek} decision room`;
  document.querySelector("#recommendation-summary").textContent =
    adviceDocument.recommendationSummary;
  document.querySelector("#deadline").textContent = formatDeadline(adviceDocument.deadlineUtc);
  document.querySelector("#decision-cutoff").textContent =
    adviceDocument.decisionCutoffUtc
      ? formatDeadline(adviceDocument.decisionCutoffUtc)
      : "Not persisted";
  const artifactLabel = document.querySelector("#artifact-reference-label");
  const artifactReference = document.querySelector("#snapshot-reference");
  if (
    adviceDocument.forecastArtifactId &&
    adviceDocument.forecastArtifactContentHash
  ) {
    artifactLabel.textContent = "Forecast";
    artifactReference.textContent =
      `#${adviceDocument.forecastArtifactId} · ${adviceDocument.forecastArtifactContentHash.slice(0, 8)}`;
  } else {
    artifactLabel.textContent = "Snapshot";
    artifactReference.textContent = adviceDocument.snapshotId
      ? `#${adviceDocument.snapshotId} · revision ${adviceDocument.snapshotRevision}`
      : "Preview only";
  }
  document.querySelector("#model-label").textContent = adviceDocument.modelLabel;
  document.querySelector("#team-points").textContent =
    adviceDocument.selection.expectedPoints.toFixed(1);
  document.querySelector("#selection-objective").textContent =
    adviceDocument.selection.objective;
  document.querySelector("#ai-status").textContent = adviceDocument.aiAccess.status;

  const callout = document.querySelector("#synthetic-callout");
  callout.dataset.realIdentities = String(
    !adviceDocument.isSynthetic,
  );
  document.querySelector("#forecast-kind").textContent = adviceDocument.isSynthetic
    ? "Synthetic preview"
    : "Baseline v0";
  document.querySelector("#forecast-callout-title").textContent =
    adviceDocument.isSynthetic ? "Forecast preview" : "Limited preseason evidence";
  document.querySelector("#forecast-callout-copy").textContent =
    adviceDocument.isSynthetic
      ? "Official identity and history; synthetic estimates until evidence is available."
      : "Real official inputs and a transparent market baseline; not yet out-of-time validated.";

  renderSquadPlayers(adviceDocument.selection.players);

  const alternatives = document.querySelector("#alternative-list");
  alternatives.replaceChildren();
  adviceDocument.alternatives.forEach((alternative) => {
    const card = document.createElement("article");
    card.className = "alternative-card";
    const title = document.createElement("strong");
    title.textContent = alternative.name;
    const description = document.createElement("p");
    description.textContent = alternative.objective;
    const delta = document.createElement("span");
    delta.textContent = alternative.name === "My selection"
      ? "Not created yet"
      : `${alternative.expectedPoints.toFixed(1)} pts · ${alternative.difference >= 0 ? "+" : ""}${alternative.difference.toFixed(1)}`;
    card.append(title, description, delta);
    alternatives.append(card);
  });

  const requestedPlayerId = Number(new URL(window.location.href).searchParams.get("player"));
  const starters = displayedPlayers.filter(
    (player) => player.lineupPlace === "starting",
  );
  const initialPlayer = displayedPlayers.find(
    (player) => player.playerId === requestedPlayerId,
  ) ?? starters.find((player) => player.captaincy === "captain") ?? starters[0];
  selectPlayer(initialPlayer.playerId, {
    updateHistory: false,
    focusDossier: false,
  });
}

function setSelectionRail(status) {
  const order = ["draft", "locked", "frozen"];
  const current = status === "expired" ? "draft" : status;
  const currentIndex = order.indexOf(current);
  document.querySelectorAll("[data-selection-step]").forEach((step) => {
    const index = order.indexOf(step.dataset.selectionStep);
    step.classList.toggle("is-complete", currentIndex >= 0 && index < currentIndex);
    step.classList.toggle("is-current", index === currentIndex);
  });
}

function setSelectionFeedback(message, state = null) {
  const panel = document.querySelector("#selection-workflow");
  if (state) panel.dataset.state = state;
  document.querySelector("#selection-feedback").textContent = message;
}

function renderSelectionState(revision, feedback = "") {
  selectionRevision = revision;
  const panel = document.querySelector("#selection-workflow");
  const title = document.querySelector("#selection-workflow-title");
  const state = document.querySelector("#selection-state");
  const summary = document.querySelector("#selection-workflow-summary");
  const create = document.querySelector("#create-selection-draft");
  const edit = document.querySelector("#edit-selection");
  const lock = document.querySelector("#lock-selection");
  const revisionLabel = document.querySelector("#selection-revision");
  const forecast = document.querySelector("#selection-forecast");
  const lockedAt = document.querySelector("#selection-locked-at");

  create.disabled = true;
  edit.disabled = true;
  lock.disabled = true;
  if (!revision) {
    if (
      advice
      && document.querySelector("#formation-kicker").textContent
        !== "Recommended selection"
    ) {
      renderSquadPlayers(advice.selection.players);
    }
    const hasForecast = Boolean(advice?.forecastArtifactId);
    const deadlinePassed = advice
      ? new Date(advice.deadlineUtc).getTime() <= Date.now()
      : false;
    panel.dataset.state = hasForecast && !deadlinePassed ? "uncreated" : "unavailable";
    title.textContent = hasForecast
      ? deadlinePassed
        ? "The deadline passed without a saved selection."
        : "This prediction is not your decision yet."
      : "A persisted real forecast is required.";
    state.textContent = hasForecast && !deadlinePassed ? "Not started" : "Unavailable";
    summary.textContent = hasForecast
      ? deadlinePassed
        ? "autoFPL will not backdate a draft or lock after the recorded deadline."
        : "Preserve the exact predicted XI, bench and captaincy as a draft before you explicitly lock it."
      : "Synthetic previews remain inspectable, but they cannot become an authoritative user selection.";
    revisionLabel.textContent = "None";
    forecast.textContent = advice?.forecastArtifactId
      ? `#${advice.forecastArtifactId}`
      : "Preview only";
    lockedAt.textContent = "Not locked";
    create.disabled = !hasForecast || deadlinePassed;
    setSelectionRail("");
    setSelectionFeedback(feedback);
    return;
  }

  panel.dataset.state = revision.status;
  renderSquadPlayers(playersForSelection(revision.selection), true);
  state.textContent = revision.status;
  revisionLabel.textContent = `r${revision.revision} · #${revision.selectionRevisionId}`;
  forecast.textContent =
    `#${revision.forecastArtifactId} · ${revision.forecastArtifactContentHash.slice(0, 8)}`;
  lockedAt.textContent = revision.lockedAtUtc
    ? formatCompactInstant(revision.lockedAtUtc)
    : "Not locked";
  setSelectionRail(revision.status);

  if (revision.status === "draft") {
    title.textContent = `Draft revision ${revision.revision} is ready to lock.`;
    summary.textContent =
      "Review or edit this immutable revision. Locking needs a separate confirmation and does not submit anything to FPL.";
    edit.disabled = false;
    lock.disabled = !revision.canLock;
  } else if (revision.status === "locked") {
    title.textContent = `Your Gameweek ${revision.gameweek} selection is locked.`;
    summary.textContent =
      "This is the current owner-approved choice. Any later pre-deadline change must become and lock a newer revision.";
    edit.disabled = false;
  } else if (revision.status === "frozen") {
    title.textContent = "Your deadline selection is frozen.";
    summary.textContent =
      "The stored choice no longer changes. Only deterministic official substitutions and captain fallback can affect the effective XI.";
  } else {
    title.textContent = "The deadline passed without locking this draft.";
    summary.textContent =
      "autoFPL preserves the draft as history but will not treat it as your approved Gameweek selection.";
  }
  setSelectionFeedback(feedback, revision.status);
}

function playerForEdit(playerId) {
  return advice.selection.players.find((player) => player.playerId === playerId);
}

function editPlayerLabel(playerId) {
  const player = playerForEdit(playerId);
  return `${player.name} · ${player.clubShortName} · ${player.position.slice(0, 3).toUpperCase()}`;
}

function fillPlayerSelect(select, playerIds, selectedPlayerId) {
  select.replaceChildren(
    ...playerIds.map((playerId) => {
      const option = document.createElement("option");
      option.value = String(playerId);
      option.textContent = editPlayerLabel(playerId);
      option.selected = playerId === selectedPlayerId;
      return option;
    }),
  );
}

function renderSelectionEditor() {
  fillPlayerSelect(
    document.querySelector("#edit-swap-out"),
    selectionEditDraft.startingPlayerIds,
    selectionEditDraft.startingPlayerIds[0],
  );
  const benchIds = [
    selectionEditDraft.replacementGoalkeeperPlayerId,
    ...selectionEditDraft.outfieldSubstitutePlayerIds,
  ];
  fillPlayerSelect(
    document.querySelector("#edit-swap-in"),
    benchIds,
    benchIds[0],
  );
  fillPlayerSelect(
    document.querySelector("#edit-captain"),
    selectionEditDraft.startingPlayerIds,
    selectionEditDraft.captainPlayerId,
  );
  fillPlayerSelect(
    document.querySelector("#edit-vice-captain"),
    selectionEditDraft.startingPlayerIds,
    selectionEditDraft.viceCaptainPlayerId,
  );
  document.querySelector("#edit-goalkeeper").textContent =
    `Goalkeeper reserve · ${editPlayerLabel(selectionEditDraft.replacementGoalkeeperPlayerId)}`;

  const bench = document.querySelector("#edit-bench-order");
  bench.replaceChildren(
    ...selectionEditDraft.outfieldSubstitutePlayerIds.map((playerId, index) => {
      const item = document.createElement("li");
      const label = document.createElement("span");
      label.textContent = `${index + 1}. ${editPlayerLabel(playerId)}`;
      const actions = document.createElement("span");
      actions.className = "edit-order-actions";
      const up = document.createElement("button");
      up.type = "button";
      up.textContent = "↑";
      up.disabled = index === 0;
      up.setAttribute("aria-label", `Move ${playerForEdit(playerId).name} earlier`);
      up.addEventListener("click", () => moveBenchPlayer(index, -1));
      const down = document.createElement("button");
      down.type = "button";
      down.textContent = "↓";
      down.disabled =
        index === selectionEditDraft.outfieldSubstitutePlayerIds.length - 1;
      down.setAttribute("aria-label", `Move ${playerForEdit(playerId).name} later`);
      down.addEventListener("click", () => moveBenchPlayer(index, 1));
      actions.append(up, down);
      item.append(label, actions);
      return item;
    }),
  );
}

function setEditFeedback(message, isError = false) {
  const feedback = document.querySelector("#edit-feedback");
  feedback.textContent = message;
  feedback.dataset.error = String(isError);
}

function moveBenchPlayer(index, direction) {
  const target = index + direction;
  const order = selectionEditDraft.outfieldSubstitutePlayerIds;
  [order[index], order[target]] = [order[target], order[index]];
  renderSelectionEditor();
  setEditFeedback("Bench priority updated. Save to create the revision.");
}

function applySelectionSwap() {
  const outgoing = Number(document.querySelector("#edit-swap-out").value);
  const incoming = Number(document.querySelector("#edit-swap-in").value);
  const outgoingPlayer = playerForEdit(outgoing);
  const incomingPlayer = playerForEdit(incoming);
  const outgoingIsGoalkeeper = outgoingPlayer.position === "goalkeeper";
  const incomingIsGoalkeeper = incomingPlayer.position === "goalkeeper";
  if (outgoingIsGoalkeeper !== incomingIsGoalkeeper) {
    setEditFeedback(
      "A goalkeeper can only swap with the reserve goalkeeper.",
      true,
    );
    return;
  }

  const starterIndex = selectionEditDraft.startingPlayerIds.indexOf(outgoing);
  selectionEditDraft.startingPlayerIds[starterIndex] = incoming;
  if (incomingIsGoalkeeper) {
    selectionEditDraft.replacementGoalkeeperPlayerId = outgoing;
  } else {
    const benchIndex =
      selectionEditDraft.outfieldSubstitutePlayerIds.indexOf(incoming);
    selectionEditDraft.outfieldSubstitutePlayerIds[benchIndex] = outgoing;
  }
  if (selectionEditDraft.captainPlayerId === outgoing) {
    selectionEditDraft.captainPlayerId = incoming;
  }
  if (selectionEditDraft.viceCaptainPlayerId === outgoing) {
    selectionEditDraft.viceCaptainPlayerId = incoming;
  }
  renderSelectionEditor();
  setEditFeedback(
    `${outgoingPlayer.name} and ${incomingPlayer.name} swapped. Save to validate the formation.`,
  );
}

function openSelectionEditDialog() {
  if (!selectionRevision || !["draft", "locked"].includes(selectionRevision.status)) {
    return;
  }
  selectionEditDraft = {
    startingPlayerIds: [...selectionRevision.selection.startingPlayerIds],
    captainPlayerId: selectionRevision.selection.captainPlayerId,
    viceCaptainPlayerId: selectionRevision.selection.viceCaptainPlayerId,
    replacementGoalkeeperPlayerId:
      selectionRevision.selection.replacementGoalkeeperPlayerId,
    outfieldSubstitutePlayerIds: [
      ...selectionRevision.selection.outfieldSubstitutePlayerIds,
    ],
  };
  renderSelectionEditor();
  setEditFeedback("No changes saved yet.");
  document.querySelector("#edit-dialog").showModal();
}

async function saveSelectionRevision() {
  selectionEditDraft.captainPlayerId =
    Number(document.querySelector("#edit-captain").value);
  selectionEditDraft.viceCaptainPlayerId =
    Number(document.querySelector("#edit-vice-captain").value);
  if (selectionEditDraft.captainPlayerId === selectionEditDraft.viceCaptainPlayerId) {
    setEditFeedback("Captain and vice-captain must be different.", true);
    return;
  }
  const save = document.querySelector("#save-selection-revision");
  save.disabled = true;
  save.textContent = "Saving…";
  try {
    const response = await fetch(
      `/api/v1/selections/${selectionRevision.selectionRevisionId}/revisions`,
      {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        body: JSON.stringify(selectionEditDraft),
      },
    );
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.code ?? `revision request failed with ${response.status}`);
    }
    const previousRevisionId = selectionRevision.selectionRevisionId;
    const revised = await response.json();
    renderSelectionState(
      revised,
      revised.selectionRevisionId === previousRevisionId
        ? "No changes detected. The current revision remains unchanged."
        : "New draft saved. Review it, then lock this revision explicitly.",
    );
    document.querySelector("#edit-dialog").close();
  } catch (error) {
    setEditFeedback(
      error.message === "lineup.formation.invalid"
        ? "That swap creates an invalid FPL formation. Keep at least three defenders and one forward."
        : "The revision was not saved. Reload the current selection before retrying.",
      true,
    );
    console.error(error);
  } finally {
    save.disabled = false;
    save.textContent = "Save new draft";
  }
}

async function loadSelectionState() {
  if (!advice?.forecastArtifactId) {
    renderSelectionState(null);
    return;
  }

  try {
    const response = await fetch("/api/v1/selections/current", {
      headers: { Accept: "application/json" },
    });
    if (response.status === 404) {
      renderSelectionState(null);
      return;
    }
    if (!response.ok) {
      throw new Error(`Selection request failed with ${response.status}`);
    }
    renderSelectionState(await response.json());
  } catch (error) {
    selectionRevision = null;
    document.querySelector("#selection-workflow-title").textContent =
      "Selection state could not be loaded.";
    document.querySelector("#selection-state").textContent = "Unavailable";
    document.querySelector("#selection-workflow-summary").textContent =
      "The forecast remains visible, but drafting and locking stay unavailable until persisted state can be verified.";
    document.querySelector("#create-selection-draft").disabled = true;
    document.querySelector("#lock-selection").disabled = true;
    setSelectionRail("");
    setSelectionFeedback("Selection storage check failed.", "error");
    console.error(error);
  }
}

async function createSelectionDraft() {
  if (!advice?.forecastArtifactId) return;
  const create = document.querySelector("#create-selection-draft");
  create.disabled = true;
  create.textContent = "Creating draft…";
  setSelectionFeedback("Preserving the forecast selection as an immutable draft.");
  try {
    const response = await fetch("/api/v1/selections/drafts", {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ forecastArtifactId: advice.forecastArtifactId }),
    });
    if (!response.ok) {
      throw new Error(`Draft request failed with ${response.status}`);
    }
    renderSelectionState(
      await response.json(),
      "Draft created. Review the squad, then lock it when you are satisfied.",
    );
  } catch (error) {
    setSelectionFeedback(
      "The draft was not created. Refresh the forecast and try again.",
      "error",
    );
    create.disabled = false;
    console.error(error);
  } finally {
    create.textContent = "Use prediction as draft";
  }
}

function openSelectionLockDialog() {
  if (!selectionRevision?.canLock) return;
  document.querySelector("#lock-dialog-copy").textContent =
    `Revision ${selectionRevision.revision} will become your chosen Gameweek ${selectionRevision.gameweek} selection before ${formatDeadline(selectionRevision.deadlineUtc)}.`;
  document.querySelector("#lock-dialog").showModal();
}

async function confirmSelectionLock() {
  if (!selectionRevision?.canLock) return;
  const dialog = document.querySelector("#lock-dialog");
  const confirm = document.querySelector("#confirm-selection-lock");
  confirm.disabled = true;
  confirm.textContent = "Locking…";
  setSelectionFeedback("Recording your explicit lock.");
  try {
    const response = await fetch(
      `/api/v1/selections/${selectionRevision.selectionRevisionId}/lock`,
      {
        method: "PUT",
        headers: { Accept: "application/json" },
      },
    );
    if (!response.ok) {
      throw new Error(`Lock request failed with ${response.status}`);
    }
    renderSelectionState(
      await response.json(),
      "Selection locked. No action was sent to your FPL account.",
    );
    dialog.close();
  } catch (error) {
    dialog.close();
    setSelectionFeedback(
      "The selection was not locked. Reload its current state before trying again.",
      "error",
    );
    console.error(error);
  } finally {
    confirm.disabled = false;
    confirm.textContent = "Confirm lock";
  }
}

async function loadAdvice() {
  const refresh = document.querySelector("#refresh-prediction");
  refresh.disabled = true;
  refresh.textContent = advice ? "Refreshing…" : "Building…";
  try {
    const response = await fetch("/api/v1/advice/demo", {
      headers: { Accept: "application/json" },
    });
    if (!response.ok) throw new Error(`Advice request failed with ${response.status}`);
    renderAdvice(await response.json());
    await loadSelectionState();
  } catch (error) {
    document.querySelector("#evidence-status").textContent = "Evidence unavailable";
    document.querySelector("#recommendation-summary").textContent =
      "The preview could not load. Check that the autoFPL API is running and try again.";
    console.error(error);
  } finally {
    refresh.disabled = false;
    refresh.textContent = "Refresh prediction";
  }
}

function setOfficialDataState(state, label, title, summary) {
  const panel = document.querySelector("#official-data");
  panel.dataset.state = state;
  document.querySelector("#source-state").textContent = label;
  document.querySelector("#source-title").textContent = title;
  document.querySelector("#source-summary").textContent = summary;
}

function renderCaptureCounts(capture) {
  document.querySelector("#source-player-count").textContent =
    capture.playerCount.toLocaleString();
  document.querySelector("#source-fixture-count").textContent =
    capture.fixtureCount.toLocaleString();
  document.querySelector("#source-capture-id").textContent = `#${capture.captureId}`;
  document.querySelector("#capture-time").textContent =
    formatCompactInstant(capture.availableAtUtc);
  document.querySelector("#source-deadline").textContent = capture.nextDeadlineUtc
    ? formatCompactInstant(capture.nextDeadlineUtc)
    : "Season complete";
}

async function loadOfficialData() {
  const outcomeStatus = document.querySelector("#source-outcome-status");
  try {
    const captureResponse = await fetch("/api/v1/data/official-fpl/latest", {
      headers: { Accept: "application/json" },
    });
    if (captureResponse.status === 404) {
      setOfficialDataState(
        "empty",
        "Not captured",
        "No real source capture yet.",
        "Run the bounded official FPL import before treating this screen as replayable evidence.",
      );
      document.querySelector("#capture-lead-time").textContent = "No provenance available";
      return;
    }
    if (!captureResponse.ok) {
      throw new Error(`Official data request failed with ${captureResponse.status}`);
    }

    const capture = await captureResponse.json();
    renderCaptureCounts(capture);
    if (!capture.nextGameweekNumber || !capture.nextDeadlineUtc) {
      setOfficialDataState(
        "partial",
        "Captured",
        "Real source captured. No next deadline is published.",
        `${capture.seasonCode} capture #${capture.captureId} is immutable and available for later analysis.`,
      );
      document.querySelector("#capture-lead-time").textContent =
        `Available ${formatCompactInstant(capture.availableAtUtc)}`;
      return;
    }

    const replayPath =
      `/api/v1/data/official-fpl/replays/${encodeURIComponent(capture.seasonCode)}` +
      `/${capture.nextGameweekNumber}/pre-deadline`;
    const replayResponse = await fetch(replayPath, {
      headers: { Accept: "application/json" },
    });
    if (replayResponse.status === 404) {
      setOfficialDataState(
        "partial",
        "Captured",
        "Real source captured. Replay cutoff not met.",
        `Capture #${capture.captureId} is stored, but no Gameweek ${capture.nextGameweekNumber} capture qualifies as pre-deadline evidence.`,
      );
      document.querySelector("#capture-lead-time").textContent =
        "No qualifying pre-deadline capture";
      return;
    }
    if (!replayResponse.ok) {
      throw new Error(`Replay request failed with ${replayResponse.status}`);
    }

    const replay = await replayResponse.json();
    document.querySelector("#capture-time").textContent =
      formatCompactInstant(replay.captureAvailableAtUtc);
    document.querySelector("#source-deadline").textContent =
      formatCompactInstant(replay.deadlineUtc);
    document.querySelector("#source-capture-id").textContent =
      `#${replay.selectedCaptureId}`;
    document.querySelector("#capture-lead-time").textContent =
      formatLeadTime(replay.captureLeadTimeSeconds);
    setOfficialDataState(
      "ready",
      "Replay ready",
      "Official evidence is ready for prediction.",
      `Gameweek ${replay.gameweek} can be rebuilt from capture #${replay.selectedCaptureId} without using data retrieved after its deadline. The advice panel states whether the current forecast is synthetic or Baseline v0.`,
    );

    if (!capture.latestCompletedGameweek) {
      outcomeStatus.textContent = "Awaiting";
      return;
    }

    const outcomeGameweek = capture.latestCompletedGameweek;
    const pairPath =
      `/api/v1/data/official-fpl/replays/${encodeURIComponent(capture.seasonCode)}` +
      `/${outcomeGameweek}/outcome`;
    const pairResponse = await fetch(pairPath, {
      headers: { Accept: "application/json" },
    });
    if (pairResponse.status === 404) {
      outcomeStatus.textContent = "Awaiting";
      return;
    }
    if (!pairResponse.ok) {
      throw new Error(`Outcome pair request failed with ${pairResponse.status}`);
    }

    const pair = await pairResponse.json();
    outcomeStatus.textContent = "Paired";
    document.querySelector("#capture-time").textContent =
      formatCompactInstant(pair.replay.captureAvailableAtUtc);
    document.querySelector("#source-deadline").textContent =
      formatCompactInstant(pair.replay.deadlineUtc);
    document.querySelector("#source-capture-id").textContent =
      `#${pair.replay.selectedCaptureId}`;
    document.querySelector("#capture-lead-time").textContent =
      formatLeadTime(pair.replay.captureLeadTimeSeconds);
    setOfficialDataState(
      "ready",
      "Outcome paired",
      "Replay and final outcome are fully matched.",
      `Gameweek ${pair.replay.gameweek} links cutoff-safe capture #${pair.replay.selectedCaptureId} to official outcome #${pair.outcome.outcomeCaptureId} across all ${pair.matchedPlayerCount.toLocaleString()} players. The advice panel states the current forecast maturity.`,
    );
  } catch (error) {
    outcomeStatus.textContent = "Unknown";
    setOfficialDataState(
      "error",
      "Unavailable",
      "The real data footing could not be checked.",
      "The decision-room preview still works, but source provenance is unavailable until the data API recovers.",
    );
    document.querySelector("#capture-lead-time").textContent = "Provenance check failed";
    console.error(error);
  }
}

function setForecastSourceState(state, label, title, summary) {
  const panel = document.querySelector("#forecast-source");
  panel.dataset.state = state;
  document.querySelector("#forecast-source-state").textContent = label;
  document.querySelector("#forecast-source-title").textContent = title;
  document.querySelector("#forecast-source-summary").textContent = summary;
}

async function loadForecastSource() {
  const checked = document.querySelector("#forecast-source-checked");
  const capture = document.querySelector("#forecast-source-capture");
  const coverage = document.querySelector("#forecast-source-coverage");
  try {
    const response = await fetch("/api/v1/data/fpl-form-forecast/status", {
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      throw new Error(`Forecast source request failed with ${response.status}`);
    }

    const source = await response.json();
    checked.textContent = source.checkedAtUtc
      ? formatCompactInstant(source.checkedAtUtc)
      : "Not run";

    if (source.latestCapture) {
      capture.textContent =
        `${source.latestCapture.seasonCode} · GW${source.latestCapture.gameweek} · #${source.latestCapture.captureId}`;
      coverage.textContent =
        `${source.latestCapture.playerCount.toLocaleString()} players · ${source.latestCapture.fixturePredictionCount.toLocaleString()} fixtures`;
    } else {
      capture.textContent = "None retained";
      coverage.textContent = "Awaiting active Gameweek";
    }

    if (source.status === "captured") {
      setForecastSourceState(
        "ready",
        "Captured",
        "Public forecast retained for evaluation.",
        "The values remain a challenger until identity coverage and out-of-time scoring beat the declared baselines.",
      );
      return;
    }
    if (source.status === "waiting") {
      setForecastSourceState(
        "waiting",
        "Waiting",
        "FPL Form has no active forecast.",
        "The collector reached the provider successfully. It will retain predictions only when an active next-Gameweek forecast is published.",
      );
      return;
    }
    if (source.status === "failed") {
      setForecastSourceState(
        "error",
        "Check failed",
        "The public forecast could not be checked.",
        source.latestCapture
          ? "The last immutable capture remains available; no partial replacement was stored."
          : "No partial forecast was stored. Official FPL evidence and the decision room remain available.",
      );
      return;
    }

    setForecastSourceState(
      "empty",
      "Not checked",
      "No FPL Form collection has run.",
      "The decision room will show source readiness after the bounded operator import runs.",
    );
  } catch (error) {
    checked.textContent = "Unknown";
    capture.textContent = "Unavailable";
    coverage.textContent = "Not evaluated";
    setForecastSourceState(
      "error",
      "Unavailable",
      "Forecast-source status could not be loaded.",
      "Official FPL evidence and the core decision room remain available.",
    );
    console.error(error);
  }
}

function setResearchCoverageState(state, label, title, summary) {
  const panel = document.querySelector("#research-coverage");
  panel.dataset.state = state;
  document.querySelector("#research-coverage-state").textContent = label;
  document.querySelector("#research-coverage-title").textContent = title;
  document.querySelector("#research-coverage-summary").textContent = summary;
}

function renderClubCoverage(teams) {
  const board = document.querySelector("#club-coverage-board");
  board.replaceChildren();
  for (const team of teams) {
    const club = document.createElement("div");
    club.className = "club-coverage";
    club.dataset.status = team.status;
    club.setAttribute("role", "listitem");
    club.setAttribute(
      "aria-label",
      `${team.teamName}: ${team.classifiedPlayerCount} of ${team.playerCount} players classified, ${team.status}`,
    );

    const name = document.createElement("strong");
    name.textContent = team.teamShortName;
    const count = document.createElement("span");
    count.textContent = `${team.classifiedPlayerCount}/${team.playerCount}`;
    club.append(name, count);
    board.append(club);
  }
}

function renderResearchCoverageEmpty(message) {
  const board = document.querySelector("#club-coverage-board");
  board.replaceChildren();
  const empty = document.createElement("p");
  empty.className = "club-coverage-empty";
  empty.textContent = message;
  board.append(empty);
}

async function loadResearchCoverage() {
  const playerCoverage = document.querySelector("#research-player-coverage");
  const clubCoverage = document.querySelector("#research-club-coverage");
  const capturedAt = document.querySelector("#research-captured-at");
  try {
    const response = await fetch("/api/v1/research/sources", {
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      throw new Error(`Research coverage request failed with ${response.status}`);
    }

    const inventory = await response.json();
    const coverage = inventory.latestStartCoverage?.find(
      (item) => item.sourceKey === "ffscout-predicted-lineups",
    );
    if (!coverage) {
      playerCoverage.textContent = "0 · no snapshot";
      clubCoverage.textContent = "0 · awaiting capture";
      capturedAt.textContent = "None retained";
      renderResearchCoverageEmpty(
        "No FFScout snapshot has been retained for the current official target.",
      );
      setResearchCoverageState(
        "empty",
        "Awaiting",
        "No team sheet retained yet.",
        "The bounded research refresh will populate club coverage when a pre-deadline source page is available.",
      );
      return;
    }

    playerCoverage.textContent =
      `${coverage.classifiedPlayerCount.toLocaleString()} / ${coverage.playerCount.toLocaleString()}`;
    clubCoverage.textContent =
      `${coverage.completeTeamCount} / ${coverage.teams.length}`;
    capturedAt.textContent =
      `GW${coverage.gameweek} · ${formatCompactInstant(coverage.retrievedAtUtc)}`;
    renderClubCoverage(coverage.teams);

    if (coverage.classifiedPlayerCount === 0) {
      setResearchCoverageState(
        "warning",
        "Not extracted",
        "Snapshot retained; classifications pending.",
        "The page is safely stored, but no exact snapshot-linked start claims have been extracted yet.",
      );
      return;
    }

    const gaps = coverage.partialTeamCount + coverage.missingTeamCount;
    if (gaps === 0) {
      setResearchCoverageState(
        "ready",
        "Complete",
        "All clubs have a classified team sheet.",
        `${coverage.predictedStarterCount} starters and ${coverage.predictedNonStarterCount} non-starters are linked to the retained FFScout snapshot.`,
      );
      return;
    }

    setResearchCoverageState(
      "warning",
      "Gaps visible",
      "The team sheet is useful, not complete.",
      `${coverage.completeTeamCount} clubs are complete, ${coverage.partialTeamCount} partial and ${coverage.missingTeamCount} missing. Unknown players remain unknown.`,
    );
  } catch (error) {
    playerCoverage.textContent = "Unavailable";
    clubCoverage.textContent = "Unavailable";
    capturedAt.textContent = "Unknown";
    renderResearchCoverageEmpty(
      "Club-level research coverage could not be loaded.",
    );
    setResearchCoverageState(
      "error",
      "Unavailable",
      "The research team sheet could not be checked.",
      "The official data footing and prediction remain available; research gaps cannot be audited until this route recovers.",
    );
    console.error(error);
  }
}

function trapDossierFocus(event) {
  if (
    event.key !== "Tab" ||
    !mobileDossierQuery.matches ||
    !document.querySelector("#player-dossier").classList.contains("is-open")
  ) {
    return;
  }

  const dossier = document.querySelector("#player-dossier");
  const focusable = [...dossier.querySelectorAll("button, a, input, [tabindex='0']")]
    .filter((element) => !element.disabled && element.offsetParent !== null);
  if (!focusable.length) return;
  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

document.querySelector("#dossier-close").addEventListener("click", closeDossier);
document.querySelector("#dossier-backdrop").addEventListener("click", closeDossier);
document.querySelector("#player-dossier").addEventListener("keydown", trapDossierFocus);
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") closeDossier();
});
mobileDossierQuery.addEventListener("change", (event) => {
  if (event.matches) {
    closeDossier();
  } else if (selectedPlayerId !== null) {
    openDossier(false);
  }
});
window.addEventListener("popstate", () => {
  if (!advice) return;
  const playerId = Number(new URL(window.location.href).searchParams.get("player"));
  if (displayedPlayers.some((player) => player.playerId === playerId)) {
    selectPlayer(playerId, { updateHistory: false, focusDossier: false });
  }
});
document.querySelector("#ai-form").addEventListener("submit", (event) => event.preventDefault());
document.querySelector("#refresh-prediction").addEventListener("click", loadAdvice);
document.querySelector("#create-selection-draft").addEventListener(
  "click",
  createSelectionDraft,
);
document.querySelector("#edit-selection").addEventListener(
  "click",
  openSelectionEditDialog,
);
document.querySelector("#apply-selection-swap").addEventListener(
  "click",
  applySelectionSwap,
);
document.querySelector("#edit-captain").addEventListener("change", (event) => {
  if (selectionEditDraft) {
    selectionEditDraft.captainPlayerId = Number(event.target.value);
  }
});
document.querySelector("#edit-vice-captain").addEventListener("change", (event) => {
  if (selectionEditDraft) {
    selectionEditDraft.viceCaptainPlayerId = Number(event.target.value);
  }
});
document.querySelector("#save-selection-revision").addEventListener(
  "click",
  saveSelectionRevision,
);
document.querySelector("#lock-selection").addEventListener(
  "click",
  openSelectionLockDialog,
);
document.querySelector("#confirm-selection-lock").addEventListener(
  "click",
  confirmSelectionLock,
);

Promise.all([
  loadAdvice(),
  loadOfficialData(),
  loadForecastSource(),
  loadResearchCoverage(),
]);
