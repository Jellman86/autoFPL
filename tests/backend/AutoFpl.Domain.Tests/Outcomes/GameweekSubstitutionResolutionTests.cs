using AutoFpl.Domain.Outcomes;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Outcomes;

public sealed class GameweekSubstitutionResolutionTests
{
    [Fact]
    public void ResolveKeepsLineupWhenEveryStarterPlayed()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        int[] startingPlayerIds = [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14];

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: startingPlayerIds);

        Assert.Equal(startingPlayerIds, resolution.OriginalStartingPlayerIds);
        Assert.Equal(startingPlayerIds, resolution.EffectivePlayerIds);
        Assert.Empty(resolution.ActivatedSubstitutePlayerIds);
        Assert.Empty(resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveUsesReplacementGoalkeeperWhenStartingGoalkeeperDidNotPlay()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14]);

        Assert.Equal([2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Equal([2], resolution.ActivatedSubstitutePlayerIds);
        Assert.Empty(resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveLeavesStartingGoalkeeperUnreplacedWhenReplacementDidNotPlay()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [3, 4, 5, 8, 9, 10, 11, 12, 13, 14]);

        Assert.Equal([1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Empty(resolution.ActivatedSubstitutePlayerIds);
        Assert.Equal([1], resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveUsesFirstPlayedOutfieldSubstituteWhenFormationRemainsValid()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 3, 4, 5, 6, 9, 10, 11, 12, 13, 14]);

        Assert.Equal([1, 3, 4, 5, 6, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Equal([6], resolution.ActivatedSubstitutePlayerIds);
        Assert.Empty(resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveRejectsDuplicatePlayedPlayerIds()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolutionValidationException exception =
            Assert.Throws<GameweekSubstitutionResolutionValidationException>(
                () => GameweekSubstitutionResolution.Resolve(
                    squad,
                    selection,
                    playerIdsWhoPlayed: [1, 1]));

        Assert.Equal("outcome.played_player.duplicate", exception.Code);
        Assert.Equal("playerIdsWhoPlayed", exception.Field);
    }

    [Fact]
    public void ResolveRejectsPlayedPlayerOutsideSquad()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolutionValidationException exception =
            Assert.Throws<GameweekSubstitutionResolutionValidationException>(
                () => GameweekSubstitutionResolution.Resolve(
                    squad,
                    selection,
                    playerIdsWhoPlayed: [1, 99]));

        Assert.Equal("outcome.played_player.not_in_squad", exception.Code);
        Assert.Equal("playerIdsWhoPlayed", exception.Field);
    }

    [Fact]
    public void ResolveSkipsPlayedSubstituteThatWouldBreakFormation()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());
        GameweekSelection selection = CreateSelection(squad, [15, 6, 7]);

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15]);

        Assert.Equal([1, 6, 4, 5, 8, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Equal([6], resolution.ActivatedSubstitutePlayerIds);
        Assert.Empty(resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveUsesMaximumLegalSubstitutesInBenchPriorityOrder()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());
        GameweekSelection selection = CreateSelection(squad, [15, 6, 7]);

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 4, 5, 6, 9, 10, 11, 12, 13, 14, 15]);

        Assert.Equal([1, 6, 4, 5, 15, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Equal([15, 6], resolution.ActivatedSubstitutePlayerIds);
        Assert.Empty(resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveLeavesOutfieldStarterUnreplacedWhenNoSubstitutePlayed()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [1, 3, 4, 5, 9, 10, 11, 12, 13, 14]);

        Assert.Equal([1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Empty(resolution.ActivatedSubstitutePlayerIds);
        Assert.Equal([8], resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveCombinesUnreplacedGoalkeeperWithOutfieldSubstitution()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed: [3, 4, 5, 6, 9, 10, 11, 12, 13, 14]);

        Assert.Equal([1, 3, 4, 5, 6, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Equal([6], resolution.ActivatedSubstitutePlayerIds);
        Assert.Equal([1], resolution.UnreplacedStartingPlayerIds);
    }

    [Fact]
    public void ResolveSnapshotsPlayedEvidenceAndReturnedCollections()
    {
        (Squad squad, GameweekSelection selection) = ValidSelection();
        List<int> playerIdsWhoPlayed = [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14];

        GameweekSubstitutionResolution resolution = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayed);
        playerIdsWhoPlayed.Clear();

        Assert.Equal([1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14], resolution.EffectivePlayerIds);
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.OriginalStartingPlayerIds).Add(99));
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.EffectivePlayerIds).Add(99));
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.ActivatedSubstitutePlayerIds).Add(99));
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)resolution.UnreplacedStartingPlayerIds).Add(99));
    }

    private static (Squad Squad, GameweekSelection Selection) ValidSelection()
    {
        Squad squad = Squad.Create(1_000, ValidPlayers());
        return (squad, CreateSelection(squad, [6, 7, 15]));
    }

    private static GameweekSelection CreateSelection(
        Squad squad,
        IReadOnlyCollection<int> outfieldSubstitutePlayerIds) =>
        GameweekSelection.Create(
            squad,
            startingPlayerIds: [1, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14],
            captainPlayerId: 8,
            viceCaptainPlayerId: 13,
            replacementGoalkeeperPlayerId: 2,
            outfieldSubstitutePlayerIds: outfieldSubstitutePlayerIds);

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
