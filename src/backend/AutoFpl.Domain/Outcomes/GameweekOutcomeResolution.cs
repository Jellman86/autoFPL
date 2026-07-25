using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Outcomes;

public sealed record GameweekOutcomeResolution
{
    private GameweekOutcomeResolution(
        GameweekSubstitutionResolution substitutions,
        GameweekCaptaincyResolution captaincy)
    {
        OriginalStartingPlayerIds = substitutions.OriginalStartingPlayerIds;
        EffectivePlayerIds = substitutions.EffectivePlayerIds;
        ActivatedSubstitutePlayerIds = substitutions.ActivatedSubstitutePlayerIds;
        UnreplacedStartingPlayerIds = substitutions.UnreplacedStartingPlayerIds;
        OriginalCaptainPlayerId = captaincy.OriginalCaptainPlayerId;
        ViceCaptainPlayerId = captaincy.ViceCaptainPlayerId;
        EffectiveCaptainPlayerId = captaincy.EffectiveCaptainPlayerId;
        CaptaincyTransferred = captaincy.CaptaincyTransferred;
    }

    public IReadOnlyList<int> OriginalStartingPlayerIds { get; }

    public IReadOnlyList<int> EffectivePlayerIds { get; }

    public IReadOnlyList<int> ActivatedSubstitutePlayerIds { get; }

    public IReadOnlyList<int> UnreplacedStartingPlayerIds { get; }

    public int OriginalCaptainPlayerId { get; }

    public int ViceCaptainPlayerId { get; }

    public int? EffectiveCaptainPlayerId { get; }

    public bool CaptaincyTransferred { get; }

    public static GameweekOutcomeResolution Resolve(
        Squad squad,
        GameweekSelection selection,
        IReadOnlyCollection<int> playerIdsWhoPlayed)
    {
        ArgumentNullException.ThrowIfNull(playerIdsWhoPlayed);

        int[] playerIdsWhoPlayedSnapshot = [.. playerIdsWhoPlayed];
        GameweekSubstitutionResolution substitutions = GameweekSubstitutionResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayedSnapshot);
        GameweekCaptaincyResolution captaincy = GameweekCaptaincyResolution.Resolve(
            squad,
            selection,
            playerIdsWhoPlayedSnapshot);

        return new(substitutions, captaincy);
    }
}
