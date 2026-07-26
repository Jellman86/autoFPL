using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class OfficialFplExpectedPointsEvaluationStore
{
    public const string EvaluatorVersion =
        "official-fpl-published-expected-points-evaluation-v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;

    public OfficialFplExpectedPointsEvaluationStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OfficialFplExpectedPointsEvaluationDocument> EvaluateAsync(
        string? seasonCode = null,
        CancellationToken cancellationToken = default)
    {
        if (seasonCode is not null
            && (string.IsNullOrWhiteSpace(seasonCode) || seasonCode.Length > 16))
        {
            throw new ArgumentException(
                "Season code must contain between 1 and 16 characters.",
                nameof(seasonCode));
        }

        await using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder(_options.ConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        await connection.OpenAsync(cancellationToken);
        IReadOnlyList<OutcomeHeader> outcomes = await ReadLatestOutcomesAsync(
            connection,
            seasonCode,
            cancellationToken);
        var folds = new List<OfficialFplExpectedPointsFoldDocument>();
        var exclusions = new List<OfficialFplExpectedPointsExclusionDocument>();
        var allSamples = new List<EvaluationSample>();

        foreach (OutcomeHeader outcome in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ForecastHeader? forecast = await ReadForecastAsync(
                connection,
                outcome.SeasonCode,
                outcome.Gameweek,
                cancellationToken);
            if (forecast is null)
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        null,
                        "no-pre-deadline-next-gameweek-capture"));
                continue;
            }

            if (outcome.AvailableAtUtc <= forecast.DeadlineUtc)
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        forecast.CaptureId,
                        "official-outcome-chronology-invalid"));
                continue;
            }

            IReadOnlyList<SampleRow> rows = await ReadSamplesAsync(
                connection,
                forecast.CaptureId,
                outcome.ReferenceCaptureId,
                outcome.OutcomeCaptureId,
                cancellationToken);
            if (rows.Count != forecast.PlayerCount
                || rows.Any(row => row.ActualPoints is null))
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        forecast.CaptureId,
                        "official-outcome-player-coverage-incomplete"));
                continue;
            }

            if (rows.Any(row => row.ExpectedPoints is null))
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        forecast.CaptureId,
                        "published-expected-points-incomplete"));
                continue;
            }

            List<EvaluationSample> foldSamples = rows
                .Select(
                    row => new EvaluationSample(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        row.PlayerId,
                        row.Position,
                        (double)row.ExpectedPoints!.Value,
                        row.ActualPoints!.Value,
                        row.Minutes!.Value))
                .ToList();
            allSamples.AddRange(foldSamples);
            folds.Add(
                new(
                    outcome.SeasonCode,
                    outcome.Gameweek,
                    forecast.DeadlineUtc,
                    forecast.CaptureId,
                    forecast.AvailableAtUtc,
                    forecast.BootstrapSha256,
                    forecast.FixturesSha256,
                    outcome.OutcomeCaptureId,
                    outcome.AvailableAtUtc,
                    outcome.LiveSha256,
                    foldSamples.Count,
                    CalculateMetrics(foldSamples),
                    CalculateOptionalMetrics(
                        foldSamples.Where(sample => sample.Minutes == 0)),
                    PositionSlices(foldSamples)));
        }

        folds.Sort(
            (left, right) =>
            {
                int season = StringComparer.Ordinal.Compare(
                    left.SeasonCode,
                    right.SeasonCode);
                return season != 0 ? season : left.Gameweek.CompareTo(right.Gameweek);
            });
        exclusions.Sort(
            (left, right) =>
            {
                int season = StringComparer.Ordinal.Compare(
                    left.SeasonCode,
                    right.SeasonCode);
                return season != 0 ? season : left.Gameweek.CompareTo(right.Gameweek);
            });
        string status = folds.Count == 0 ? "insufficient-data" : "complete";
        string? reason = folds.Count == 0
            ? "no-complete-forecast-outcome-pairs"
            : null;
        OfficialFplExpectedPointsMetricsDocument? metrics =
            CalculateOptionalMetrics(allSamples);
        OfficialFplExpectedPointsMetricsDocument? zeroMinuteMetrics =
            CalculateOptionalMetrics(
                allSamples.Where(sample => sample.Minutes == 0));
        IReadOnlyList<OfficialFplExpectedPointsSliceDocument> positionSlices =
            PositionSlices(allSamples);
        string dataIdentity = Hash(
            new
            {
                seasonCode,
                folds = folds.Select(
                    fold => new
                    {
                        fold.SeasonCode,
                        fold.Gameweek,
                        fold.ForecastCaptureId,
                        fold.ForecastBootstrapSha256,
                        fold.ForecastFixturesSha256,
                        fold.OutcomeCaptureId,
                        fold.OutcomeLiveSha256,
                    }),
                exclusions,
            });
        string runIdentity = Hash(
            new
            {
                evaluatorVersion = EvaluatorVersion,
                researchStatus = "exploratory-external-baseline-not-promoted",
                dataIdentitySha256 = dataIdentity,
                status,
                reason,
                folds,
                metrics,
                zeroMinuteMetrics,
                positionSlices,
            });
        return new(
            "1.0",
            EvaluatorVersion,
            "exploratory-external-baseline-not-promoted",
            status,
            reason,
            seasonCode,
            outcomes.Count,
            folds.Count,
            dataIdentity,
            runIdentity,
            metrics,
            zeroMinuteMetrics,
            positionSlices,
            folds,
            exclusions);
    }

    private static IReadOnlyList<OfficialFplExpectedPointsSliceDocument> PositionSlices(
        IEnumerable<EvaluationSample> source) =>
        source
            .GroupBy(sample => sample.Position)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(
                group => new OfficialFplExpectedPointsSliceDocument(
                    group.Key,
                    CalculateMetrics(group)))
            .ToList();

    private static OfficialFplExpectedPointsMetricsDocument? CalculateOptionalMetrics(
        IEnumerable<EvaluationSample> source)
    {
        List<EvaluationSample> samples = source.ToList();
        return samples.Count == 0 ? null : CalculateMetrics(samples);
    }

    private static OfficialFplExpectedPointsMetricsDocument CalculateMetrics(
        IEnumerable<EvaluationSample> source)
    {
        List<EvaluationSample> samples = source.ToList();
        if (samples.Count == 0)
        {
            throw new InvalidOperationException(
                "Expected-points evaluation metrics require at least one sample.");
        }

        double mae = samples.Average(
            sample => Math.Abs(sample.Predicted - sample.Actual));
        double mse = samples.Average(
            sample => Math.Pow(sample.Predicted - sample.Actual, 2));
        double bias = samples.Average(
            sample => sample.Predicted - sample.Actual);
        return new(samples.Count, Round(mae), Round(Math.Sqrt(mse)), Round(bias));
    }

    private static async Task<IReadOnlyList<OutcomeHeader>> ReadLatestOutcomesAsync(
        SqliteConnection connection,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                outcome.outcome_capture_id,
                outcome.season_code,
                outcome.gameweek,
                outcome.reference_capture_id,
                outcome.available_at_utc,
                outcome.live_sha256
            FROM official_fpl_outcome_captures AS outcome
            WHERE ($seasonCode IS NULL OR outcome.season_code = $seasonCode)
              AND NOT EXISTS (
                  SELECT 1
                  FROM official_fpl_outcome_captures AS newer
                  WHERE newer.season_code = outcome.season_code
                    AND newer.gameweek = outcome.gameweek
                    AND (
                        julianday(newer.available_at_utc)
                            > julianday(outcome.available_at_utc)
                        OR (
                            julianday(newer.available_at_utc)
                                = julianday(outcome.available_at_utc)
                            AND newer.outcome_capture_id
                                > outcome.outcome_capture_id
                        )
                    )
              )
            ORDER BY outcome.season_code, outcome.gameweek;
            """;
        command.Parameters.AddWithValue(
            "$seasonCode",
            seasonCode is null ? DBNull.Value : seasonCode);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<OutcomeHeader>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt64(3),
                    ParseTimestamp(reader.GetString(4)),
                    reader.GetString(5)));
        }

        return results;
    }

    private static async Task<ForecastHeader?> ReadForecastAsync(
        SqliteConnection connection,
        string seasonCode,
        int gameweek,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture.capture_id,
                capture.available_at_utc,
                event.deadline_utc,
                capture.bootstrap_sha256,
                capture.fixtures_sha256,
                capture.player_count
            FROM official_fpl_captures AS capture
            INNER JOIN official_fpl_events AS event
                ON event.capture_id = capture.capture_id
               AND event.event_id = $gameweek
            WHERE capture.season_code = $seasonCode
              AND capture.next_gameweek_number = $gameweek
              AND julianday(capture.available_at_utc)
                    <= julianday(event.deadline_utc)
            ORDER BY
                julianday(capture.available_at_utc) DESC,
                capture.capture_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$seasonCode", seasonCode);
        command.Parameters.AddWithValue("$gameweek", gameweek);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetInt64(0),
                ParseTimestamp(reader.GetString(1)),
                ParseTimestamp(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5))
            : null;
    }

    private static async Task<IReadOnlyList<SampleRow>> ReadSamplesAsync(
        SqliteConnection connection,
        long forecastCaptureId,
        long outcomeReferenceCaptureId,
        long outcomeCaptureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                forecast.player_id,
                forecast.position,
                forecast.expected_points_next,
                outcome.total_points,
                outcome.minutes
            FROM official_fpl_players AS forecast
            LEFT JOIN official_fpl_players AS outcome_identity
                ON outcome_identity.capture_id = $outcomeReferenceCaptureId
               AND outcome_identity.code = forecast.code
            LEFT JOIN official_fpl_player_outcomes AS outcome
                ON outcome.outcome_capture_id = $outcomeCaptureId
               AND outcome.reference_capture_id = $outcomeReferenceCaptureId
               AND outcome.player_id = outcome_identity.player_id
            WHERE forecast.capture_id = $forecastCaptureId
            ORDER BY forecast.player_id;
            """;
        command.Parameters.AddWithValue("$forecastCaptureId", forecastCaptureId);
        command.Parameters.AddWithValue(
            "$outcomeReferenceCaptureId",
            outcomeReferenceCaptureId);
        command.Parameters.AddWithValue("$outcomeCaptureId", outcomeCaptureId);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SampleRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2)
                        ? null
                        : decimal.Parse(
                            reader.GetString(2),
                            CultureInfo.InvariantCulture),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        }

        return results;
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static double Round(double value) =>
        Math.Round(value, 6, MidpointRounding.ToEven);

    private sealed record OutcomeHeader(
        long OutcomeCaptureId,
        string SeasonCode,
        int Gameweek,
        long ReferenceCaptureId,
        DateTimeOffset AvailableAtUtc,
        string LiveSha256);

    private sealed record ForecastHeader(
        long CaptureId,
        DateTimeOffset AvailableAtUtc,
        DateTimeOffset DeadlineUtc,
        string BootstrapSha256,
        string FixturesSha256,
        int PlayerCount);

    private sealed record SampleRow(
        int PlayerId,
        string Position,
        decimal? ExpectedPoints,
        int? ActualPoints,
        int? Minutes);

    private sealed record EvaluationSample(
        string SeasonCode,
        int Gameweek,
        int PlayerId,
        string Position,
        double Predicted,
        int Actual,
        int Minutes);
}
