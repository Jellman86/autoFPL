using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Selections;

public sealed class GameweekSelectionTests
{
    [Fact]
    public void CreateReturnsStartingSummaryAndOrderedBenchForValidSelection()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        GameweekSelection selection = GameweekSelection.Create(
            squad,
            StartingPlayerIds,
            captainPlayerId: 8,
            viceCaptainPlayerId: 13,
            replacementGoalkeeperPlayerId: 2,
            outfieldSubstitutePlayerIds: [6, 7, 15]);

        Assert.Equal("3-5-2", selection.Lineup.Formation);
        Assert.Equal(8, selection.Lineup.CaptainPlayerId);
        Assert.Equal(13, selection.Lineup.ViceCaptainPlayerId);
        Assert.Equal(2, selection.ReplacementGoalkeeperPlayerId);
        Assert.Equal([6, 7, 15], selection.OutfieldSubstitutePlayerIds);
    }

    [Fact]
    public void CreateRejectsOutfieldSubstituteCountOtherThanThree()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        GameweekSelectionValidationException exception =
            Assert.Throws<GameweekSelectionValidationException>(
                () => GameweekSelection.Create(
                    squad,
                    StartingPlayerIds,
                    captainPlayerId: 8,
                    viceCaptainPlayerId: 13,
                    replacementGoalkeeperPlayerId: 2,
                    outfieldSubstitutePlayerIds: [6, 7]));

        Assert.Equal("selection.outfield_substitutes.count", exception.Code);
        Assert.Equal("outfieldSubstitutePlayerIds", exception.Field);
    }

    [Theory]
    [InlineData(new[] { 6, 6, 15 })]
    [InlineData(new[] { 2, 6, 7 })]
    public void CreateRejectsDuplicateBenchPlayerIds(int[] outfieldSubstitutePlayerIds)
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        GameweekSelectionValidationException exception =
            Assert.Throws<GameweekSelectionValidationException>(
                () => GameweekSelection.Create(
                    squad,
                    StartingPlayerIds,
                    captainPlayerId: 8,
                    viceCaptainPlayerId: 13,
                    replacementGoalkeeperPlayerId: 2,
                    outfieldSubstitutePlayerIds));

        Assert.Equal("selection.substitute.duplicate", exception.Code);
        Assert.Equal("outfieldSubstitutePlayerIds", exception.Field);
    }

    [Theory]
    [InlineData(99, new[] { 6, 7, 15 }, "selection.replacement_goalkeeper.not_in_squad", "replacementGoalkeeperPlayerId")]
    [InlineData(2, new[] { 6, 7, 99 }, "selection.outfield_substitute.not_in_squad", "outfieldSubstitutePlayerIds")]
    public void CreateRejectsSubstituteOutsideSquad(
        int replacementGoalkeeperPlayerId,
        int[] outfieldSubstitutePlayerIds,
        string expectedCode,
        string expectedField)
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        GameweekSelectionValidationException exception =
            Assert.Throws<GameweekSelectionValidationException>(
                () => GameweekSelection.Create(
                    squad,
                    StartingPlayerIds,
                    captainPlayerId: 8,
                    viceCaptainPlayerId: 13,
                    replacementGoalkeeperPlayerId,
                    outfieldSubstitutePlayerIds));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedField, exception.Field);
    }

    [Theory]
    [InlineData(1, new[] { 6, 7, 15 }, "selection.replacement_goalkeeper.in_starting", "replacementGoalkeeperPlayerId")]
    [InlineData(2, new[] { 3, 6, 7 }, "selection.outfield_substitute.in_starting", "outfieldSubstitutePlayerIds")]
    public void CreateRejectsSubstituteInStartingEleven(
        int replacementGoalkeeperPlayerId,
        int[] outfieldSubstitutePlayerIds,
        string expectedCode,
        string expectedField)
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        GameweekSelectionValidationException exception =
            Assert.Throws<GameweekSelectionValidationException>(
                () => GameweekSelection.Create(
                    squad,
                    StartingPlayerIds,
                    captainPlayerId: 8,
                    viceCaptainPlayerId: 13,
                    replacementGoalkeeperPlayerId,
                    outfieldSubstitutePlayerIds));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedField, exception.Field);
    }

    [Fact]
    public void CreateRejectsReplacementGoalkeeperWithOutfieldPosition()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());

        GameweekSelectionValidationException exception =
            Assert.Throws<GameweekSelectionValidationException>(
                () => GameweekSelection.Create(
                    squad,
                    StartingPlayerIds,
                    captainPlayerId: 8,
                    viceCaptainPlayerId: 13,
                    replacementGoalkeeperPlayerId: 6,
                    outfieldSubstitutePlayerIds: [2, 7, 15]));

        Assert.Equal("selection.replacement_goalkeeper.invalid_position", exception.Code);
        Assert.Equal("replacementGoalkeeperPlayerId", exception.Field);
    }

    private static int[] StartingPlayerIds =>
        [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14];

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
