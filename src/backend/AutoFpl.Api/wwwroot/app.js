const positionOrder = ["forward", "midfielder", "defender", "goalkeeper"];
let selectedPlayerId = null;
let advice = null;

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

function formatLeadTime(totalSeconds) {
  const hours = Math.floor(totalSeconds / 3600);
  const days = Math.floor(hours / 24);
  const remainingHours = hours % 24;
  if (days > 0) {
    return `${days}d ${remainingHours}h before deadline`;
  }
  if (hours > 0) {
    return `${hours}h before deadline`;
  }
  return `${Math.max(0, Math.floor(totalSeconds / 60))}m before deadline`;
}

function createPlayerCard(player) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "player-card";
  button.dataset.playerId = String(player.playerId);
  button.setAttribute("aria-pressed", String(player.playerId === selectedPlayerId));
  button.setAttribute(
    "aria-label",
    `${player.name}, ${player.expectedPoints} expected points, ${player.expectedMinutes} expected minutes`,
  );

  if (player.captaincy) {
    const captain = document.createElement("span");
    captain.className = "captain";
    captain.textContent = player.captaincy === "captain" ? "C" : "VC";
    button.append(captain);
  }

  if (player.benchOrder) {
    const order = document.createElement("span");
    order.className = "bench-order";
    order.textContent = String(player.benchOrder);
    button.append(order);
  }

  const club = document.createElement("span");
  club.className = "club";
  club.textContent = `${player.clubShortName} · ${player.position.slice(0, 3).toUpperCase()}`;

  const name = document.createElement("span");
  name.className = "name";
  name.textContent = player.name;

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
  fixture.textContent = `${player.isHome ? "vs" : "at"} ${player.opponent}`;

  button.append(club, name, projection, range, fixture);
  button.addEventListener("click", () => selectPlayer(player.playerId));
  return button;
}

function selectPlayer(playerId) {
  selectedPlayerId = playerId;
  const player = advice.selection.players.find((candidate) => candidate.playerId === playerId);
  if (!player) return;

  document.querySelectorAll(".player-card").forEach((card) => {
    card.setAttribute("aria-pressed", String(Number(card.dataset.playerId) === playerId));
  });

  document.querySelector("#player-name").textContent = player.name;
  document.querySelector("#player-context").textContent =
    `${player.clubShortName} · ${player.position} · ${player.isHome ? "home to" : "away at"} ${player.opponent}`;
  document.querySelector("#player-points").textContent = player.expectedPoints.toFixed(1);
  document.querySelector("#player-minutes").textContent = `${player.expectedMinutes}′`;
  document.querySelector("#player-range").textContent = `${player.lower80.toFixed(0)}–${player.upper80.toFixed(0)}`;

  const badge = document.querySelector("#player-badge");
  const badgeText = player.captaincy
    ? player.captaincy
    : player.lineupPlace === "bench"
      ? `bench ${player.benchOrder}`
      : "starter";
  badge.textContent = badgeText;
  badge.hidden = false;

  renderList("#player-reasons", player.reasons);
  renderList("#player-risks", player.risks);
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

function renderAdvice(adviceDocument) {
  advice = adviceDocument;
  document.querySelector("#evidence-status").textContent = adviceDocument.evidenceStatus.replace("-", " ");
  document.querySelector("#gameweek-label").textContent = `Gameweek ${adviceDocument.gameweek} decision room`;
  document.querySelector("#recommendation-summary").textContent = adviceDocument.recommendationSummary;
  document.querySelector("#deadline").textContent = formatDeadline(adviceDocument.deadlineUtc);
  document.querySelector("#decision-cutoff").textContent =
    adviceDocument.decisionCutoffUtc ? formatDeadline(adviceDocument.decisionCutoffUtc) : "Not persisted";
  document.querySelector("#snapshot-reference").textContent =
    adviceDocument.snapshotId
      ? `#${adviceDocument.snapshotId} · revision ${adviceDocument.snapshotRevision}`
      : "Preview only";
  document.querySelector("#model-label").textContent = adviceDocument.modelLabel;
  document.querySelector("#team-points").textContent = adviceDocument.selection.expectedPoints.toFixed(1);
  document.querySelector("#selection-objective").textContent = adviceDocument.selection.objective;
  document.querySelector("#ai-status").textContent = adviceDocument.aiAccess.status;

  const starters = adviceDocument.selection.players.filter((player) => player.lineupPlace === "starting");
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

  selectPlayer(starters.find((player) => player.captaincy === "captain")?.playerId ?? starters[0].playerId);
}

async function loadAdvice() {
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
      "Real source captured. Forecasts still synthetic.",
      `Gameweek ${replay.gameweek} can be rebuilt from capture #${replay.selectedCaptureId} without using data retrieved after its deadline.`,
    );

    const outcomeGameweek = capture.latestCompletedGameweek ?? replay.gameweek;
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
      `Gameweek ${pair.replay.gameweek} links cutoff-safe capture #${pair.replay.selectedCaptureId} to official outcome #${pair.outcome.outcomeCaptureId} across all ${pair.matchedPlayerCount.toLocaleString()} players. Forecasts remain synthetic.`,
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

document.querySelector("#ai-form").addEventListener("submit", (event) => event.preventDefault());
Promise.all([loadAdvice(), loadOfficialData()]);
