using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Lineups;

public sealed record Lineup
{
    private Lineup(
        int goalkeeperCount,
        int defenderCount,
        int midfielderCount,
        int forwardCount,
        int captainPlayerId,
        int viceCaptainPlayerId)
    {
        GoalkeeperCount = goalkeeperCount;
        DefenderCount = defenderCount;
        MidfielderCount = midfielderCount;
        ForwardCount = forwardCount;
        CaptainPlayerId = captainPlayerId;
        ViceCaptainPlayerId = viceCaptainPlayerId;
    }

    public int PlayerCount => 11;

    public int GoalkeeperCount { get; }

    public int DefenderCount { get; }

    public int MidfielderCount { get; }

    public int ForwardCount { get; }

    public string Formation => $"{DefenderCount}-{MidfielderCount}-{ForwardCount}";

    public int CaptainPlayerId { get; }

    public int ViceCaptainPlayerId { get; }

    public static Lineup Create(
        Squad squad,
        IReadOnlyCollection<int> startingPlayerIds,
        int captainPlayerId,
        int viceCaptainPlayerId)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(startingPlayerIds);

        if (startingPlayerIds.Count != 11)
        {
            throw new LineupValidationException(
                "lineup.players.count",
                "startingPlayerIds");
        }
        if (startingPlayerIds.Distinct().Count() != startingPlayerIds.Count)
        {
            throw new LineupValidationException(
                "lineup.player.duplicate",
                "startingPlayerIds");
        }
        if (startingPlayerIds.Any(playerId => squad.Players.All(player => player.PlayerId != playerId)))
        {
            throw new LineupValidationException(
                "lineup.player.not_in_squad",
                "startingPlayerIds");
        }

        SquadPlayer[] starters = startingPlayerIds
            .Select(playerId => squad.Players.Single(player => player.PlayerId == playerId))
            .ToArray();

        int goalkeeperCount = starters.Count(player => player.Position == SquadPosition.Goalkeeper);
        int defenderCount = starters.Count(player => player.Position == SquadPosition.Defender);
        int midfielderCount = starters.Count(player => player.Position == SquadPosition.Midfielder);
        int forwardCount = starters.Count(player => player.Position == SquadPosition.Forward);
        if (goalkeeperCount != 1 || defenderCount < 3 || forwardCount < 1)
        {
            throw new LineupValidationException(
                "lineup.formation.invalid",
                "startingPlayerIds");
        }
        if (captainPlayerId == viceCaptainPlayerId)
        {
            throw new LineupValidationException(
                "lineup.captain.duplicate",
                "viceCaptainPlayerId");
        }
        if (!startingPlayerIds.Contains(captainPlayerId))
        {
            throw new LineupValidationException(
                "lineup.captain.not_in_starting",
                "captainPlayerId");
        }
        if (!startingPlayerIds.Contains(viceCaptainPlayerId))
        {
            throw new LineupValidationException(
                "lineup.vice_captain.not_in_starting",
                "viceCaptainPlayerId");
        }

        return new(
            goalkeeperCount,
            defenderCount,
            midfielderCount,
            forwardCount,
            captainPlayerId,
            viceCaptainPlayerId);
    }
}
