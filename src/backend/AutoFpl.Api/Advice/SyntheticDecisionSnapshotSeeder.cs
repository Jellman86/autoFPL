using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Advice;

namespace AutoFpl.Api.Advice;

public static class SyntheticDecisionSnapshotSeeder
{
    public const string SeasonCode = "demo-2026";
    public const int Gameweek = 1;

    public static async Task EnsureSeededAsync(
        DecisionSnapshotStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (await store.GetLatestSnapshotAsync(SeasonCode, Gameweek, cancellationToken) is not null)
        {
            return;
        }

        GameweekAdviceDocument fixture = DemoGameweekAdvice.Create();
        IReadOnlyDictionary<string, int> clubIds = new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            ["HBR"] = 1,
            ["NBR"] = 2,
            ["RIV"] = 3,
            ["CST"] = 4,
            ["WFD"] = 5,
            ["MTR"] = 6,
            ["EAS"] = 7,
            ["ALB"] = 8,
        };
        SnapshotPlayer[] players = fixture.Selection.Players
            .OrderBy(player => player.PlayerId)
            .Select(player => new SnapshotPlayer(
                player.PlayerId,
                player.Name,
                clubIds[player.ClubShortName],
                player.Position,
                PriceTenths(player.Position)))
            .ToArray();
        SnapshotObservation[] observations = fixture.Selection.Players
            .OrderBy(player => player.PlayerId)
            .Select(player => new SnapshotObservation(
                "repository-synthetic-fixture-v1",
                player.PlayerId,
                "expected-points",
                player.ExpectedPoints,
                fixture.GeneratedAtUtc,
                fixture.GeneratedAtUtc,
                fixture.GeneratedAtUtc,
                SupersedesObservationId: null))
            .ToArray();

        var command = new DecisionSnapshotCommand(
            SchemaVersion: "1.0",
            SeasonCode: SeasonCode,
            Gameweek: Gameweek,
            DeadlineUtc: fixture.DeadlineUtc,
            DecisionCutoffUtc: fixture.GeneratedAtUtc,
            BudgetTenths: 1_000,
            Players: players,
            StartingPlayerIds: fixture.Selection.Players
                .Where(player => player.LineupPlace == "starting")
                .Select(player => player.PlayerId)
                .ToArray(),
            CaptainPlayerId: fixture.Selection.Players
                .Single(player => player.Captaincy == "captain")
                .PlayerId,
            ViceCaptainPlayerId: fixture.Selection.Players
                .Single(player => player.Captaincy == "vice-captain")
                .PlayerId,
            ReplacementGoalkeeperPlayerId: fixture.Selection.Players
                .Single(player => player.BenchOrder == 1)
                .PlayerId,
            OutfieldSubstitutePlayerIds: fixture.Selection.Players
                .Where(player => player.BenchOrder is > 1)
                .OrderBy(player => player.BenchOrder)
                .Select(player => player.PlayerId)
                .ToArray(),
            Observations: observations,
            SupersedesSnapshotId: null);
        try
        {
            _ = await store.CreateSnapshotAsync(command, cancellationToken);
        }
        catch (DecisionSnapshotPersistenceException)
        {
            if (await store.GetLatestSnapshotAsync(
                SeasonCode,
                Gameweek,
                cancellationToken) is not null)
            {
                return;
            }

            throw;
        }
    }

    private static int PriceTenths(string position) =>
        position switch
        {
            "goalkeeper" or "defender" => 45,
            "midfielder" => 50,
            "forward" => 60,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };
}
