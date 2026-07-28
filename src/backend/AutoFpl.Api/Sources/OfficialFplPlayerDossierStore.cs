using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Forecasts;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplPlayerDossierStore
{
    public const string PhotoBaseUrl =
        "https://resources.premierleague.com/premierleague/photos/players/110x140/";

    private const int RecentOutcomeLimit = 5;
    private const int UpcomingGameweekWindow = 6;

    private readonly DatabaseOptions _options;
    private readonly OfficialFplCaptureStore _captureStore;

    public OfficialFplPlayerDossierStore(
        DatabaseOptions options,
        OfficialFplCaptureStore captureStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _captureStore = captureStore ?? throw new ArgumentNullException(nameof(captureStore));
    }

    public async Task<OfficialFplPlayerDossierDocument?> GetAsync(
        string seasonCode,
        int targetGameweek,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        OfficialFplReplayDocument? replay =
            await _captureStore.GetLatestPreDeadlineReplayAsync(
                seasonCode,
                targetGameweek,
                cancellationToken);
        if (replay is null)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        PlayerIdentityRow? identity = await ReadIdentityAsync(
            connection,
            replay.SelectedCaptureId,
            playerId,
            cancellationToken);
        if (identity is null)
        {
            return null;
        }

        IReadOnlyList<OutcomeRow> outcomeRows = await ReadOutcomeRowsAsync(
            connection,
            seasonCode,
            targetGameweek,
            replay.DeadlineUtc,
            identity.PlayerCode,
            cancellationToken);
        var outcomes = new List<OfficialFplPlayerOutcomeDocument>(outcomeRows.Count);
        foreach (OutcomeRow outcome in outcomeRows.OrderBy(item => item.Gameweek))
        {
            IReadOnlyList<OfficialFplPlayerFixtureDocument> fixtures =
                await ReadFixturesAsync(
                    connection,
                    outcome.ReferenceCaptureId,
                    outcome.TeamId,
                    outcome.Gameweek,
                    outcome.Gameweek,
                    cancellationToken);
            outcomes.Add(
                new(
                    outcome.Gameweek,
                    outcome.OutcomeCaptureId,
                    outcome.AvailableAtUtc,
                    fixtures.Count > 1,
                    fixtures,
                    outcome.Minutes,
                    outcome.Starts,
                    outcome.TotalPoints,
                    outcome.GoalsScored,
                    outcome.Assists,
                    outcome.CleanSheets,
                    outcome.GoalsConceded,
                    outcome.Saves,
                    outcome.Bonus,
                    outcome.YellowCards,
                    outcome.RedCards,
                    outcome.OwnGoals,
                    outcome.PenaltiesSaved,
                    outcome.PenaltiesMissed,
                    outcome.Bps,
                    outcome.Influence,
                    outcome.Creativity,
                    outcome.Threat,
                    outcome.IctIndex,
                    outcome.ClearancesBlocksInterceptions,
                    outcome.Recoveries,
                    outcome.Tackles,
                    outcome.DefensiveContribution,
                    outcome.ExpectedGoals,
                    outcome.ExpectedAssists,
                    outcome.ExpectedGoalInvolvements,
                    outcome.ExpectedGoalsConceded));
        }

        IReadOnlyList<OfficialFplPlayerFixtureDocument> upcoming =
            await ReadFixturesAsync(
                connection,
                replay.SelectedCaptureId,
                identity.TeamId,
                targetGameweek,
                Math.Min(38, targetGameweek + UpcomingGameweekWindow - 1),
                cancellationToken);
        IReadOnlyList<EvidenceRow> evidenceRows = await ReadEvidenceRowsAsync(
            connection,
            seasonCode,
            targetGameweek,
            replay.DeadlineUtc,
            identity.PlayerCode,
            cancellationToken);
        OfficialFplPlayerResearchEvidenceDocument researchEvidence =
            CreateResearchEvidence(evidenceRows, replay.DeadlineUtc);
        OfficialFplPreseasonChallengerDocument? preseasonChallenger =
            await ReadPreseasonChallengerAsync(
                connection,
                replay.SelectedCaptureId,
                identity.PlayerId,
                cancellationToken);
        OfficialFplMultiSeasonShadowDocument? multiSeasonShadow =
            await ReadMultiSeasonShadowAsync(
                connection,
                replay.SelectedCaptureId,
                identity.PlayerId,
                cancellationToken);

        return new(
            "1.4",
            seasonCode,
            targetGameweek,
            replay.DeadlineUtc,
            replay.SelectedCaptureId,
            replay.CaptureAvailableAtUtc,
            new(
                identity.PlayerId,
                identity.PlayerCode,
                $"{identity.FirstName} {identity.SecondName}",
                identity.WebName,
                identity.TeamId,
                identity.TeamName,
                identity.TeamShortName,
                identity.Position,
                identity.PriceTenths,
                identity.Status,
                identity.News,
                identity.PhotoIdentifier,
                CreatePhotoUrl(identity.PhotoIdentifier)),
            identity.ExpectedPointsNext is decimal expectedPointsNext
                ? new(
                    OfficialFplImporter.SourceKey,
                    targetGameweek,
                    expectedPointsNext,
                    "published-challenger-not-promoted")
                : null,
            preseasonChallenger,
            multiSeasonShadow,
            outcomes,
            upcoming,
            researchEvidence);
    }

    private static async Task<OfficialFplMultiSeasonShadowDocument?>
        ReadMultiSeasonShadowAsync(
            SqliteConnection connection,
            long captureId,
            int playerId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT forecast_artifact_id, document_json, content_sha256
            FROM multi_season_player_forecast_artifacts
            WHERE official_capture_id = $captureId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        long artifactId = reader.GetInt64(0);
        string documentJson = reader.GetString(1);
        string contentSha256 = reader.GetString(2);
        string calculatedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)));
        if (!StringComparer.Ordinal.Equals(contentSha256, calculatedHash))
        {
            throw new InvalidOperationException(
                "The persisted multi-season shadow content hash is invalid.");
        }

        MultiSeasonPlayerForecastDocument document =
            JsonSerializer.Deserialize<MultiSeasonPlayerForecastDocument>(
                documentJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException(
                "The persisted multi-season shadow could not be read.");
        MultiSeasonPlayerForecastPlayerDocument? player =
            document.Players.SingleOrDefault(item => item.PlayerId == playerId);
        return player is null
            ? null
            : new(
                document.ModelKey,
                document.Status,
                player.ExpectedPoints,
                player.BaselineV0ExpectedPoints,
                player.DifferenceFromBaselineV0,
                player.AvailabilityStatus,
                player.HistoricalIdentityStatus,
                player.HistoricalSeasonCodes,
                document.DistributionStatus,
                document.Comparison.MatchedCurrentSeasonTreeMaeImprovementFraction,
                document.Comparison.MatchedCurrentSeasonTreeFoldWins,
                document.Comparison.MatchedCurrentSeasonTreeFoldCount,
                document.InfluencesAdvice,
                artifactId,
                contentSha256);
    }

    private static async Task<OfficialFplPreseasonChallengerDocument?>
        ReadPreseasonChallengerAsync(
            SqliteConnection connection,
            long captureId,
            int playerId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                forecast_artifact_id,
                document_json,
                content_sha256
            FROM preseason_player_forecast_artifacts
            WHERE official_capture_id = $captureId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        long artifactId = reader.GetInt64(0);
        string documentJson = reader.GetString(1);
        string contentSha256 = reader.GetString(2);
        string calculatedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)));
        if (!StringComparer.Ordinal.Equals(contentSha256, calculatedHash))
        {
            throw new InvalidOperationException(
                "The persisted preseason challenger content hash is invalid.");
        }

        PreseasonPlayerForecastDocument document =
            JsonSerializer.Deserialize<PreseasonPlayerForecastDocument>(
                documentJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException(
                "The persisted preseason challenger could not be read.");
        PreseasonPlayerForecastPlayerDocument? player =
            document.Players.SingleOrDefault(item => item.PlayerId == playerId);
        return player is null
            ? null
            : new(
                document.ModelKey,
                player.ExpectedPoints,
                player.BaselineV0ExpectedPoints,
                player.DifferenceFromBaselineV0,
                player.AvailabilityStatus,
                player.PriorSeasonIdentityStatus,
                document.DistributionStatus,
                document.Comparison.LockedHoldoutMaeImprovementFraction,
                document.InfluencesAdvice,
                artifactId,
                contentSha256);
    }

    private static async Task<PlayerIdentityRow?> ReadIdentityAsync(
        SqliteConnection connection,
        long captureId,
        int playerId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                player.player_id,
                player.code,
                player.first_name,
                player.second_name,
                player.web_name,
                player.team_id,
                team.name,
                team.short_name,
                player.position,
                player.price_tenths,
                player.status,
                player.news,
                player.photo_identifier,
                player.expected_points_next
            FROM official_fpl_players AS player
            INNER JOIN official_fpl_teams AS team
                ON team.capture_id = player.capture_id
               AND team.team_id = player.team_id
            WHERE player.capture_id = $captureId
              AND player.player_id = $playerId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$playerId", playerId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13)
                    ? null
                    : decimal.Parse(
                        reader.GetString(13),
                        CultureInfo.InvariantCulture))
            : null;
    }

    private static async Task<IReadOnlyList<OutcomeRow>> ReadOutcomeRowsAsync(
        SqliteConnection connection,
        string seasonCode,
        int targetGameweek,
        DateTimeOffset cutoffUtc,
        int playerCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH ranked_outcomes AS (
                SELECT
                    capture.outcome_capture_id,
                    capture.gameweek,
                    capture.available_at_utc,
                    capture.reference_capture_id,
                    player.team_id,
                    outcome.minutes,
                    outcome.starts,
                    outcome.total_points,
                    outcome.goals_scored,
                    outcome.assists,
                    outcome.clean_sheets,
                    outcome.goals_conceded,
                    outcome.saves,
                    outcome.bonus,
                    outcome.yellow_cards,
                    outcome.red_cards,
                    outcome.own_goals,
                    outcome.penalties_saved,
                    outcome.penalties_missed,
                    outcome.bps,
                    outcome.influence,
                    outcome.creativity,
                    outcome.threat,
                    outcome.ict_index,
                    outcome.clearances_blocks_interceptions,
                    outcome.recoveries,
                    outcome.tackles,
                    outcome.defensive_contribution,
                    outcome.expected_goals,
                    outcome.expected_assists,
                    outcome.expected_goal_involvements,
                    outcome.expected_goals_conceded,
                    ROW_NUMBER() OVER (
                        PARTITION BY capture.gameweek
                        ORDER BY
                            capture.available_at_utc DESC,
                            capture.outcome_capture_id DESC
                    ) AS correction_rank
                FROM official_fpl_outcome_captures AS capture
                INNER JOIN official_fpl_players AS player
                    ON player.capture_id = capture.reference_capture_id
                   AND player.code = $playerCode
                INNER JOIN official_fpl_player_outcomes AS outcome
                    ON outcome.outcome_capture_id = capture.outcome_capture_id
                   AND outcome.reference_capture_id = capture.reference_capture_id
                   AND outcome.player_id = player.player_id
                WHERE capture.season_code = $seasonCode
                  AND capture.gameweek < $targetGameweek
                  AND capture.available_at_utc <= $cutoffUtc
            )
            SELECT
                outcome_capture_id,
                gameweek,
                available_at_utc,
                reference_capture_id,
                team_id,
                minutes,
                starts,
                total_points,
                goals_scored,
                assists,
                clean_sheets,
                goals_conceded,
                saves,
                bonus,
                yellow_cards,
                red_cards,
                own_goals,
                penalties_saved,
                penalties_missed,
                bps,
                influence,
                creativity,
                threat,
                ict_index,
                clearances_blocks_interceptions,
                recoveries,
                tackles,
                defensive_contribution,
                expected_goals,
                expected_assists,
                expected_goal_involvements,
                expected_goals_conceded
            FROM ranked_outcomes
            WHERE correction_rank = 1
            ORDER BY gameweek DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$playerCode", playerCode);
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$targetGameweek", targetGameweek);
        command.Parameters.AddWithValue("$cutoffUtc", FormatUtc(cutoffUtc));
        command.Parameters.AddWithValue("$limit", RecentOutcomeLimit);

        var results = new List<OutcomeRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    ParseUtc(reader.GetString(2)),
                    reader.GetInt64(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    reader.GetInt32(14),
                    reader.GetInt32(15),
                    ReadNullableInt32(reader, 16),
                    ReadNullableInt32(reader, 17),
                    ReadNullableInt32(reader, 18),
                    ReadNullableInt32(reader, 19),
                    ReadNullableDecimal(reader, 20),
                    ReadNullableDecimal(reader, 21),
                    ReadNullableDecimal(reader, 22),
                    ReadNullableDecimal(reader, 23),
                    ReadNullableInt32(reader, 24),
                    ReadNullableInt32(reader, 25),
                    ReadNullableInt32(reader, 26),
                    ReadNullableInt32(reader, 27),
                    ReadNullableDecimal(reader, 28),
                    ReadNullableDecimal(reader, 29),
                    ReadNullableDecimal(reader, 30),
                    ReadNullableDecimal(reader, 31)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<OfficialFplPlayerFixtureDocument>>
        ReadFixturesAsync(
            SqliteConnection connection,
            long captureId,
            int teamId,
            int firstGameweek,
            int lastGameweek,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                fixture.fixture_id,
                fixture.event_id,
                fixture.kickoff_utc,
                opponent.team_id,
                opponent.name,
                opponent.short_name,
                CASE WHEN fixture.home_team_id = $teamId THEN 1 ELSE 0 END,
                fixture.started,
                fixture.finished
            FROM official_fpl_fixtures AS fixture
            INNER JOIN official_fpl_teams AS opponent
                ON opponent.capture_id = fixture.capture_id
               AND opponent.team_id = CASE
                    WHEN fixture.home_team_id = $teamId
                    THEN fixture.away_team_id
                    ELSE fixture.home_team_id
               END
            WHERE fixture.capture_id = $captureId
              AND fixture.event_id BETWEEN $firstGameweek AND $lastGameweek
              AND (
                    fixture.home_team_id = $teamId
                    OR fixture.away_team_id = $teamId
              )
            ORDER BY fixture.event_id, fixture.kickoff_utc, fixture.fixture_id;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$teamId", teamId);
        command.Parameters.AddWithValue("$firstGameweek", firstGameweek);
        command.Parameters.AddWithValue("$lastGameweek", lastGameweek);

        var results = new List<OfficialFplPlayerFixtureDocument>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : ParseUtc(reader.GetString(2)),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<EvidenceRow>> ReadEvidenceRowsAsync(
        SqliteConnection connection,
        string seasonCode,
        int targetGameweek,
        DateTimeOffset cutoffUtc,
        int playerCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                claim.claim_id,
                claim.source_key,
                claim.canonical_url,
                claim.author,
                claim.available_at_utc,
                claim.claim_type,
                claim.availability_status,
                claim.start_status,
                claim.forecast_probability,
                claim.expected_minutes,
                claim.role,
                claim.directness,
                claim.source_span,
                claim.extraction_version,
                claim.extraction_confidence,
                claim.duplicate_cluster_key
            FROM evidence_claims AS claim
            INNER JOIN official_fpl_players AS claim_identity
                ON claim_identity.capture_id = claim.identity_capture_id
               AND claim_identity.player_id = claim.player_id
            WHERE claim.status = 'quarantined'
              AND claim.season_code = $seasonCode
              AND claim.gameweek = $targetGameweek
              AND claim.available_at_utc <= $cutoffUtc
              AND claim_identity.code = $playerCode
            ORDER BY claim.available_at_utc DESC, claim.claim_id DESC;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$targetGameweek", targetGameweek);
        command.Parameters.AddWithValue("$cutoffUtc", FormatEvidenceUtc(cutoffUtc));
        command.Parameters.AddWithValue("$playerCode", playerCode);

        var results = new List<EvidenceRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    ParseEvidenceUtc(reader.GetString(4)),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    ReadNullableDecimal(reader, 8),
                    ReadNullableInt32(reader, 9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetDecimal(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15)));
        }

        return results;
    }

    private static OfficialFplPlayerResearchEvidenceDocument CreateResearchEvidence(
        IReadOnlyList<EvidenceRow> rows,
        DateTimeOffset cutoffUtc)
    {
        HashSet<string> dependentClusters = rows
            .Where(row => row.DuplicateClusterKey is not null)
            .GroupBy(row => row.DuplicateClusterKey!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        bool hasAvailabilityContradiction = rows
            .Where(row => row.AvailabilityStatus is not null)
            .Select(row => row.AvailabilityStatus!)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
        bool hasStartContradiction = rows
            .Where(row => row.StartStatus is not null)
            .Select(row => row.StartStatus!)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
        OfficialFplPlayerResearchClaimDocument[] claims = rows
            .Select(row =>
                new OfficialFplPlayerResearchClaimDocument(
                    row.ClaimId,
                    row.SourceKey,
                    row.CanonicalUrl,
                    row.Author,
                    row.AvailableAtUtc,
                    Math.Max(
                        0L,
                        (long)Math.Floor(
                            (cutoffUtc - row.AvailableAtUtc).TotalSeconds)),
                    row.ClaimType,
                    row.AvailabilityStatus,
                    row.StartStatus,
                    row.ForecastProbability,
                    row.ExpectedMinutes,
                    row.Role,
                    row.Directness,
                    row.SourceSpan,
                    row.ExtractionVersion,
                    row.ExtractionConfidence,
                    row.DuplicateClusterKey,
                    row.DuplicateClusterKey is not null
                        && dependentClusters.Contains(row.DuplicateClusterKey)))
            .ToArray();

        return new(
            "quarantined-not-used",
            false,
            claims.Length,
            claims
                .Select(claim => claim.SourceKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            dependentClusters.Count,
            hasAvailabilityContradiction || hasStartContradiction,
            claims);
    }

    public static string? CreatePhotoUrl(string? photoIdentifier)
    {
        if (string.IsNullOrEmpty(photoIdentifier))
        {
            return null;
        }

        int extensionIndex = photoIdentifier.LastIndexOf('.');
        if (extensionIndex <= 0
            || !int.TryParse(
                photoIdentifier.AsSpan(0, extensionIndex),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int photoCode)
            || photoCode <= 0)
        {
            return null;
        }

        return $"{PhotoBaseUrl}p{photoCode}.png";
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string FormatEvidenceUtc(DateTimeOffset value) =>
        value
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static DateTimeOffset ParseEvidenceUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static int? ReadNullableInt32(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static decimal? ReadNullableDecimal(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private sealed record PlayerIdentityRow(
        int PlayerId,
        int PlayerCode,
        string FirstName,
        string SecondName,
        string WebName,
        int TeamId,
        string TeamName,
        string TeamShortName,
        string Position,
        int PriceTenths,
        string Status,
        string News,
        string? PhotoIdentifier,
        decimal? ExpectedPointsNext);

    private sealed record OutcomeRow(
        long OutcomeCaptureId,
        int Gameweek,
        DateTimeOffset AvailableAtUtc,
        long ReferenceCaptureId,
        int TeamId,
        int Minutes,
        int Starts,
        int TotalPoints,
        int GoalsScored,
        int Assists,
        int CleanSheets,
        int GoalsConceded,
        int Saves,
        int Bonus,
        int YellowCards,
        int RedCards,
        int? OwnGoals,
        int? PenaltiesSaved,
        int? PenaltiesMissed,
        int? Bps,
        decimal? Influence,
        decimal? Creativity,
        decimal? Threat,
        decimal? IctIndex,
        int? ClearancesBlocksInterceptions,
        int? Recoveries,
        int? Tackles,
        int? DefensiveContribution,
        decimal? ExpectedGoals,
        decimal? ExpectedAssists,
        decimal? ExpectedGoalInvolvements,
        decimal? ExpectedGoalsConceded);

    private sealed record EvidenceRow(
        long ClaimId,
        string SourceKey,
        string CanonicalUrl,
        string? Author,
        DateTimeOffset AvailableAtUtc,
        string ClaimType,
        string? AvailabilityStatus,
        string? StartStatus,
        decimal? ForecastProbability,
        int? ExpectedMinutes,
        string? Role,
        string Directness,
        string SourceSpan,
        string ExtractionVersion,
        decimal ExtractionConfidence,
        string? DuplicateClusterKey);
}
