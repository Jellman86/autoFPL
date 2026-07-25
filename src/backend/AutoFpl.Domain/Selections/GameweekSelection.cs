using AutoFpl.Domain.Lineups;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Selections;

public sealed record GameweekSelection
{
    private GameweekSelection(
        Lineup lineup,
        int replacementGoalkeeperPlayerId,
        IReadOnlyList<int> outfieldSubstitutePlayerIds)
    {
        Lineup = lineup;
        ReplacementGoalkeeperPlayerId = replacementGoalkeeperPlayerId;
        OutfieldSubstitutePlayerIds = outfieldSubstitutePlayerIds;
    }

    public Lineup Lineup { get; }

    public int ReplacementGoalkeeperPlayerId { get; }

    public IReadOnlyList<int> OutfieldSubstitutePlayerIds { get; }

    public static GameweekSelection Create(
        Squad squad,
        IReadOnlyCollection<int> startingPlayerIds,
        int captainPlayerId,
        int viceCaptainPlayerId,
        int replacementGoalkeeperPlayerId,
        IReadOnlyCollection<int> outfieldSubstitutePlayerIds)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(startingPlayerIds);
        ArgumentNullException.ThrowIfNull(outfieldSubstitutePlayerIds);

        int[] startingPlayerSnapshot = [.. startingPlayerIds];
        Lineup lineup = Lineup.Create(
            squad,
            startingPlayerSnapshot,
            captainPlayerId,
            viceCaptainPlayerId);

        int[] outfieldSubstituteSnapshot = [.. outfieldSubstitutePlayerIds];
        if (outfieldSubstituteSnapshot.Length != 3)
        {
            throw new GameweekSelectionValidationException(
                "selection.outfield_substitutes.count",
                "outfieldSubstitutePlayerIds");
        }
        if (outfieldSubstituteSnapshot.Distinct().Count() != outfieldSubstituteSnapshot.Length
            || outfieldSubstituteSnapshot.Contains(replacementGoalkeeperPlayerId))
        {
            throw new GameweekSelectionValidationException(
                "selection.substitute.duplicate",
                "outfieldSubstitutePlayerIds");
        }
        if (squad.Players.All(player => player.PlayerId != replacementGoalkeeperPlayerId))
        {
            throw new GameweekSelectionValidationException(
                "selection.replacement_goalkeeper.not_in_squad",
                "replacementGoalkeeperPlayerId");
        }
        if (outfieldSubstituteSnapshot.Any(
            playerId => squad.Players.All(player => player.PlayerId != playerId)))
        {
            throw new GameweekSelectionValidationException(
                "selection.outfield_substitute.not_in_squad",
                "outfieldSubstitutePlayerIds");
        }
        if (startingPlayerSnapshot.Contains(replacementGoalkeeperPlayerId))
        {
            throw new GameweekSelectionValidationException(
                "selection.replacement_goalkeeper.in_starting",
                "replacementGoalkeeperPlayerId");
        }
        if (outfieldSubstituteSnapshot.Any(startingPlayerSnapshot.Contains))
        {
            throw new GameweekSelectionValidationException(
                "selection.outfield_substitute.in_starting",
                "outfieldSubstitutePlayerIds");
        }
        SquadPlayer replacementGoalkeeper = squad.Players.Single(
            player => player.PlayerId == replacementGoalkeeperPlayerId);
        if (replacementGoalkeeper.Position != SquadPosition.Goalkeeper)
        {
            throw new GameweekSelectionValidationException(
                "selection.replacement_goalkeeper.invalid_position",
                "replacementGoalkeeperPlayerId");
        }

        return new(
            lineup,
            replacementGoalkeeperPlayerId,
            Array.AsReadOnly(outfieldSubstituteSnapshot));
    }
}
