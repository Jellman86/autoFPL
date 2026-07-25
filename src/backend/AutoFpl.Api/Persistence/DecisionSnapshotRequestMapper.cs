using AutoFpl.Contracts.Snapshots;
using AutoFpl.Domain.Selections;
using AutoFpl.Domain.Squads;

namespace AutoFpl.Api.Persistence;

internal static class DecisionSnapshotRequestMapper
{
    public static bool TryMap(
        DecisionSnapshotPersistenceRequest request,
        out DecisionSnapshotCommand? command)
    {
        command = null;
        if (request.SchemaVersion is null
            || request.SeasonCode is null
            || request.Gameweek is null
            || request.DeadlineUtc is null
            || request.DecisionCutoffUtc is null
            || request.BudgetTenths is null
            || request.Players is null
            || request.StartingPlayerIds is null
            || request.CaptainPlayerId is null
            || request.ViceCaptainPlayerId is null
            || request.ReplacementGoalkeeperPlayerId is null
            || request.OutfieldSubstitutePlayerIds is null
            || request.Observations is null
            || request.SupersedesSnapshotId is <= 0
            || request.Players.Any(PlayerIsMalformed)
            || request.StartingPlayerIds.Any(playerId => playerId is null)
            || request.OutfieldSubstitutePlayerIds.Any(playerId => playerId is null)
            || request.Observations.Any(ObservationIsMalformed))
        {
            return false;
        }

        if (!StringComparer.Ordinal.Equals(request.SchemaVersion, "1.0"))
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.schema_version.unsupported",
                "schemaVersion");
        }

        if (request.SeasonCode.Length is < 4 or > 16)
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.season_code.invalid",
                "seasonCode");
        }

        if (request.Gameweek is < 1 or > 38)
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.gameweek.invalid",
                "gameweek");
        }

        if (request.DeadlineUtc.Value.Offset != TimeSpan.Zero
            || request.DecisionCutoffUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.timestamp.not_utc",
                "decisionCutoffUtc");
        }

        if (request.DecisionCutoffUtc > request.DeadlineUtc)
        {
            throw new DecisionSnapshotPersistenceException(
                "snapshot.cutoff.after_deadline",
                "decisionCutoffUtc");
        }

        SnapshotPlayer[] players = request.Players
            .Select(player => new SnapshotPlayer(
                player!.PlayerId!.Value,
                player.DisplayName!,
                player.ClubId!.Value,
                player.Position!,
                player.PriceTenths!.Value))
            .ToArray();

        SquadPlayer[] domainPlayers = players
            .Select(player => SquadPlayer.Create(
                player.PlayerId,
                player.ClubId,
                player.Position,
                player.PriceTenths))
            .ToArray();
        Squad squad = Squad.Create(request.BudgetTenths.Value, domainPlayers);

        int[] startingPlayerIds = request.StartingPlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        int[] outfieldSubstitutePlayerIds = request.OutfieldSubstitutePlayerIds
            .Select(playerId => playerId!.Value)
            .ToArray();
        _ = GameweekSelection.Create(
            squad,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            outfieldSubstitutePlayerIds);

        var playerIds = players.Select(player => player.PlayerId).ToHashSet();
        var observations = new SnapshotObservation[request.Observations.Count];
        for (int index = 0; index < request.Observations.Count; index++)
        {
            SourceObservationRequest observation = request.Observations[index]!;
            if (!playerIds.Contains(observation.PlayerId!.Value))
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.player.not_in_squad",
                    "playerId");
            }

            if (observation.SourceKey!.Length > 100
                || observation.Metric!.Length > 64)
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.identity.invalid",
                    "sourceKey");
            }

            if (observation.ObservedAtUtc!.Value.Offset != TimeSpan.Zero
                || observation.RetrievedAtUtc!.Value.Offset != TimeSpan.Zero
                || observation.AvailableAtUtc!.Value.Offset != TimeSpan.Zero)
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.timestamp.not_utc",
                    "availableAtUtc");
            }

            if (observation.ObservedAtUtc > observation.RetrievedAtUtc
                || observation.RetrievedAtUtc > observation.AvailableAtUtc)
            {
                throw new DecisionSnapshotPersistenceException(
                    "observation.timeline.invalid",
                    "availableAtUtc");
            }

            observations[index] = new(
                observation.SourceKey,
                observation.PlayerId.Value,
                observation.Metric,
                observation.Value!.Value,
                observation.ObservedAtUtc.Value,
                observation.RetrievedAtUtc.Value,
                observation.AvailableAtUtc.Value,
                observation.SupersedesObservationId);
        }

        command = new(
            request.SchemaVersion,
            request.SeasonCode,
            request.Gameweek.Value,
            request.DeadlineUtc.Value,
            request.DecisionCutoffUtc.Value,
            request.BudgetTenths.Value,
            players,
            startingPlayerIds,
            request.CaptainPlayerId.Value,
            request.ViceCaptainPlayerId.Value,
            request.ReplacementGoalkeeperPlayerId.Value,
            outfieldSubstitutePlayerIds,
            observations,
            request.SupersedesSnapshotId);
        return true;
    }

    private static bool PlayerIsMalformed(DecisionSnapshotPlayerRequest? player) =>
        player is null
        || player.PlayerId is null
        || player.DisplayName is null
        || string.IsNullOrWhiteSpace(player.DisplayName)
        || player.DisplayName.Length > 100
        || player.ClubId is null
        || player.Position is null
        || player.PriceTenths is null;

    private static bool ObservationIsMalformed(SourceObservationRequest? observation) =>
        observation is null
        || observation.SourceKey is null
        || string.IsNullOrWhiteSpace(observation.SourceKey)
        || observation.PlayerId is null
        || observation.Metric is null
        || string.IsNullOrWhiteSpace(observation.Metric)
        || observation.Value is null
        || observation.ObservedAtUtc is null
        || observation.RetrievedAtUtc is null
        || observation.AvailableAtUtc is null
        || observation.SupersedesObservationId is <= 0;
}
