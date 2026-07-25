using AutoFpl.Domain.Lineups;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Lineups;

public sealed class LineupTests
{
    [Fact]
    public void CreateReturnsFormationAndCaptaincyForValidStartingEleven()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        Lineup lineup = Lineup.Create(
            squad,
            [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            captainPlayerId: 8,
            viceCaptainPlayerId: 13);

        Assert.Equal(11, lineup.PlayerCount);
        Assert.Equal(1, lineup.GoalkeeperCount);
        Assert.Equal(3, lineup.DefenderCount);
        Assert.Equal(5, lineup.MidfielderCount);
        Assert.Equal(2, lineup.ForwardCount);
        Assert.Equal("3-5-2", lineup.Formation);
        Assert.Equal(8, lineup.CaptainPlayerId);
        Assert.Equal(13, lineup.ViceCaptainPlayerId);
    }

    [Fact]
    public void CreateRejectsStartingPlayerCountOtherThanEleven()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        LineupValidationException exception = Assert.Throws<LineupValidationException>(
            () => Lineup.Create(
                squad,
                [1, 3, 4, 5, 8, 9, 10, 11, 12, 13],
                captainPlayerId: 8,
                viceCaptainPlayerId: 13));

        Assert.Equal("lineup.players.count", exception.Code);
        Assert.Equal("startingPlayerIds", exception.Field);
    }

    [Fact]
    public void CreateRejectsDuplicateStartingPlayerIds()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        LineupValidationException exception = Assert.Throws<LineupValidationException>(
            () => Lineup.Create(
                squad,
                [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 13],
                captainPlayerId: 8,
                viceCaptainPlayerId: 13));

        Assert.Equal("lineup.player.duplicate", exception.Code);
        Assert.Equal("startingPlayerIds", exception.Field);
    }

    [Fact]
    public void CreateRejectsStartingPlayerOutsideSquad()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        LineupValidationException exception = Assert.Throws<LineupValidationException>(
            () => Lineup.Create(
                squad,
                [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 99],
                captainPlayerId: 8,
                viceCaptainPlayerId: 13));

        Assert.Equal("lineup.player.not_in_squad", exception.Code);
        Assert.Equal("startingPlayerIds", exception.Field);
    }

    public static TheoryData<int[]> InvalidFormations => new()
    {
        { [1, 2, 3, 4, 5, 8, 9, 10, 11, 13, 14] },
        { [1, 3, 4, 8, 9, 10, 11, 12, 13, 14, 15] },
        { [1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12] },
    };

    [Theory]
    [MemberData(nameof(InvalidFormations))]
    public void CreateRejectsInvalidStartingFormation(int[] startingPlayerIds)
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        LineupValidationException exception = Assert.Throws<LineupValidationException>(
            () => Lineup.Create(
                squad,
                startingPlayerIds,
                captainPlayerId: 8,
                viceCaptainPlayerId: 9));

        Assert.Equal("lineup.formation.invalid", exception.Code);
        Assert.Equal("startingPlayerIds", exception.Field);
    }

    [Theory]
    [InlineData(8, 8, "lineup.captain.duplicate", "viceCaptainPlayerId")]
    [InlineData(2, 13, "lineup.captain.not_in_starting", "captainPlayerId")]
    [InlineData(8, 15, "lineup.vice_captain.not_in_starting", "viceCaptainPlayerId")]
    public void CreateRejectsInvalidCaptaincy(
        int captainPlayerId,
        int viceCaptainPlayerId,
        string expectedCode,
        string expectedField)
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        LineupValidationException exception = Assert.Throws<LineupValidationException>(
            () => Lineup.Create(
                squad,
                [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
                captainPlayerId,
                viceCaptainPlayerId));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedField, exception.Field);
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
