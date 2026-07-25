using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Lineups;

internal static class LineupFormation
{
    internal static bool IsValid(IReadOnlyCollection<SquadPlayer> players) =>
        players.Count == 11
        && players.Count(player => player.Position == SquadPosition.Goalkeeper) == 1
        && players.Count(player => player.Position == SquadPosition.Defender) >= 3
        && players.Count(player => player.Position == SquadPosition.Forward) >= 1;
}
