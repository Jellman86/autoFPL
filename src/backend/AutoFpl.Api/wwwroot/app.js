const positionOrder = ["forward", "midfielder", "defender", "goalkeeper"];
const mobileDossierQuery = window.matchMedia("(max-width: 980px)");

let selectedPlayerId = null;
let advice = null;
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
  const player = advice?.selection.players.find(
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

async function loadPlayerDossier(player) {
  const request = ++dossierRequest;
  renderEmpty(document.querySelector("#recent-form"), "Loading previous Gameweeks…");
  renderEmpty(document.querySelector("#upcoming-fixtures"), "Loading upcoming fixtures…");

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
    return;
  }

  setDossierState("loading", "Loading cutoff-correct official identity, form and fixtures…");
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
    renderRecentForm(dossier.recentOutcomes);
    renderUpcomingFixtures(dossier.upcomingFixtures);
  } catch (error) {
    if (request !== dossierRequest) return;
    setDossierState(
      "error",
      "Official form and fixtures could not be loaded. Forecast preview remains available.",
    );
    renderEmpty(document.querySelector("#recent-form"), "Player history is temporarily unavailable.");
    renderEmpty(document.querySelector("#upcoming-fixtures"), "Fixtures are temporarily unavailable.");
    console.error(error);
  }
}

function renderAdvice(adviceDocument) {
  advice = adviceDocument;
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

  const starters = adviceDocument.selection.players.filter(
    (player) => player.lineupPlace === "starting",
  );
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
  adviceDocument.selection.players
    .filter((player) => player.lineupPlace === "bench")
    .sort((left, right) => left.benchOrder - right.benchOrder)
    .forEach((player) => bench.append(createPlayerCard(player)));

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
  const initialPlayer = adviceDocument.selection.players.find(
    (player) => player.playerId === requestedPlayerId,
  ) ?? starters.find((player) => player.captaincy === "captain") ?? starters[0];
  selectPlayer(initialPlayer.playerId, {
    updateHistory: false,
    focusDossier: false,
  });
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
  if (advice.selection.players.some((player) => player.playerId === playerId)) {
    selectPlayer(playerId, { updateHistory: false, focusDossier: false });
  }
});
document.querySelector("#ai-form").addEventListener("submit", (event) => event.preventDefault());
document.querySelector("#refresh-prediction").addEventListener("click", loadAdvice);

Promise.all([loadAdvice(), loadOfficialData(), loadForecastSource()]);
