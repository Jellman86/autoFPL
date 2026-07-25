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

document.querySelector("#ai-form").addEventListener("submit", (event) => event.preventDefault());
loadAdvice();
