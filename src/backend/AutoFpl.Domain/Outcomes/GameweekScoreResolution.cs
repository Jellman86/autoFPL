using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Outcomes;

public sealed record GameweekScoreResolution
{
    private GameweekScoreResolution(
        GameweekOutcomeResolution outcome,
        IReadOnlyList<EffectivePlayerScore> effectivePlayerScores)
    {
        Outcome = outcome;
        EffectivePlayerScores = effectivePlayerScores;
        BasePoints = effectivePlayerScores.Sum(score => (long)score.Points);
        CaptainBonusPoints = effectivePlayerScores
            .Where(score => score.Multiplier == 2)
            .Sum(score => (long)score.Points);
        TotalPoints = effectivePlayerScores.Sum(score => score.CountedPoints);
    }

    public GameweekOutcomeResolution Outcome { get; }

    public IReadOnlyList<EffectivePlayerScore> EffectivePlayerScores { get; }

    public long BasePoints { get; }

    public long CaptainBonusPoints { get; }

    public long TotalPoints { get; }

    public static GameweekScoreResolution Resolve(
        Squad squad,
        GameweekSelection selection,
        IReadOnlyCollection<int> playerIdsWhoPlayed,
        IReadOnlyCollection<PlayerGameweekPoints> playerPoints)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(playerIdsWhoPlayed);
        ArgumentNullException.ThrowIfNull(playerPoints);

        int[] playerIdsWhoPlayedSnapshot = [.. playerIdsWhoPlayed];
        PlayerGameweekPoints[] playerPointsSnapshot = [.. playerPoints];
        if (playerPointsSnapshot.Select(entry => entry.PlayerId).Distinct().Count()
            != playerPointsSnapshot.Length)
        {
            throw new GameweekScoreResolutionValidationException(
                "outcome.player_points.duplicate",
                "playerPoints");
        }

        if (playerPointsSnapshot.Any(
            entry => squad.Players.All(player => player.PlayerId != entry.PlayerId)))
        {
            throw new GameweekScoreResolutionValidationException(
                "outcome.player_points.not_in_squad",
                "playerPoints");
        }

        if (squad.Players.Any(
            player => playerPointsSnapshot.All(entry => entry.PlayerId != player.PlayerId)))
        {
            throw new GameweekScoreResolutionValidationException(
                "outcome.player_points.missing",
                "playerPoints");
        }

        GameweekOutcomeResolution outcome = GameweekOutcomeResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayedSnapshot);
        HashSet<int> playedPlayerIds = [.. playerIdsWhoPlayedSnapshot];
        if (playerPointsSnapshot.Any(entry => entry.Points != 0 && !playedPlayerIds.Contains(entry.PlayerId)))
        {
            throw new GameweekScoreResolutionValidationException(
                "outcome.player_points.not_played",
                "playerPoints");
        }

        IReadOnlyDictionary<int, int> pointsByPlayerId = playerPointsSnapshot.ToDictionary(
            entry => entry.PlayerId,
            entry => entry.Points);
        EffectivePlayerScore[] effectivePlayerScores = outcome.EffectivePlayerIds
            .Select(playerId =>
            {
                int points = pointsByPlayerId[playerId];
                int multiplier = outcome.EffectiveCaptainPlayerId == playerId ? 2 : 1;
                return new EffectivePlayerScore(playerId, points, multiplier, points * (long)multiplier);
            })
            .ToArray();

        return new(outcome, Array.AsReadOnly(effectivePlayerScores));
    }
}
