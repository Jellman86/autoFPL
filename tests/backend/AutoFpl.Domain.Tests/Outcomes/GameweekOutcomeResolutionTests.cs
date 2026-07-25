using AutoFpl.Domain.Outcomes;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Outcomes;

public sealed class GameweekOutcomeResolutionTests
{
    [Fact]
    public void ResolveComposesAutomaticSubstitutionsAndCaptaincyFallback()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekOutcomeResolution resolution = GameweekOutcomeResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 4, 5, 6, 9, 10, 11, 12, 13, 14]);

        Assert.Equal(
            [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            resolution.OriginalStartingPlayerIds);
        Assert.Equal(
            [1, 6, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            resolution.EffectivePlayerIds);
        Assert.Equal([6], resolution.ActivatedSubstitutePlayerIds);
        Assert.Equal([8], resolution.UnreplacedStartingPlayerIds);
        Assert.Equal(8, resolution.OriginalCaptainPlayerId);
        Assert.Equal(13, resolution.ViceCaptainPlayerId);
        Assert.Equal(13, resolution.EffectiveCaptainPlayerId);
        Assert.True(resolution.CaptaincyTransferred);
    }

    [Fact]
    public void ResolveSnapshotsEvidenceAndExposesImmutableCollections()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        List<int> playerIdsWhoPlayed = [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14];

        GameweekOutcomeResolution resolution = GameweekOutcomeResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed);
        playerIdsWhoPlayed.Clear();

        Assert.Equal(
            [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            resolution.EffectivePlayerIds);
        Assert.Equal(8, resolution.EffectiveCaptainPlayerId);
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.OriginalStartingPlayerIds).Add(99));
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.EffectivePlayerIds).Add(99));
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.ActivatedSubstitutePlayerIds).Add(99));
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.UnreplacedStartingPlayerIds).Add(99));
    }

    [Fact]
    public void ResolveReturnsNoEffectiveCaptainWhenNeitherCaptainPlayed()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekOutcomeResolution resolution = GameweekOutcomeResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 3, 4, 5, 9, 10, 11, 12, 14]);

        Assert.Null(resolution.EffectiveCaptainPlayerId);
        Assert.False(resolution.CaptaincyTransferred);
        Assert.Equal([8, 13], resolution.UnreplacedStartingPlayerIds);
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
