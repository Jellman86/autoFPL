using AutoFpl.Domain.Outcomes;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Outcomes;

public sealed class GameweekScoreResolutionTests
{
    [Fact]
    public void ResolveScoresEffectivePlayersAndCaptaincyFallbackFromManualPoints()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekScoreResolution resolution = GameweekScoreResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 2, 4, 5, 6, 9, 10, 11, 12, 13, 14],
            PlayerPoints(
                (1, 2), (2, 10), (3, 0), (4, 6), (5, 1),
                (6, 8), (7, 0), (8, 0), (9, 3), (10, -1),
                (11, 5), (12, 2), (13, 7), (14, 4), (15, 0)));

        Assert.Equal(
            [1, 6, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            resolution.Outcome.EffectivePlayerIds);
        Assert.Equal(13, resolution.Outcome.EffectiveCaptainPlayerId);
        Assert.Equal(37, resolution.BasePoints);
        Assert.Equal(7, resolution.CaptainBonusPoints);
        Assert.Equal(44, resolution.TotalPoints);
        Assert.Equal(
            [
                (1, 2, 1, 2L),
                (6, 8, 1, 8L),
                (4, 6, 1, 6L),
                (5, 1, 1, 1L),
                (8, 0, 1, 0L),
                (9, 3, 1, 3L),
                (10, -1, 1, -1L),
                (11, 5, 1, 5L),
                (12, 2, 1, 2L),
                (13, 7, 2, 14L),
                (14, 4, 1, 4L),
            ],
            resolution.EffectivePlayerScores.Select(
                score => (score.PlayerId, score.Points, score.Multiplier, score.CountedPoints)));
    }

    [Fact]
    public void ResolveRejectsMissingSquadPlayerPoints()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekScoreResolutionValidationException exception = Assert.Throws<GameweekScoreResolutionValidationException>(
            () => GameweekScoreResolution.Resolve(
                squad,
                selection,
                playerIdsWhoPlayed: [1],
                PlayerPoints(
                    (1, 0), (2, 0), (3, 0), (4, 0), (5, 0),
                    (6, 0), (7, 0), (8, 0), (9, 0), (10, 0),
                    (11, 0), (12, 0), (13, 0), (14, 0))));

        Assert.Equal("outcome.player_points.missing", exception.Code);
        Assert.Equal("playerPoints", exception.Field);
    }

    [Fact]
    public void ResolveRejectsDuplicatePlayerPoints()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        PlayerGameweekPoints[] points = CompleteZeroPoints();
        points[14] = new PlayerGameweekPoints(1, 0);

        GameweekScoreResolutionValidationException exception = Assert.Throws<GameweekScoreResolutionValidationException>(
            () => GameweekScoreResolution.Resolve(squad, selection, [1], points));

        Assert.Equal("outcome.player_points.duplicate", exception.Code);
        Assert.Equal("playerPoints", exception.Field);
    }

    [Fact]
    public void ResolveRejectsOutOfSquadPlayerPoints()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        PlayerGameweekPoints[] points = CompleteZeroPoints();
        points[14] = new PlayerGameweekPoints(16, 0);

        GameweekScoreResolutionValidationException exception = Assert.Throws<GameweekScoreResolutionValidationException>(
            () => GameweekScoreResolution.Resolve(squad, selection, [1], points));

        Assert.Equal("outcome.player_points.not_in_squad", exception.Code);
        Assert.Equal("playerPoints", exception.Field);
    }

    [Fact]
    public void ResolveRejectsNonZeroPointsForPlayerWhoDidNotPlay()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        PlayerGameweekPoints[] points = CompleteZeroPoints();
        points[1] = new PlayerGameweekPoints(2, 5);

        GameweekScoreResolutionValidationException exception = Assert.Throws<GameweekScoreResolutionValidationException>(
            () => GameweekScoreResolution.Resolve(squad, selection, [1], points));

        Assert.Equal("outcome.player_points.not_played", exception.Code);
        Assert.Equal("playerPoints", exception.Field);
    }

    [Fact]
    public void ResolveAddsNoCaptainBonusWhenNeitherCaptainPlayed()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        int[] playedPlayerIds = [1, 3, 4, 5, 9, 10, 11, 12, 14];
        PlayerGameweekPoints[] points = Enumerable.Range(1, 15)
            .Select(playerId => new PlayerGameweekPoints(playerId, playedPlayerIds.Contains(playerId) ? 1 : 0))
            .ToArray();

        GameweekScoreResolution resolution = GameweekScoreResolution.Resolve(
            squad,
            selection,
            playedPlayerIds,
            points);

        Assert.Null(resolution.Outcome.EffectiveCaptainPlayerId);
        Assert.Equal(9, resolution.BasePoints);
        Assert.Equal(0, resolution.CaptainBonusPoints);
        Assert.Equal(9, resolution.TotalPoints);
        Assert.All(resolution.EffectivePlayerScores, score => Assert.Equal(1, score.Multiplier));
    }

    [Fact]
    public void ResolveSnapshotsInputsAndExposesImmutableScoreCollection()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        List<int> playedPlayerIds = [1, 8];
        List<PlayerGameweekPoints> points = [.. CompleteZeroPoints()];
        points[7] = new PlayerGameweekPoints(8, 2);

        GameweekScoreResolution resolution = GameweekScoreResolution.Resolve(
            squad,
            selection,
            playedPlayerIds,
            points);
        playedPlayerIds.Clear();
        points.Clear();

        Assert.Equal(2, resolution.BasePoints);
        Assert.Equal(2, resolution.CaptainBonusPoints);
        Assert.Equal(4, resolution.TotalPoints);
        Assert.Throws<NotSupportedException>(
            () => ((IList<EffectivePlayerScore>)resolution.EffectivePlayerScores).Add(
                new EffectivePlayerScore(99, 1, 1, 1)));
    }

    [Fact]
    public void ResolveUsesWideTotalsForFullIntegerPointValues()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        PlayerGameweekPoints[] points = CompleteZeroPoints();
        points[7] = new PlayerGameweekPoints(8, int.MaxValue);

        GameweekScoreResolution resolution = GameweekScoreResolution.Resolve(squad, selection, [8], points);

        Assert.Equal((long)int.MaxValue, resolution.BasePoints);
        Assert.Equal((long)int.MaxValue, resolution.CaptainBonusPoints);
        Assert.Equal(2L * int.MaxValue, resolution.TotalPoints);
        Assert.Equal(
            2L * int.MaxValue,
            resolution.EffectivePlayerScores.Single(score => score.PlayerId == 8).CountedPoints);
    }

    private static PlayerGameweekPoints[] CompleteZeroPoints() =>
        Enumerable.Range(1, 15).Select(playerId => new PlayerGameweekPoints(playerId, 0)).ToArray();

    private static PlayerGameweekPoints[] PlayerPoints(params (int PlayerId, int Points)[] points) =>
        points.Select(point => new PlayerGameweekPoints(point.PlayerId, point.Points)).ToArray();

    private static (Squad Squad, GameweekSelection Selection) ValidSelection()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());
        GameweekSelection selection = GameweekSelection.Create(
            squad,
            startingPlayerIds: [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            captainPlayerId: 8,
            viceCaptainPlayerId: 13,
            replacementGoalkeeperPlayerId: 2,
            outfieldSubstitutePlayerIds: [6, 7, 15]);
        return (squad, selection);
    }

    private static SquadPlayer[] ValidPlayers() =>
    [
        SquadPlayer.Create(1, 1, "goalkeeper", 45),
        SquadPlayer.Create(2, 2, "goalkeeper", 45),
        SquadPlayer.Create(3, 1, "defender", 45),
        SquadPlayer.Create(4, 2, "defender", 45),
        SquadPlayer.Create(5, 3, "defender", 45),
        SquadPlayer.Create(6, 4, "defender", 45),
        SquadPlayer.Create(7, 5, "defender", 45),
        SquadPlayer.Create(8, 1, "midfielder", 50),
        SquadPlayer.Create(9, 2, "midfielder", 50),
        SquadPlayer.Create(10, 3, "midfielder", 50),
        SquadPlayer.Create(11, 4, "midfielder", 50),
        SquadPlayer.Create(12, 5, "midfielder", 50),
        SquadPlayer.Create(13, 3, "forward", 60),
        SquadPlayer.Create(14, 4, "forward", 60),
        SquadPlayer.Create(15, 5, "forward", 60),
    ];
}
