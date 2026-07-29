using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Selections;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Selections;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class InitialSquadQualityShadowStoreTests
{
    [Fact]
    public void Import_boundary_recomputes_every_scenario_score()
    {
        string[] positions =
        [
            "goalkeeper", "goalkeeper",
            "defender", "defender", "defender", "defender", "defender",
            "midfielder", "midfielder", "midfielder", "midfielder",
            "midfielder",
            "forward", "forward", "forward",
        ];
        int[] playerIds = [.. Enumerable.Range(1, 15)];
        int[] starting = [1, 3, 4, 5, 8, 9, 10, 11, 13, 14, 15];
        var selection = new SelectionScenarioDefinitionDocument(
            playerIds,
            positions,
            starting,
            2,
            [6, 12, 7],
            13,
            9);
        var result = new SelectionScenarioResultDocument(
            selection,
            new(
                "1.0",
                SelectionScenarioScoreShadowStore.EngineVersion,
                2,
                18m,
                6m,
                12,
                13.2m,
                18m,
                22.8m,
                24,
                0m,
                0m),
            [12, 24],
            [1, 2]);
        JointScenarioShadowDocument scenario = CreateScenario(
            playerIds,
            positions);
        IReadOnlyDictionary<
            int,
            InitialSquadQualityShadowStore.OfficialPlayerState> official =
            playerIds.ToDictionary(
                playerId => playerId,
                playerId =>
                    new InitialSquadQualityShadowStore.OfficialPlayerState(
                        ((playerId - 1) / 3) + 1,
                        positions[playerId - 1],
                        50,
                        "a",
                        null));

        InitialSquadQualityShadowStore.ValidateScenarioResult(
            result,
            scenario,
            official,
            "candidate");

        InitialSquadQualityValidationException exception =
            Assert.Throws<InitialSquadQualityValidationException>(
                () =>
                    InitialSquadQualityShadowStore.ValidateScenarioResult(
                        result with
                        {
                            TotalPointRows = [11, 24],
                        },
                        scenario,
                        official,
                        "candidate"));
        Assert.Equal("scenario-score", exception.Code);
        Assert.Equal("candidate.totalPointRows", exception.Field);
    }

    private static JointScenarioShadowDocument CreateScenario(
        IReadOnlyList<int> playerIds,
        IReadOnlyList<string> positions)
    {
        JointScenarioPlayerDocument[] players =
        [
            .. playerIds.Select(
                (playerId, index) =>
                    new JointScenarioPlayerDocument(
                        index,
                        playerId,
                        10_000 + playerId,
                        $"Player {playerId}",
                        ((playerId - 1) / 3) + 1,
                        $"Team {((playerId - 1) / 3) + 1}",
                        positions[index],
                        1.5m,
                        1.5m,
                        1m,
                        1m,
                        "a",
                        null,
                        "both-historical-seasons",
                        "stable-code-match")),
        ];
        IReadOnlyList<IReadOnlyList<int>> points =
        [
            playerIds.Select(_ => 1).ToArray(),
            playerIds.Select(_ => 2).ToArray(),
        ];
        IReadOnlyList<IReadOnlyList<bool>> played =
        [
            playerIds.Select(_ => true).ToArray(),
            playerIds.Select(_ => true).ToArray(),
        ];
        return new(
            "1.0",
            JointScenarioShadowStore.ArtifactType,
            JointScenarioShadowStore.ArtifactVersion,
            JointScenarioShadowStore.Status,
            false,
            false,
            "2026-27",
            1,
            DateTimeOffset.Parse("2026-08-21T17:30:00Z"),
            DateTimeOffset.Parse("2026-07-29T04:38:41Z"),
            16,
            JointScenarioShadowStore.ScenarioModelKey,
            JointScenarioShadowStore.AppearanceVariant,
            JointScenarioShadowStore.PointAvailabilityFusion,
            2,
            15,
            [1, 2],
            players,
            points,
            played,
            new string('a', 64),
            new(0m, 0m, 0m, 0m, 0),
            new(
                "2025-26",
                1,
                new string('b', 64),
                new string('c', 64),
                new string('d', 64),
                new string('e', 64),
                new(
                    JointScenarioShadowStore.EvaluatorVersion,
                    JointScenarioShadowStore.EvaluationDataIdentity,
                    JointScenarioShadowStore.EvaluationRunIdentity,
                    "passes-retrospective-screen",
                    0.6m,
                    0.1m,
                    8,
                    8,
                    false),
                0,
                0),
            JointScenarioShadowStore.DistributionStatus,
            ["Research only."],
            new string('f', 64),
            new string('0', 64));
    }
}
