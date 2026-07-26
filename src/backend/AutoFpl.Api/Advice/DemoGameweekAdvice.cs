using AutoFpl.Contracts.Advice;
using AutoFpl.Contracts.Snapshots;

namespace AutoFpl.Api.Advice;

public static class DemoGameweekAdvice
{
    public static GameweekAdviceDocument Create(
        DecisionSnapshotDocument? snapshot = null,
        OfficialDecisionRoomPreview? officialPreview = null)
    {
        GameweekAdviceDocument fixture = new(
            SchemaVersion: "1.0",
            EvidenceStatus: snapshot is null ? "synthetic-preview" : "synthetic-persisted",
            IsSynthetic: true,
            SnapshotId: snapshot?.SnapshotId,
            SnapshotRevision: snapshot?.Revision,
            DecisionCutoffUtc: snapshot?.DecisionCutoffUtc,
            SnapshotContentHash: snapshot?.ContentHash,
            Gameweek: 1,
            DeadlineUtc: DateTimeOffset.Parse("2026-08-15T10:00:00Z"),
            GeneratedAtUtc: DateTimeOffset.Parse("2026-08-14T18:30:00Z"),
            ModelLabel: "UI contract fixture — not a fitted forecast",
            RecommendationSummary:
                "A balanced 4-4-2 built to preview how calibrated forecasts, uncertainty and selection evidence will appear.",
            Selection: new(
                Name: "Recommended",
                Objective: "Highest synthetic expected points subject to valid formation and captaincy rules.",
                ExpectedPoints: 61.42m,
                Players:
                [
                    Player(1, "Mara Voss", "HBR", "goalkeeper", "starting", null, null, "Northbridge", true, 4.2m, 1.0m, 8.0m, 90,
                        ["Strong clean-sheet outlook in the demo fixture.", "Highest expected minutes among the goalkeepers."],
                        ["Goalkeeper points remain sensitive to one conceded goal."]),
                    Player(3, "Theo March", "RIV", "defender", "starting", null, null, "Westford", true, 4.8m, 1.0m, 10.0m, 86,
                        ["Attacking involvement lifts the synthetic ceiling.", "Home clean-sheet probability supports selection."],
                        ["Wide role creates substitution risk late in the match."]),
                    Player(4, "Idris Cole", "HBR", "defender", "starting", null, null, "Northbridge", true, 4.5m, 1.0m, 9.0m, 90,
                        ["Secure minutes and a favourable home fixture.", "Reliable baseline keeps the lineup balanced."],
                        ["Limited attacking contribution in the fixture model."]),
                    Player(5, "Hugo Ramires", "CST", "defender", "starting", null, null, "Easton", false, 4.1m, 0.0m, 9.0m, 82,
                        ["Set-piece involvement adds upside.", "Still clears the fourth-defender decision margin."],
                        ["Away clean-sheet uncertainty is material."]),
                    Player(6, "Finn Ward", "WFD", "defender", "starting", null, null, "Riverside", true, 3.9m, 0.0m, 8.0m, 88,
                        ["Minutes security edges the first bench alternative.", "Home fixture supports the floor."],
                        ["Low attacking-event rate caps the ceiling."]),
                    Player(8, "Ari Vale", "MTR", "midfielder", "starting", null, "captain", "Albion", true, 7.3m, 2.0m, 14.0m, 84,
                        ["Best synthetic expected points in the squad.", "Penalty involvement and home advantage drive captaincy."],
                        ["The wide uncertainty band makes captaincy the largest decision risk."]),
                    Player(9, "Kian Mercer", "RIV", "midfielder", "starting", null, null, "Westford", true, 6.1m, 2.0m, 12.0m, 88,
                        ["High expected minutes and central attacking role.", "Combines a strong floor with useful upside."],
                        ["Output depends on a small number of high-value chances."]),
                    Player(10, "Noah Sol", "CST", "midfielder", "starting", null, "vice-captain", "Easton", false, 5.7m, 1.0m, 12.0m, 90,
                        ["Near-certain minutes make him the synthetic vice-captain.", "Stable involvement supports fallback captaincy."],
                        ["Away context lowers the central estimate."]),
                    Player(11, "Luca Hart", "HBR", "midfielder", "starting", null, null, "Northbridge", true, 5.2m, 1.0m, 11.0m, 79,
                        ["Creative role wins the final midfield place.", "Home matchup offers more upside than the bench."],
                        ["Expected minutes are less secure than the other starters."]),
                    Player(13, "Mateo Cruz", "MTR", "forward", "starting", null, null, "Albion", true, 6.4m, 1.0m, 13.0m, 81,
                        ["Highest synthetic goal probability among the forwards.", "Benefits from the same favourable home matchup as the captain."],
                        ["Recent workload makes a full match less certain."]),
                    Player(14, "Elliot King", "WFD", "forward", "starting", null, null, "Riverside", true, 5.1m, 1.0m, 11.0m, 76,
                        ["Starting probability keeps him ahead of the third forward.", "Home advantage supports the selection."],
                        ["Early substitution risk reduces his expected minutes."]),
                    Player(2, "Ren Ito", "NBR", "goalkeeper", "bench", 1, null, "Harbour", false, 3.1m, 0.0m, 7.0m, 90,
                        ["Provides the required replacement goalkeeper."],
                        ["Away clean-sheet probability trails the starter."]),
                    Player(12, "Samir Dane", "EAS", "midfielder", "bench", 2, null, "Castle", true, 4.4m, 1.0m, 10.0m, 72,
                        ["First outfield substitute because he can cover every legal formation.", "Useful ceiling if late team news changes."],
                        ["Starting probability is below the selected midfielders."]),
                    Player(7, "Ben Okoro", "ALB", "defender", "bench", 3, null, "Metro", false, 3.6m, 0.0m, 8.0m, 84,
                        ["Second outfield substitute preserves defensive coverage."],
                        ["Difficult away fixture limits the expected return."]),
                    Player(15, "Owen Pike", "NBR", "forward", "bench", 4, null, "Harbour", false, 3.4m, 0.0m, 8.0m, 61,
                        ["Retained as the final legal bench option."],
                        ["Low expected minutes make an attacking return unlikely."]),
                ]),
            Alternatives:
            [
                new("Safer minutes", "Prefer expected minutes when points are close.", 60.80m, -0.62m),
                new("Higher ceiling", "Prefer upper-tail points while preserving feasibility.", 62.15m, 0.73m),
                new("My selection", "A future user-authored comparison.", 0m, 0m),
            ],
            AiAccess: new(
                Available: false,
                Status:
                    "AI conversation is not connected in this preview. Planned access is through ChatGPT/Codex MCP or a server-side provider key.",
                PlannedModes: ["chatgpt-plugin", "mcp", "server-api-key", "compatible-provider"]));

        if (officialPreview is not null)
        {
            return ApplyOfficialPreview(fixture, officialPreview);
        }

        if (snapshot is null)
        {
            return fixture;
        }

        IReadOnlyDictionary<int, PersistedPlayerDocument> persistedPlayers =
            snapshot.Squad.Players.ToDictionary(player => player.PlayerId);
        var startingPlayerIds = snapshot.Selection.StartingPlayerIds.ToHashSet();
        IReadOnlyDictionary<int, int> benchOrders = new Dictionary<int, int>
        {
            [snapshot.Selection.ReplacementGoalkeeperPlayerId] = 1,
            [snapshot.Selection.OutfieldSubstitutePlayerIds[0]] = 2,
            [snapshot.Selection.OutfieldSubstitutePlayerIds[1]] = 3,
            [snapshot.Selection.OutfieldSubstitutePlayerIds[2]] = 4,
        };
        AdvicePlayerDocument[] players = fixture.Selection.Players
            .Select(player =>
            {
                PersistedPlayerDocument persistedPlayer = persistedPlayers[player.PlayerId];
                string? captaincy = player.PlayerId == snapshot.Selection.CaptainPlayerId
                    ? "captain"
                    : player.PlayerId == snapshot.Selection.ViceCaptainPlayerId
                        ? "vice-captain"
                        : null;
                int? benchOrder = benchOrders.TryGetValue(
                    player.PlayerId,
                    out int persistedBenchOrder)
                    ? persistedBenchOrder
                    : null;
                return player with
                {
                    Name = persistedPlayer.DisplayName,
                    Position = persistedPlayer.Position,
                    LineupPlace = startingPlayerIds.Contains(player.PlayerId)
                        ? "starting"
                        : "bench",
                    BenchOrder = benchOrder,
                    Captaincy = captaincy,
                };
            })
            .ToArray();

        return fixture with
        {
            Gameweek = snapshot.Gameweek,
            DeadlineUtc = snapshot.DeadlineUtc,
            GeneratedAtUtc = snapshot.DecisionCutoffUtc,
            Selection = fixture.Selection with { Players = players },
        };
    }

    private static GameweekAdviceDocument ApplyOfficialPreview(
        GameweekAdviceDocument fixture,
        OfficialDecisionRoomPreview preview)
    {
        AdvicePlayerDocument[] players = preview.Players
            .Select(player => new AdvicePlayerDocument(
                player.PlayerId,
                player.Name,
                player.ClubShortName,
                player.Position,
                player.LineupPlace,
                player.BenchOrder,
                player.Captaincy,
                player.Opponent,
                player.IsHome,
                player.ExpectedPoints,
                player.Lower80,
                player.Upper80,
                player.ExpectedMinutes,
                player.Reasons,
                player.Risks,
                player.PhotoUrl,
                player.DossierPath))
            .ToArray();
        decimal startingPoints = players
            .Where(player => player.LineupPlace == "starting")
            .Sum(player => player.ExpectedPoints);
        decimal captainPoints = players
            .Single(player => player.Captaincy == "captain")
            .ExpectedPoints;

        return fixture with
        {
            EvidenceStatus = "official-market-baseline-v0",
            IsSynthetic = false,
            SnapshotId = null,
            SnapshotRevision = null,
            SnapshotContentHash = null,
            DecisionCutoffUtc = preview.CaptureAvailableAtUtc,
            Gameweek = preview.Gameweek,
            DeadlineUtc = preview.DeadlineUtc,
            GeneratedAtUtc = preview.CaptureAvailableAtUtc,
            ModelLabel = "Baseline v0 · limited preseason evidence",
            RecommendationSummary =
                "A first evidence-based squad from official price, ownership, availability "
                + "and fixture context. Wide intervals reflect the missing current-season history.",
            Selection = fixture.Selection with
            {
                ExpectedPoints = startingPoints + captainPoints,
                Objective =
                    "A Baseline v0 squad prioritising expected points under the £100m budget, "
                    + "position, formation, club, bench and captaincy constraints.",
                Players = players,
            },
            Alternatives = [],
        };
    }

    private static AdvicePlayerDocument Player(
        int playerId,
        string name,
        string clubShortName,
        string position,
        string lineupPlace,
        int? benchOrder,
        string? captaincy,
        string opponent,
        bool isHome,
        decimal expectedPoints,
        decimal lower80,
        decimal upper80,
        int expectedMinutes,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> risks) =>
        new(
            playerId,
            name,
            clubShortName,
            position,
            lineupPlace,
            benchOrder,
            captaincy,
            opponent,
            isHome,
            expectedPoints,
            lower80,
            upper80,
            expectedMinutes,
            reasons,
            risks);
}
