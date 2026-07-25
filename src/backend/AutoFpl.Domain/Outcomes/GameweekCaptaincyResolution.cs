using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Outcomes;

public sealed record GameweekCaptaincyResolution
{
    private GameweekCaptaincyResolution(
        int originalCaptainPlayerId,
        int viceCaptainPlayerId,
        int? effectiveCaptainPlayerId)
    {
        OriginalCaptainPlayerId = originalCaptainPlayerId;
        ViceCaptainPlayerId = viceCaptainPlayerId;
        EffectiveCaptainPlayerId = effectiveCaptainPlayerId;
    }

    public int OriginalCaptainPlayerId { get; }

    public int ViceCaptainPlayerId { get; }

    public int? EffectiveCaptainPlayerId { get; }

    public bool CaptaincyTransferred =>
        EffectiveCaptainPlayerId == ViceCaptainPlayerId;

    public static GameweekCaptaincyResolution Resolve(
        Squad squad,
        GameweekSelection selection,
        IReadOnlyCollection<int> playerIdsWithMinutes)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(playerIdsWithMinutes);

        int[] playerIdsWithMinutesSnapshot = [.. playerIdsWithMinutes];
        if (playerIdsWithMinutesSnapshot.Distinct().Count() != playerIdsWithMinutesSnapshot.Length)
        {
            throw new GameweekCaptaincyResolutionValidationException(
                "outcome.minutes_player.duplicate",
                "playerIdsWithMinutes");
        }

        if (playerIdsWithMinutesSnapshot.Any(
            playerId => squad.Players.All(player => player.PlayerId != playerId)))
        {
            throw new GameweekCaptaincyResolutionValidationException(
                "outcome.minutes_player.not_in_squad",
                "playerIdsWithMinutes");
        }

        int captainPlayerId = selection.Lineup.CaptainPlayerId;
        int viceCaptainPlayerId = selection.Lineup.ViceCaptainPlayerId;
        int? effectiveCaptainPlayerId = playerIdsWithMinutesSnapshot.Contains(captainPlayerId)
            ? captainPlayerId
            : playerIdsWithMinutesSnapshot.Contains(viceCaptainPlayerId)
                ? viceCaptainPlayerId
                : null;

        return new(
            captainPlayerId,
            viceCaptainPlayerId,
            effectiveCaptainPlayerId);
    }
}
