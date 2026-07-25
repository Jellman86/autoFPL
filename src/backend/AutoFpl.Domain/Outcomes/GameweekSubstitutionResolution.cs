using AutoFpl.Domain.Lineups;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Outcomes;

public sealed record GameweekSubstitutionResolution
{
    private GameweekSubstitutionResolution(
        IReadOnlyList<int> originalStartingPlayerIds,
        IReadOnlyList<int> effectivePlayerIds,
        IReadOnlyList<int> activatedSubstitutePlayerIds,
        IReadOnlyList<int> unreplacedStartingPlayerIds)
    {
        OriginalStartingPlayerIds = originalStartingPlayerIds;
        EffectivePlayerIds = effectivePlayerIds;
        ActivatedSubstitutePlayerIds = activatedSubstitutePlayerIds;
        UnreplacedStartingPlayerIds = unreplacedStartingPlayerIds;
    }

    public IReadOnlyList<int> OriginalStartingPlayerIds { get; }

    public IReadOnlyList<int> EffectivePlayerIds { get; }

    public IReadOnlyList<int> ActivatedSubstitutePlayerIds { get; }

    public IReadOnlyList<int> UnreplacedStartingPlayerIds { get; }

    public static GameweekSubstitutionResolution Resolve(
        Squad squad,
        GameweekSelection selection,
        IReadOnlyCollection<int> playerIdsWhoPlayed)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(playerIdsWhoPlayed);

        int[] playerIdsWhoPlayedSnapshot = [.. playerIdsWhoPlayed];
        if (playerIdsWhoPlayedSnapshot.Distinct().Count() != playerIdsWhoPlayedSnapshot.Length)
        {
            throw new GameweekSubstitutionResolutionValidationException(
                "outcome.played_player.duplicate",
                "playerIdsWhoPlayed");
        }
        if (playerIdsWhoPlayedSnapshot.Any(
            playerId => squad.Players.All(player => player.PlayerId != playerId)))
        {
            throw new GameweekSubstitutionResolutionValidationException(
                "outcome.played_player.not_in_squad",
                "playerIdsWhoPlayed");
        }

        HashSet<int> playedPlayerIds = [.. playerIdsWhoPlayedSnapshot];
        int[] originalStartingPlayerIds = [.. selection.Lineup.StartingPlayerIds];
        int[] effectivePlayerIds = [.. originalStartingPlayerIds];
        List<int> activatedSubstitutePlayerIds = [];
        List<int> unreplacedStartingPlayerIds = [];
        int startingGoalkeeperPlayerId = originalStartingPlayerIds.Single(
            playerId => squad.Players.Single(player => player.PlayerId == playerId).Position
                == SquadPosition.Goalkeeper);
        if (!playedPlayerIds.Contains(startingGoalkeeperPlayerId))
        {
            if (playedPlayerIds.Contains(selection.ReplacementGoalkeeperPlayerId))
            {
                effectivePlayerIds[Array.IndexOf(effectivePlayerIds, startingGoalkeeperPlayerId)] =
                    selection.ReplacementGoalkeeperPlayerId;
                activatedSubstitutePlayerIds.Add(selection.ReplacementGoalkeeperPlayerId);
            }
            else
            {
                unreplacedStartingPlayerIds.Add(startingGoalkeeperPlayerId);
            }
        }

        int[] missingOutfieldStarterPlayerIds = originalStartingPlayerIds
            .Where(playerId => playerId != startingGoalkeeperPlayerId)
            .Where(playerId => !playedPlayerIds.Contains(playerId))
            .ToArray();
        HashSet<int> replacedStartingPlayerIds = [];
        foreach (int substitutePlayerId in selection.OutfieldSubstitutePlayerIds)
        {
            if (!playedPlayerIds.Contains(substitutePlayerId))
            {
                continue;
            }

            foreach (int missingStarterPlayerId in missingOutfieldStarterPlayerIds)
            {
                if (replacedStartingPlayerIds.Contains(missingStarterPlayerId))
                {
                    continue;
                }

                int missingStarterIndex = Array.IndexOf(effectivePlayerIds, missingStarterPlayerId);
                int[] candidatePlayerIds = [.. effectivePlayerIds];
                candidatePlayerIds[missingStarterIndex] = substitutePlayerId;
                SquadPlayer[] candidatePlayers = candidatePlayerIds
                    .Select(playerId => squad.Players.Single(player => player.PlayerId == playerId))
                    .ToArray();
                if (!LineupFormation.IsValid(candidatePlayers))
                {
                    continue;
                }

                effectivePlayerIds = candidatePlayerIds;
                activatedSubstitutePlayerIds.Add(substitutePlayerId);
                replacedStartingPlayerIds.Add(missingStarterPlayerId);
                break;
            }
        }

        unreplacedStartingPlayerIds.AddRange(
            missingOutfieldStarterPlayerIds.Where(playerId => !replacedStartingPlayerIds.Contains(playerId)));

        return new(
            Array.AsReadOnly(originalStartingPlayerIds),
            Array.AsReadOnly(effectivePlayerIds),
            Array.AsReadOnly(activatedSubstitutePlayerIds.ToArray()),
            Array.AsReadOnly(unreplacedStartingPlayerIds.ToArray()));
    }
}
