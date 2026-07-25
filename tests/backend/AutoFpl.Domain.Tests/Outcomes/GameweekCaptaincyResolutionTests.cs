using AutoFpl.Domain.Outcomes;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Outcomes;

public sealed class GameweekCaptaincyResolutionTests
{
    [Fact]
    public void ResolveKeepsCaptainWhenCaptainPlayedMinutes()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekCaptaincyResolution resolution = GameweekCaptaincyResolution.Resolve(
            squad,
            selection,
            playerIdsWithMinutes: [8, 13]);

        Assert.Equal(8, resolution.OriginalCaptainPlayerId);
        Assert.Equal(13, resolution.ViceCaptainPlayerId);
        Assert.Equal(8, resolution.EffectiveCaptainPlayerId);
        Assert.False(resolution.CaptaincyTransferred);
    }

    [Fact]
    public void ResolvePromotesViceCaptainWhenCaptainPlayedNoMinutes()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekCaptaincyResolution resolution = GameweekCaptaincyResolution.Resolve(
            squad,
            selection,
            playerIdsWithMinutes: [13]);

        Assert.Equal(13, resolution.EffectiveCaptainPlayerId);
        Assert.True(resolution.CaptaincyTransferred);
    }

    [Fact]
    public void ResolveReturnsNoEffectiveCaptainWhenNeitherPlayedMinutes()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekCaptaincyResolution resolution = GameweekCaptaincyResolution.Resolve(
            squad,
            selection,
            playerIdsWithMinutes: [1, 3, 4]);

        Assert.Null(resolution.EffectiveCaptainPlayerId);
        Assert.False(resolution.CaptaincyTransferred);
    }

    [Fact]
    public void ResolveRejectsDuplicatePlayerIdsWithMinutes()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekCaptaincyResolutionValidationException exception =
            Assert.Throws<GameweekCaptaincyResolutionValidationException>(
                () => GameweekCaptaincyResolution.Resolve(
                    squad,
                    selection,
                    playerIdsWithMinutes: [8, 8]));

        Assert.Equal("outcome.minutes_player.duplicate", exception.Code);
        Assert.Equal("playerIdsWithMinutes", exception.Field);
    }

    [Fact]
    public void ResolveRejectsPlayerWithMinutesOutsideSquad()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekCaptaincyResolutionValidationException exception =
            Assert.Throws<GameweekCaptaincyResolutionValidationException>(
                () => GameweekCaptaincyResolution.Resolve(
                    squad,
                    selection,
                    playerIdsWithMinutes: [8, 99]));

        Assert.Equal("outcome.minutes_player.not_in_squad", exception.Code);
        Assert.Equal("playerIdsWithMinutes", exception.Field);
    }

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
